Imports System.Globalization
Imports System.Text.RegularExpressions
Imports DiRT2VR.CustomTracks

' Only named, disposable caches in DiRT2VR's user directories are eligible: this
' installation's and those left by other or removed game folders. Recovery and
' user-owned data (settings, replays, lap times) are never cleanup targets.
' Failures never block play.
Public NotInheritable Class CacheCleanup
    Public Class Receipt
        ' Cleanup rules that wrote this receipt. Older launchers wrote none (0),
        ' so a daily check from before an upgrade does not delay the new rules.
        Public Property Rules As Integer
        Public Property CompletedUtc As DateTime
        Public Property FilesRemoved As Integer
        Public Property BytesRemoved As Long
        Public Property Skipped As String = ""
        ' Work remained when the time budget ran out; the launcher retries soon.
        Public Property Incomplete As Boolean
        Public Property Problems As New List(Of String)
    End Class
    Private Shared ReadOnly InstallFolderName As New Regex("^[0-9A-F]{24}$", RegexOptions.CultureInvariant)
    Private Shared ReadOnly TimeBudget As TimeSpan = TimeSpan.FromSeconds(5)
    Private Const DirectUnusedDays As Integer = 14
    ' Removed on uninstall. Settings, replays, lap times and recovery data stay.
    Private Shared ReadOnly DisposableFolders As String() = {"logs", "Direct", "updates", "custom-track-builds", "custom-car-builds", "replay-playback"}
    Private Shared ReadOnly InstallerName As New Regex("^[0-9a-f]{32}-DiRT2VR-(\d+\.\d+\.\d+)-Setup\.exe(\.part)?$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private Const LeaseName As String = "cache-activity.lock"
    Private Const ReceiptName As String = "cache-cleanup.json"
    ' Raise when the rules change, so the next launch checks again at once.
    Private Const CurrentRules As Integer = 2

    ' A file lease survives async thread switches and releases automatically on crash.
    Public Shared Async Function AcquireUpdateLeaseAsync(context As InstallContext, token As CancellationToken) As Task(Of FileStream)
        Dim deadline = DateTime.UtcNow.AddSeconds(30)
        Do
            token.ThrowIfCancellationRequested()
            Try
                Return OpenLease(context)
            Catch ex As IOException
                If DateTime.UtcNow >= deadline Then Throw New IOException("Cache maintenance or another update is busy. Please retry shortly.", ex)
            End Try
            Await Task.Delay(100, token)
        Loop
    End Function
    Private Shared Function OpenLease(context As InstallContext) As FileStream
        Dim path = SafeFiles.Inside(context.UserRoot, LeaseName)
        Directory.CreateDirectory(context.UserRoot)
        Return New FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
    End Function
    Public Shared Function RunAsync(context As InstallContext) As Task(Of Receipt)
        Return Task.Run(Function() Run(context))
    End Function
    ' Cleanup holds the session mutex while it works, and a launch fails while that
    ' mutex is taken. Before launching, the launcher stops a running cleanup and
    ' pauses further runs for a while.
    Private Shared stopRequested As Integer
    Private Shared pausedUntilUtc As Long
    Private Shared ReadOnly idle As New ManualResetEventSlim(True)
    Public Shared Sub PauseForLaunch()
        Interlocked.Exchange(pausedUntilUtc, DateTime.UtcNow.AddMinutes(10).Ticks)
        Volatile.Write(stopRequested, 1)
        idle.Wait(TimeSpan.FromSeconds(3))
        Volatile.Write(stopRequested, 0)
    End Sub
    Private Shared Function OutOfTime(watch As Stopwatch) As Boolean
        Return watch.Elapsed > TimeBudget OrElse Volatile.Read(stopRequested) <> 0
    End Function
    Public Shared Function Run(context As InstallContext) As Receipt
        Dim result As New Receipt
        If DateTime.UtcNow.Ticks < Interlocked.Read(pausedUntilUtc) Then
            result.Skipped = "Paused for a launch" : result.Incomplete = True : Return result
        End If
        Try
            Using guard As New Mutex(False, "Global\DiRT2VR.Session")
                Dim held As Boolean
                Try
                    held = guard.WaitOne(0)
                Catch ex As AbandonedMutexException
                    held = True
                End Try
                If Not held Then
                    result.Skipped = "Session or build active" : Return result
                End If
                idle.Reset()
                Try
                    If ExternalOperationActive() Then
                        result.Skipped = "Game, installer or converter active" : Return result
                    End If
                    Using lease = OpenLease(context)
                        If RecoveryPending(context) Then
                            result.Skipped = "Recovery pending" : Return result
                        End If
                        Dim receiptPath = SafeFiles.Inside(context.UserRoot, ReceiptName)
                        Dim now = DateTime.UtcNow
                        If File.Exists(receiptPath) Then
                            Dim previous = SafeFiles.ReadJson(Of Receipt)(receiptPath)
                            If previous.Rules >= CurrentRules AndAlso previous.CompletedUtc <= now AndAlso previous.CompletedUtc > now.AddDays(-1) Then
                                result.Skipped = "Already checked today" : Return result
                            End If
                        End If
                        Dim watch = Stopwatch.StartNew()
                        For Each userRoot In InstallRoots(context)
                            If OutOfTime(watch) Then Exit For
                            Try
                                ' Another installation's recovery journal keeps its folder as it is.
                                If userRoot <> context.UserRoot AndAlso Directory.EnumerateFiles(userRoot, "*pending*.json", SearchOption.TopDirectoryOnly).Any() Then Continue For
                                CleanUpdates(userRoot, now, result, watch)
                                CleanBuilds(userRoot, now, result, watch)
                                CleanDirect(userRoot, now, result, watch)
                                CleanLogs(userRoot, now, result, watch)
                            Catch ex As Exception
                                Problem(result, ex)
                            End Try
                        Next
                        ' A large backlog is removed a few seconds at a time. Record the
                        ' daily check only once everything has been looked at.
                        result.Incomplete = OutOfTime(watch)
                        If Not result.Incomplete Then
                            result.Rules = CurrentRules
                            result.CompletedUtc = now
                            SafeFiles.WriteJson(receiptPath, result)
                        End If
                    End Using
                Finally
                    guard.ReleaseMutex()
                    idle.Set()
                End Try
            End Using
        Catch ex As Exception
            ' Locked paths, denied access and malformed metadata must never prompt
            ' for elevation or interfere with launcher startup/track installation.
            result.Skipped = "Cache cleanup deferred"
            Problem(result, ex)
        End Try
        Return result
    End Function
    Private Shared Function ExternalOperationActive() As Boolean
        Dim processes = Process.GetProcesses()
        Try
            For Each item In processes
                Dim name = item.ProcessName
                If name.Equals("dirt2", StringComparison.OrdinalIgnoreCase) OrElse name.Equals("dirt2_game", StringComparison.OrdinalIgnoreCase) OrElse
                    (name.Equals("DiRT2VR", StringComparison.OrdinalIgnoreCase) AndAlso item.Id <> Environment.ProcessId) OrElse
                    name.Contains("setup", StringComparison.OrdinalIgnoreCase) OrElse name.EndsWith("Converter", StringComparison.OrdinalIgnoreCase) Then Return True
            Next
            Return False
        Finally
            For Each item In processes
                item.Dispose()
            Next
        End Try
    End Function
    Private Shared Function RecoveryPending(context As InstallContext) As Boolean
        ' Include journals outside AppData; a stale Ready session.json proves nothing.
        For Each folder In {context.UserRoot, SafeFiles.Inside(context.ModRoot, "backups"), SafeFiles.Inside(context.ModRoot, "lan-backups"),
            SafeFiles.Inside(context.ModRoot, "dof-session"), SafeFiles.Inside(context.ModRoot, "bloom-session"), SafeFiles.Inside(context.ModRoot, "filter-session"), SafeFiles.Inside(context.ModRoot, "road-sheen-session"),
            SafeFiles.Inside(context.ModRoot, "buttermilk-postprocess"), SafeFiles.Inside(context.ModRoot, "custom-track-session"),
            SafeFiles.Inside(context.ModRoot, "custom-track-install"), SafeFiles.Inside(context.ModRoot, "external-session"), SafeFiles.Inside(context.ModRoot, "custom-car-install")}
            SafeFiles.NoLinks(folder)
            If Directory.Exists(folder) AndAlso Directory.EnumerateFiles(folder, "*pending*.json", SearchOption.TopDirectoryOnly).Any() Then Return True
        Next
        Return False
    End Function
    ' This installation first, then folders that other or removed game folders left behind.
    Private Shared Function InstallRoots(context As InstallContext) As List(Of String)
        Dim roots As New List(Of String) From {context.UserRoot}
        Dim parent = IO.Path.GetDirectoryName(context.UserRoot)
        If Not Directory.Exists(parent) Then Return roots
        SafeFiles.NoLinks(parent)
        For Each path In Directory.EnumerateDirectories(parent).Take(1000)
            If InstallFolderName.IsMatch(IO.Path.GetFileName(path)) AndAlso Not String.Equals(path, context.UserRoot, StringComparison.OrdinalIgnoreCase) AndAlso
                (File.GetAttributes(path) And FileAttributes.ReparsePoint) = 0 Then roots.Add(path)
        Next
        Return roots
    End Function
    ' Direct keeps a private ~10 GB copy of the game for later races. Remove it once
    ' unused for two weeks; the next Direct race prepares it again.
    Private Shared Sub CleanDirect(userRoot As String, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim direct = SafeFiles.Inside(userRoot, "Direct")
        If Not Directory.Exists(direct) OrElse Not Directory.Exists(IO.Path.Combine(direct, "game")) Then Return
        Dim cutoff = now.AddDays(-DirectUnusedDays)
        Dim lastUse = Directory.GetLastWriteTimeUtc(direct)
        Dim runs = IO.Path.Combine(direct, "runs")
        If Directory.Exists(runs) Then
            For Each race In Directory.EnumerateDirectories(runs).Take(10000)
                Dim stamp = Directory.GetLastWriteTimeUtc(race)
                If stamp > lastUse Then lastUse = stamp
            Next
        End If
        If lastUse >= cutoff Then Return
        RemoveOldTree(userRoot, "Direct", cutoff, result, watch)
    End Sub
    ' Uninstall removes this installation's caches; failures never block it.
    Public Shared Sub RemoveDisposable(context As InstallContext)
        For Each name In DisposableFolders
            Try
                SafeFiles.DeleteWorkTree(context.UserRoot, name)
            Catch
                ' Locked or linked folders stay; uninstalling continues.
            End Try
        Next
    End Sub
    Private Shared Sub CleanUpdates(userRoot As String, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(userRoot, "updates")
        If Not Directory.Exists(root) Then Return
        Dim installers As New List(Of FileInfo), partials As New List(Of FileInfo)
        Dim entries = Directory.EnumerateFiles(root).Take(10001).ToArray()
        If entries.Length > 10000 Then Return ' Do not choose a newest file from an incomplete inventory.
        For Each path In entries
            Dim match = InstallerName.Match(IO.Path.GetFileName(path))
            If Not match.Success OrElse Not SafeFiles.Version(match.Groups(1).Value) Then Continue For
            SafeFiles.NoLinks(path)
            If match.Groups(2).Success Then partials.Add(New FileInfo(path)) Else installers.Add(New FileInfo(path))
        Next
        ' Retain the most recent download (including a deliberate stable rollback),
        ' and leave every recent download alone during installer handoff.
        Dim expired = installers.OrderByDescending(Function(f) f.LastWriteTimeUtc).ThenBy(Function(f) f.Name).Skip(1).Concat(partials)
        For Each item In expired
            If OutOfTime(watch) Then Exit For
            If item.LastWriteTimeUtc >= now.AddDays(-1) Then Continue For
            Try
                SafeFiles.NoLinks(item.FullName)
                Using probe As New FileStream(item.FullName, FileMode.Open, FileAccess.Read, FileShare.None)
                End Using
                Dim size = New FileInfo(item.FullName).Length
                SafeFiles.DeleteOwned(item.FullName)
                result.FilesRemoved += 1 : result.BytesRemoved += size
            Catch ex As Exception
                Problem(result, ex)
            End Try
        Next
    End Sub
    Private Shared Sub CleanBuilds(userRoot As String, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(userRoot, "custom-track-builds")
        If Not Directory.Exists(root) Then Return
        For Each path In Directory.EnumerateDirectories(root).Take(1000)
            If OutOfTime(watch) Then Exit For
            Dim id As Guid
            If Not Guid.TryParseExact(IO.Path.GetFileName(path), "N", id) Then Continue For
            Try
                SafeFiles.NoLinks(path)
                ' Known old converters also extracted converter.zip beside conversion/.
                If Directory.EnumerateFileSystemEntries(path).Any(Function(p) Not {"conversion", "converter", "converter.zip"}.Contains(IO.Path.GetFileName(p))) Then Continue For
                If Not Directory.Exists(IO.Path.Combine(path, "conversion")) Then Continue For
                RemoveOldTree(root, IO.Path.GetFileName(path), now.AddDays(-1), result, watch)
            Catch ex As Exception
                Problem(result, ex)
            End Try
        Next
    End Sub
    Private Shared Sub CleanLogs(userRoot As String, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(userRoot, "logs")
        If Not Directory.Exists(root) Then Return
        Dim sessions As New List(Of (Path As String, Stamp As DateTime))
        Dim entries = Directory.EnumerateDirectories(root).Take(20001).ToArray()
        If entries.Length > 20000 Then Return ' Retain the newest sessions across the complete inventory.
        For Each path In entries
            Dim stamp As DateTime
            If DateTime.TryParseExact(IO.Path.GetFileName(path), "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, stamp) Then sessions.Add((path, stamp))
        Next
        ' Keep the newest sessions, then remove sessions older than 30 days or, once
        ' the newer ones fill the size budget, anything older than a day.
        Dim kept As Long, index As Integer, measured = True
        For Each item In sessions.OrderByDescending(Function(s) s.Stamp)
            If OutOfTime(watch) Then Exit For
            index += 1
            Try
                ' Once the newer sessions fill the budget, older ones need no measuring.
                Dim size = If(measured AndAlso kept > LogBudgetBytes, 0L, SessionBytes(item.Path))
                ' After an unmeasurable session only the age rule applies.
                If size < 0 Then measured = False : size = 0
                Dim expired = item.Stamp < now.ToLocalTime().AddDays(-30)
                Dim overBudget = measured AndAlso kept + size > LogBudgetBytes
                ' A timestamp-shaped directory alone is not enough to claim ownership.
                If index <= KeptLogSessions OrElse Not (expired OrElse overBudget) OrElse
                    Not {"trace.log", "dirt2vr-settings.json", "preflight.txt", "custom-build-error.json"}.Any(Function(n) File.Exists(IO.Path.Combine(item.Path, n))) Then
                    kept += size : Continue For
                End If
                RemoveOldTree(root, IO.Path.GetFileName(item.Path), If(expired, now.AddDays(-30), now.AddDays(-1)), result, watch)
            Catch ex As Exception
                Problem(result, ex)
            End Try
        Next
    End Sub
    Private Const KeptLogSessions As Integer = 5
    Private Const LogBudgetBytes As Long = 2L * 1024 * 1024 * 1024
    Private Shared Function SessionBytes(path As String) As Long
        SafeFiles.NoLinks(path)
        Dim options As New EnumerationOptions With {.RecurseSubdirectories = True, .AttributesToSkip = FileAttributes.ReparsePoint, .IgnoreInaccessible = True}
        Dim total As Long, count As Integer
        For Each file In New DirectoryInfo(path).EnumerateFiles("*", options)
            count += 1
            If count > 25000 Then Return -1
            total += file.Length
        Next
        Return total
    End Function
    Private Shared Sub RemoveOldTree(parent As String, name As String, cutoff As DateTime, result As Receipt, watch As Stopwatch)
        ' Resolves the root and rejects links anywhere on its path.
        Dim root = SafeFiles.Inside(parent, name)
        Dim files As New List(Of FileInfo), folders As New List(Of DirectoryInfo), pending As New Stack(Of DirectoryInfo)
        pending.Push(New DirectoryInfo(root))
        Dim inspected As Integer
        ' Validate the whole candidate before deleting any file. Reject links, fresh
        ' children and locked files; never recurse into unrecognized cache roots.
        ' Directory entries carry their own attributes and times, and every parent
        ' has already been checked, so each entry needs no further lookups.
        While pending.Count > 0
            If OutOfTime(watch) Then Return
            Dim folder = pending.Pop()
            If folder.LastWriteTimeUtc >= cutoff Then Return
            folders.Add(folder)
            For Each entry In folder.EnumerateFileSystemInfos()
                inspected += 1
                If inspected > 25000 OrElse OutOfTime(watch) Then Return
                If (entry.Attributes And FileAttributes.ReparsePoint) <> 0 Then Return
                Dim child = TryCast(entry, DirectoryInfo)
                If child IsNot Nothing Then pending.Push(child) : Continue For
                If entry.LastWriteTimeUtc >= cutoff Then Return
                Using probe As New FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.None)
                End Using
                files.Add(DirectCast(entry, FileInfo))
            Next
        End While
        Dim bytes As Long
        For Each file In files
            ' Older converters copied read-only source flags into their work folders.
            If (file.Attributes And FileAttributes.ReadOnly) <> 0 Then file.Attributes = FileAttributes.Normal
            bytes += file.Length
            file.Delete()
        Next
        For Each folder In folders.OrderByDescending(Function(d) d.FullName.Length)
            folder.Delete()
        Next
        result.FilesRemoved += files.Count : result.BytesRemoved += bytes
    End Sub
    Private Shared Sub Problem(result As Receipt, ex As Exception)
        If result.Problems.Count < 20 Then result.Problems.Add(ex.Message)
    End Sub
End Class
