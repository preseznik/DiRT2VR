Imports DiRT2VR.CustomTracks
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

Public Class CustomTrackPreferences
    Public Property Enabled As Boolean
    Public Property LayoutId As String = "aspen-lakeside"
    Public Property SourceFolder As String = ""
    Public Shared Function Load(context As InstallContext) As CustomTrackPreferences
        Dim path = IO.Path.Combine(context.UserRoot, "custom-tracks.json")
        Return If(File.Exists(path), Files.ReadJson(Of CustomTrackPreferences)(path), New CustomTrackPreferences())
    End Function
    Public Sub Save(context As InstallContext)
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "custom-tracks.json"), Me)
    End Sub
End Class

Public Module CustomTrackService
    Public Function RecoveryPending(context As InstallContext) As Boolean
        Return File.Exists(IO.Path.Combine(context.ModRoot, "custom-track-session/pending.json")) OrElse
            File.Exists(IO.Path.Combine(context.ModRoot, "custom-track-install/pending.json"))
    End Function
    Public Sub RequireLauncher(receipt As PackReceipt)
        If System.Version.Parse(receipt.MinimumLauncher) > System.Version.Parse(BuildInfo.Version) Then Throw New IOException("This Aspen pack needs DiRT2VR " & receipt.MinimumLauncher & " or later. Update the launcher before playing.")
    End Sub
    Public Function SourceFolders() As String()
        Dim result As New List(Of String)
        Dim libraries As List(Of String)
        Try
            libraries = Discovery.SteamLibraries()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
            Return Array.Empty(Of String)() ' Manual selection remains available.
        End Try
        For Each library In libraries
            Try
                Dim manifest = IO.Path.Combine(library, "steamapps", "appmanifest_321040.acf")
                If Not File.Exists(manifest) Then Continue For
                Dim match = Regex.Match(File.ReadAllText(manifest), """installdir""\s+""([^""]+)""")
                If Not match.Success Then Continue For
                SafeFiles.Relative(match.Groups(1).Value)
                Dim folder = SafeFiles.Inside(IO.Path.Combine(library, "steamapps/common"), match.Groups(1).Value)
                If Directory.Exists(IO.Path.Combine(folder, "tracks/locations/usa/aspen")) Then result.Add(folder)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
                ' An inaccessible or malformed library must not hide the folder picker.
            End Try
        Next
        Return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
    End Function
    Public Function Staging(context As InstallContext, id As String) As String
        Dim parsed As Guid
        If Not Guid.TryParseExact(id, "N", parsed) Then Throw New IOException("Invalid custom-track build identifier.")
        Return SafeFiles.Inside(context.UserRoot, "custom-track-builds/" & id)
    End Function
    Public Sub WithSessionLock(action As Action)
        Using guard As New Mutex(False, "Global\DiRT2VR.Session")
            Dim held As Boolean
            Try
                held = guard.WaitOne(0)
            Catch ex As AbandonedMutexException
                held = True
            End Try
            If Not held Then Throw New IOException("A game session or track installation is already running. Try again when it finishes.")
            Try
                action()
            Finally
                guard.ReleaseMutex()
            End Try
        End Using
    End Sub
    Public Function InstallAsync(context As InstallContext, offer As ConversionProfile, source As String, progress As IProgress(Of TrackProgress), cancel As CancellationToken) As Task
        Return Task.Run(Sub()
                            WithSessionLock(Sub() Install(context, offer, source, progress, cancel))
                        End Sub, cancel)
    End Function
    Private Sub Install(context As InstallContext, offer As ConversionProfile, source As String, progress As IProgress(Of TrackProgress), cancel As CancellationToken)
        context.ValidateGame() : context.RequireClosed()
        Worker.Invoke(context, "recover")
        AspenPack.Validate(offer, BuildInfo.Version)
        Dim id = Guid.NewGuid().ToString("N"), stage = Staging(context, id)
        Directory.CreateDirectory(stage)
        Try
        If New DriveInfo(IO.Path.GetPathRoot(stage)).AvailableFreeSpace < offer.StagingBytes Then Throw New IOException("Not enough free space for conversion. Free at least " & Math.Ceiling(offer.StagingBytes / 1073741824.0).ToString() & " GB on " & IO.Path.GetPathRoot(stage))
        If New DriveInfo(IO.Path.GetPathRoot(context.GameRoot)).AvailableFreeSpace < offer.InstalledBytes * 2 Then Throw New IOException("Not enough free space beside DiRT 2 for the track and its rollback copy.")
        AspenPack.VerifySources(offer, context.GameRoot, source, progress, cancel)
        cancel.ThrowIfCancellationRequested()
        context.RequireClosed()
        Dim start As New ProcessStartInfo(Environment.ProcessPath) With {
            .UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = stage, .RedirectStandardOutput = True, .RedirectStandardError = True}
        For Each argument In {"--convert-aspen", IO.Path.GetFullPath(source), context.GameRoot, IO.Path.Combine(stage, "conversion")}
            start.ArgumentList.Add(argument)
        Next
        Using child = Process.Start(start)
            Dim errors = child.StandardError.ReadToEndAsync()
            AddHandler child.OutputDataReceived, Sub(sender, e)
                                                     If String.IsNullOrEmpty(e.Data) OrElse Not e.Data.StartsWith("{"c) Then Return
                                                     Try
                                                         Dim value = JsonSerializer.Deserialize(Of TrackProgress)(e.Data)
                                                         If value IsNot Nothing Then progress.Report(value)
                                                     Catch ex As JsonException
                                                     End Try
                                                 End Sub
            child.BeginOutputReadLine()
            Using registration = cancel.Register(Sub()
                                                     Try
                                                         If Not child.HasExited Then child.Kill(entireProcessTree:=True)
                                                     Catch ex As InvalidOperationException
                                                     End Try
                                                 End Sub)
                child.WaitForExit()
            End Using
            cancel.ThrowIfCancellationRequested()
            If child.ExitCode <> 0 Then Throw New IOException("Aspen conversion failed: " & errors.GetAwaiter().GetResult())
        End Using
        Dim built = IO.Path.Combine(stage, "conversion/install")
        Dim receipt = AspenPack.Read(built, True, cancel)
        RequireLauncher(receipt)
        If receipt.Version <> offer.Version OrElse Not receipt.Sources.SequenceEqual(offer.Sources) Then Throw New IOException("Converted pack does not match the selected package.")
        cancel.ThrowIfCancellationRequested() : context.RequireClosed()
        progress.Report(New TrackProgress(100, "Installing verified layouts; please wait for the safe commit to finish"))
        Worker.Invoke(context, "install-custom", workId:=id)
        progress.Report(New TrackProgress(100, "Aspen installed. Select a layout, then choose Launch."))
        Finally
            Try
                SafeFiles.DeleteWorkTree(context.UserRoot, "custom-track-builds/" & id)
            Catch ex As IOException
                ' A locked or externally modified staging path is preserved.
            Catch ex As UnauthorizedAccessException
            End Try
        End Try
    End Sub
    Public Function UninstallAsync(context As InstallContext) As Task
        Return Task.Run(Sub() WithSessionLock(Sub() Worker.Invoke(context, "remove-custom")))
    End Function
End Module
