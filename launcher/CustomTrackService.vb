Imports DiRT2VR.CustomTracks
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

Public Class CustomTrackSettings
    Public Property LayoutId As String = "aspen-lakeside"
    Public Property SourceFolder As String = ""
    Public Property BuildLayoutIds As String()
    Public Property LaunchMode As String = "practice"
    Public Property CarCode As String = "sti"
    Public Property Opponents As Integer = 3
    Public Property OpponentCars As String = "same"
    Public Property Laps As Integer = 1
    Public Property RaceDifficulty As Integer = -1
    Public Property PostProcessTest As String
    Public Sub ApplyTo(settings As VrSettings)
        Dim pack = TrackPacks.ForLayout(LayoutId)
        Dim gridOpponents = Math.Min(Opponents, pack.MaximumOpponents(LayoutId))
        pack.RequireMode(LayoutId, False, LaunchMode, CarCode, If(LaunchMode = "race", gridOpponents, 0), Laps)
        RaceCatalog.Current.Car(CarCode)
        If Opponents < 1 OrElse Opponents > 7 OrElse Not {"same", "mixed", "class"}.Contains(OpponentCars) Then Throw New IOException("Choose valid custom-track race opponents.")
        DirectRaceDifficulty.Validate(RaceDifficulty)
        settings.RaceDifficulty = RaceDifficulty
        settings.TrackId = LayoutId : settings.LaunchMode = LaunchMode : settings.CarCode = CarCode
        settings.Opponents = gridOpponents : settings.OpponentCars = OpponentCars : settings.Laps = Laps
    End Sub
End Class

Public Class CustomTrackPreferences
    Public Property Schema As Integer = 2
    Public Property Enabled As Boolean
    Public Property SelectedPackId As String = AspenPack.Id
    Public Property Packs As New Dictionary(Of String, CustomTrackSettings)(StringComparer.Ordinal)
    Public Function ForPack(id As String) As CustomTrackSettings
        If Not Packs.ContainsKey(id) Then
            Dim pack = CustomTrackCatalog.Find(id)
            Packs(id) = New CustomTrackSettings With {.LayoutId = If(pack?.Layouts.FirstOrDefault()?.Id, "")}
        End If
        Return Packs(id)
    End Function
    Public Sub ApplyTo(settings As VrSettings)
        Dim pack = CustomTrackCatalog.RequireAvailable(SelectedPackId)
        Dim selected = ForPack(pack.Id)
        If Not pack.Layouts.Any(Function(l) l.Id = selected.LayoutId) Then Throw New IOException("Choose an available layout for " & pack.Name & ".")
        selected.ApplyTo(settings)
    End Sub
    Public Shared Function Load(context As InstallContext) As CustomTrackPreferences
        Dim path = IO.Path.Combine(context.UserRoot, "custom-tracks.json")
        If Not File.Exists(path) Then Return New CustomTrackPreferences()
        Using document = JsonDocument.Parse(File.ReadAllBytes(path))
            Dim value As JsonElement
            If Not document.RootElement.TryGetProperty("Schema", value) Then
                ' The previous flat file contains Aspen settings only. Preserve
                ' every choice, including an unavailable saved car/layout.
                Dim migrated As New CustomTrackPreferences()
                If document.RootElement.TryGetProperty("Enabled", value) Then migrated.Enabled = value.GetBoolean()
                migrated.Packs(AspenPack.Id) = Files.ReadJson(Of CustomTrackSettings)(path)
                Return migrated
            End If
        End Using
        Dim result = Files.ReadJson(Of CustomTrackPreferences)(path)
        If result Is Nothing OrElse result.Schema <> 2 OrElse String.IsNullOrWhiteSpace(result.SelectedPackId) OrElse result.Packs Is Nothing OrElse result.Packs.Any(Function(p) String.IsNullOrWhiteSpace(p.Key) OrElse p.Value Is Nothing) Then Throw New IOException("Unsupported custom-track preferences. Restore your custom-tracks.json backup or choose fresh settings.")
        Return result
    End Function
    Public Sub Save(context As InstallContext)
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "custom-tracks.json"), Me)
    End Sub
