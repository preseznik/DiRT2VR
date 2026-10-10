Imports System.Text.Json
Imports System.Xml.Linq
Imports DiRT2VR.CustomTracks

' One capture directory per game process; only complete, atomically published
' native files enter compression. Disk work runs away from the polling/UI thread.
Public NotInheritable Class ReplaySession
    Implements IDisposable
    Private ReadOnly context As InstallContext
    Private ReadOnly manifest As ReplayManifest
    Private ReadOnly folder As String
    Private ReadOnly playback As Boolean
    Private ReadOnly autoSave As Boolean
    Private ReadOnly seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private work As Tasks.Task
    Public Property Warning As String = ""

    Public Sub New(value As InstallContext, settings As VrSettings, start As ProcessStartInfo, prepared As PreparedReplay)
        context = value
        If Not settings.DirectMode Then Return
        playback = prepared IsNot Nothing
        If playback Then
            folder = prepared.Folder
        Else
            Dim configPath = IO.Path.Combine(context.GameRoot, New AssetTransaction(context).PracticeConfig())
            Dim xml = XDocument.Parse(Text.Encoding.UTF8.GetString(Files.ReadBytes(configPath)))
            manifest = New ReplayManifest With {
                .Name = RaceCatalog.Current.Track(settings.TrackId).Label & " — " & RaceCatalog.Current.Car(settings.CarCode).Label,
                .Laps = settings.SessionLaps, .LaunchConfig = xml.ToString(), .Content = ReplayContent.Capture(context, settings.TrackId, xml)}
            If manifest.Name.Length > 100 Then manifest.Name = manifest.Name.Substring(0, 100)
            folder = IO.Path.Combine(context.UserRoot, "replay-recordings", Guid.NewGuid().ToString("N"))
            Files.NoLinks(folder) : Directory.CreateDirectory(folder)
            Files.SaveJson(IO.Path.Combine(folder, "event.json"), manifest)
            autoSave = ReplayPreferences.Load(context).AutoSave
        End If
        start.Environment("DIRT2VR_REPLAY_DIRECTORY") = folder
        start.Environment("DIRT2VR_REPLAY_MODE") = If(playback, "load", "save")
    End Sub
    Public Sub Poll()
        If folder Is Nothing Then Return
        If work IsNot Nothing Then
            If Not work.IsCompleted Then Return
            Try : work.GetAwaiter().GetResult()
            Catch ex As Exception : Warning = "Replay was not saved: " & ex.Message
            End Try
            work = Nothing
        End If
        Dim errorPath = IO.Path.Combine(folder, "error.txt")
        If File.Exists(errorPath) Then
            Try
                Files.NoLinks(errorPath)
                Using input As New FileStream(errorPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    If input.Length <= 4096 Then
                        Using reader As New StreamReader(input) : Warning = reader.ReadToEnd() : End Using
                    End If
                End Using
            Catch ex As IOException
                ' The native writer may still have the status file open.
            End Try
        End If
        If playback Then Return
        ' Native attempt numbers have no padding: 10 must follow 9, not 1.
        Dim nextFile = Directory.EnumerateFiles(folder, "attempt-*.bin").OrderBy(Function(p) p.Length).ThenBy(Function(p) p, StringComparer.Ordinal).FirstOrDefault(Function(p) Not seen.Contains(p))
        If nextFile Is Nothing Then Return
        seen.Add(nextFile)
        work = Tasks.Task.Run(Sub()
                                  Files.NoLinks(nextFile)
                                  Dim snapshot = JsonSerializer.Deserialize(Of ReplayManifest)(JsonSerializer.SerializeToUtf8Bytes(manifest, ReplayArchive.Json), ReplayArchive.Json)
                                  snapshot.RecordedUtc = New DateTimeOffset(File.GetLastWriteTimeUtc(nextFile))
                                  Call (New ReplayLibrary(context)).SaveLatest(nextFile, snapshot, autoSave)
                                  SafeFiles.DeleteOwned(nextFile)
                              End Sub)
    End Sub
    Public Sub Complete()
        If folder Is Nothing Then Return
        Do
            Poll()
            If work Is Nothing Then Exit Do
            Try : work.GetAwaiter().GetResult()
            Catch ex As Exception
                ' Poll reports and retains the failed recording on the next iteration.
            End Try
        Loop
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Complete()
        If folder Is Nothing OrElse playback Then Return
        ' Retain failures with their event details for recovery; no broad deletion.
        If Directory.EnumerateFiles(folder).Any(Function(p) IO.Path.GetFileName(p) <> "event.json") Then Return
        SafeFiles.DeleteOwned(IO.Path.Combine(folder, "event.json"))
        Directory.Delete(folder, False)
    End Sub

    Public Shared Function ReadPrepared(context As InstallContext, id As String) As ReplayManifest
        Dim parsed As Guid
        If Not Guid.TryParseExact(id, "N", parsed) OrElse id <> id.ToLowerInvariant() Then Throw New IOException("Invalid prepared replay ID.")
        Dim path = SafeFiles.Inside(IO.Path.Combine(context.UserRoot, "replay-playback", id), "event.json")
        Dim info As New FileInfo(path)
        If Not info.Exists OrElse info.Length > 2 * 1024 * 1024 Then Throw New IOException("Prepared replay details are missing or too large.")
        Dim result = SafeFiles.ReadJson(Of ReplayManifest)(path)
        result.Validate() : ReplayContent.RequireAvailable(context, result.Content)
        Return result
    End Function
End Class
