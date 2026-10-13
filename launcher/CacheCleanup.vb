Imports System.Globalization
Imports System.Text.RegularExpressions
Imports DiRT2VR.CustomTracks

' Only named, disposable caches in this installation's user directory are eligible.
' Recovery and user-owned data are never cleanup targets. Failures never block play.
Public NotInheritable Class CacheCleanup
    Public Class Receipt
        Public Property CompletedUtc As DateTime
        Public Property FilesRemoved As Integer
        Public Property BytesRemoved As Long
        Public Property Skipped As String = ""
        Public Property Problems As New List(Of String)
    End Class
    Private Shared ReadOnly InstallerName As New Regex("^[0-9a-f]{32}-DiRT2VR-(\d+\.\d+\.\d+)-Setup\.exe(\.part)?$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private Const LeaseName As String = "cache-activity.lock"
    Private Const ReceiptName As String = "cache-cleanup.json"

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
    Public Shared Function Run(context As InstallContext) As Receipt
        Dim result As New Receipt
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
                            If previous.CompletedUtc <= now AndAlso previous.CompletedUtc > now.AddDays(-1) Then
                                result.Skipped = "Already checked today" : Return result
                            End If
                        End If
                        Dim watch = Stopwatch.StartNew()
                        CleanUpdates(context, now, result, watch)
                        CleanBuilds(context, now, result, watch)
                        CleanLogs(context, now, result, watch)
                        result.CompletedUtc = now
                        SafeFiles.WriteJson(receiptPath, result)
                    End Using
                Finally
                    guard.ReleaseMutex()
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
            SafeFiles.Inside(context.ModRoot, "bloom-session"), SafeFiles.Inside(context.ModRoot, "filter-session"),
            SafeFiles.Inside(context.ModRoot, "buttermilk-postprocess"), SafeFiles.Inside(context.ModRoot, "custom-track-session"),
            SafeFiles.Inside(context.ModRoot, "custom-track-install"), SafeFiles.Inside(context.ModRoot, "external-session")}
            SafeFiles.NoLinks(folder)
            If Directory.Exists(folder) AndAlso Directory.EnumerateFiles(folder, "*pending*.json", SearchOption.TopDirectoryOnly).Any() Then Return True
        Next
        Return False
    End Function
    Private Shared Sub CleanUpdates(context As InstallContext, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(context.UserRoot, "updates")
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
            If watch.Elapsed > TimeSpan.FromSeconds(5) Then Exit For
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
    Private Shared Sub CleanBuilds(context As InstallContext, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(context.UserRoot, "custom-track-builds")
        If Not Directory.Exists(root) Then Return
        For Each path In Directory.EnumerateDirectories(root).Take(1000)
            If watch.Elapsed > TimeSpan.FromSeconds(5) Then Exit For
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
    Private Shared Sub CleanLogs(context As InstallContext, now As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(context.UserRoot, "logs")
        If Not Directory.Exists(root) Then Return
        Dim sessions As New List(Of (Path As String, Stamp As DateTime))
        Dim entries = Directory.EnumerateDirectories(root).Take(20001).ToArray()
        If entries.Length > 20000 Then Return ' Retain the newest sessions across the complete inventory.
        For Each path In entries
            Dim stamp As DateTime
            If DateTime.TryParseExact(IO.Path.GetFileName(path), "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture, DateTimeStyles.None, stamp) Then sessions.Add((path, stamp))
        Next
        For Each item In sessions.OrderByDescending(Function(s) s.Stamp).Skip(10)
            If watch.Elapsed > TimeSpan.FromSeconds(5) Then Exit For
            If item.Stamp >= now.ToLocalTime().AddDays(-30) Then Continue For
            Try
                ' A timestamp-shaped directory alone is not enough to claim ownership.
                If Not {"trace.log", "dirt2vr-settings.json", "preflight.txt"}.Any(Function(n) File.Exists(IO.Path.Combine(item.Path, n))) Then Continue For
                RemoveOldTree(root, IO.Path.GetFileName(item.Path), now.AddDays(-30), result, watch)
            Catch ex As Exception
                Problem(result, ex)
            End Try
        Next
    End Sub
    Private Shared Sub RemoveOldTree(parent As String, name As String, cutoff As DateTime, result As Receipt, watch As Stopwatch)
        Dim root = SafeFiles.Inside(parent, name)
        Dim files As New List(Of String), directories As New Stack(Of String)
        directories.Push(root)
        Dim inspected As Integer
        ' Validate the whole candidate before deleting any file. Reject links, fresh
        ' children and locked files; never recurse into unrecognized cache roots.
        While directories.Count > 0
            If watch.Elapsed > TimeSpan.FromSeconds(5) OrElse files.Count > 25000 Then Return
            Dim folder = directories.Pop()
            SafeFiles.NoLinks(folder)
            If Directory.GetLastWriteTimeUtc(folder) >= cutoff Then Return
            For Each path In Directory.EnumerateFileSystemEntries(folder)
                inspected += 1
                If inspected > 25000 OrElse watch.Elapsed > TimeSpan.FromSeconds(5) Then Return
                SafeFiles.NoLinks(path)
                If Directory.Exists(path) Then
                    directories.Push(path)
                Else
                    If File.GetLastWriteTimeUtc(path) >= cutoff Then Return
                    Using probe As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)
                    End Using
                    files.Add(path)
                End If
            Next
        End While
        Dim bytes = files.Sum(Function(p) New FileInfo(p).Length)
        SafeFiles.DeleteWorkTree(parent, name)
        result.FilesRemoved += files.Count : result.BytesRemoved += bytes
    End Sub
    Private Shared Sub Problem(result As Receipt, ex As Exception)
        If result.Problems.Count < 20 Then result.Problems.Add(ex.Message)
    End Sub
End Class