End Class
Public Module CustomTrackService
    Public Function RecoveryPending(context As InstallContext) As Boolean
        Return New ButtermilkPostProcess(context).Pending OrElse File.Exists(IO.Path.Combine(context.ModRoot, "custom-track-session/pending.json")) OrElse
            File.Exists(IO.Path.Combine(context.ModRoot, "custom-track-install/pending.json"))
    End Function
    Public Sub RequireLauncher(receipt As PackReceipt)
        If System.Version.Parse(receipt.MinimumLauncher) > System.Version.Parse(BuildInfo.Version) Then Throw New IOException("This track pack needs DiRT2VR " & receipt.MinimumLauncher & " or later. Update the launcher before playing.")
    End Sub
    Public Function Profile(packId As String) As ConversionProfile
        Select Case packId
            Case AspenPack.Id : Return Aspen.AspenConversion.GetProfile()
            Case "smelter" : Return Aspen.SmelterConversion.GetProfile()
            Case "nordschleife" : Return Nordschleife.NordschleifeConversion.GetProfile()
            Case Else : Throw New IOException("Unknown custom-track pack.")
        End Select
    End Function
    Public Function SourceTrackFolder(packId As String) As String
        TrackPacks.Get(packId)
        Return If(packId = "nordschleife", "content/tracks/ks_nordschleife", "tracks/locations/usa/" & If(packId = "smelter", "smelter", "aspen"))
    End Function
    Public Function SourceFolders(Optional packId As String = AspenPack.Id) As String()
        Dim result As New List(Of String)
        Dim libraries As List(Of String)
        Try
            libraries = Discovery.SteamLibraries()
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
            Return Array.Empty(Of String)() ' Manual selection remains available.
        End Try
        For Each library In libraries
            Try
                Dim manifest = IO.Path.Combine(library, "steamapps", If(packId = "nordschleife", "appmanifest_347990.acf", "appmanifest_321040.acf"))
                If Not File.Exists(manifest) Then Continue For
                Dim match = Regex.Match(File.ReadAllText(manifest), """installdir""\s+""([^""]+)""")
                If Not match.Success Then Continue For
                SafeFiles.Relative(match.Groups(1).Value)
                Dim folder = SafeFiles.Inside(IO.Path.Combine(library, "steamapps/common"), match.Groups(1).Value)
                If Directory.Exists(IO.Path.Combine(folder, SourceTrackFolder(packId))) Then result.Add(folder)
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
    Public Function InstallAsync(context As InstallContext, offer As ConversionProfile, source As String, progress As IProgress(Of TrackProgress), cancel As CancellationToken, Optional layoutIds As String() = Nothing) As Task
        Return Task.Run(Sub()
                            WithSessionLock(Sub() Install(context, offer, source, progress, cancel, layoutIds))
                        End Sub, cancel)
    End Function
    Private Sub Install(context As InstallContext, offer As ConversionProfile, source As String, progress As IProgress(Of TrackProgress), cancel As CancellationToken, layoutIds As String())
        context.ValidateGame() : context.RequireClosed()
        Worker.Invoke(context, "recover")
        Dim pack = TrackPacks.Get(offer.Id)
        pack.Validate(offer, BuildInfo.Version)
        Dim selectedLayouts = pack.SelectLayouts(layoutIds)
        If pack.Id = "nordschleife" AndAlso selectedLayouts.Length <> pack.Layouts.Length Then Throw New IOException("Nordschleife builds all three lighting presets together.")
        Dim id = Guid.NewGuid().ToString("N"), stage = Staging(context, id)
        Directory.CreateDirectory(stage)
        Try
        If New DriveInfo(IO.Path.GetPathRoot(stage)).AvailableFreeSpace < offer.StagingBytes Then Throw New IOException("Not enough free space for conversion. Free at least " & Math.Ceiling(offer.StagingBytes / 1073741824.0).ToString() & " GB on " & IO.Path.GetPathRoot(stage))
        If New DriveInfo(IO.Path.GetPathRoot(context.GameRoot)).AvailableFreeSpace < offer.InstalledBytes * 2 Then Throw New IOException("Not enough free space beside DiRT 2 for the track and its rollback copy.")
        TrackPack.VerifySources(offer, context.GameRoot, source, progress, cancel)
        cancel.ThrowIfCancellationRequested()
        context.RequireClosed()
        Dim start As New ProcessStartInfo(Environment.ProcessPath) With {
            .UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = stage, .RedirectStandardOutput = True, .RedirectStandardError = True}
        Dim command = If(pack.Id = "nordschleife", "--convert-nordschleife", If(pack.Id = AspenPack.Id, "--convert-aspen", "--convert-smelter"))
        For Each argument In {command, IO.Path.GetFullPath(source), context.GameRoot, IO.Path.Combine(stage, "conversion")}
            start.ArgumentList.Add(argument)
        Next
        If pack.Id <> "nordschleife" Then
            start.ArgumentList.Add("--layouts")
            start.ArgumentList.Add(String.Join(",", selectedLayouts.Select(Function(l) l.Id)))
        End If
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
                                                     Catch ex As ComponentModel.Win32Exception
                                                         ' Keep waiting for the owned converter; never interrupt the install worker.
                                                     End Try
                                                 End Sub)
                child.WaitForExit()
            End Using
            cancel.ThrowIfCancellationRequested()
            If child.ExitCode <> 0 Then Throw New IOException(pack.Name & " conversion failed: " & errors.GetAwaiter().GetResult())
        End Using
        Dim built = IO.Path.Combine(stage, "conversion/install")
        Dim receipt = pack.Read(built, True, cancel)
        RequireLauncher(receipt)
        If receipt.Version <> offer.Version OrElse Not receipt.Sources.SequenceEqual(offer.Sources) OrElse Not receipt.Sessions.Select(Function(s) s.LayoutId).SequenceEqual(selectedLayouts.Select(Function(l) l.Id)) Then Throw New IOException("Converted pack does not match the selected package.")
        cancel.ThrowIfCancellationRequested() : context.RequireClosed()
        progress.Report(New TrackProgress(100, "Finishing installation — please wait. It is no longer safe to stop.", False))
        Worker.Invoke(context, "install-custom", workId:=id)
        progress.Report(New TrackProgress(100, selectedLayouts.Length.ToString() & " " & pack.Name & " layouts built. Other installed layouts were kept.", False))
        Finally
            Try
                SafeFiles.DeleteWorkTree(context.UserRoot, "custom-track-builds/" & id)
            Catch ex As IOException
                ' A locked or externally modified staging path is preserved.
            Catch ex As UnauthorizedAccessException
            End Try
        End Try
    End Sub
    Public Function UninstallAsync(context As InstallContext, Optional packId As String = AspenPack.Id) As Task
        Return Task.Run(Sub() WithSessionLock(Sub() Worker.Invoke(context, "remove-custom", trackId:=packId)))
    End Function
End Module
