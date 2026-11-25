Imports DiRT2VR.CustomCars
Imports DiRT2VR.CustomTracks
Imports System.Text.Json

Public Class CustomCarPreferences
    Public Property Schema As Integer = 1
    Public Property SelectedId As String = CarPack.Code
    Public Property Enabled As Boolean
    Public Property SourceFolder As String = ""
    Public Property StockCars As New Dictionary(Of String, String)(StringComparer.Ordinal)
    ' Stock or external car code -> chosen livery code. No entry keeps the car's first livery.
    Public Property Liveries As New Dictionary(Of String, String)(StringComparer.Ordinal)
    Public Shared Function Load(context As InstallContext) As CustomCarPreferences
        Dim path = IO.Path.Combine(context.UserRoot, "custom-cars.json")
        If Not File.Exists(path) Then Return New CustomCarPreferences()
        Dim value = Files.ReadJson(Of CustomCarPreferences)(path)
        If value Is Nothing OrElse String.IsNullOrEmpty(value.SelectedId) OrElse value.SelectedId.Length > 100 OrElse value.Schema <> 1 OrElse value.StockCars Is Nothing OrElse value.StockCars.Count > 32 OrElse
            value.StockCars.Any(Function(p) String.IsNullOrWhiteSpace(p.Key) OrElse Not RaceCatalog.Current.Cars.Any(Function(c) c.Code = p.Value)) OrElse
            value.Liveries Is Nothing OrElse value.Liveries.Count > 512 OrElse value.Liveries.Any(Function(p) Not CarLiveries.Saveable(p.Key, p.Value)) Then Throw New IOException("Invalid custom-car preferences. The saved file was kept.")
        Return value
    End Function
    Public Sub Save(context As InstallContext)
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "custom-cars.json"), Me)
    End Sub
End Class

Public Module CustomCarService
    Public Function SelectedCode(context As InstallContext) As String
        Dim id = CustomCarPreferences.Load(context).SelectedId
        If id = CarPack.Code Then Return id
        Dim pack = TrackPacks.External.SingleOrDefault(Function(p) p.Kind = "car" AndAlso p.Id = id)
        If pack Is Nothing Then Throw New IOException("The selected external car is missing. Restore its pack or select another car.")
        If Not pack.Ready Then Throw New IOException(pack.Problem)
        Return pack.Manifest.Car.Code
    End Function
    Public Sub ConfigureProcess(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_CUSTOM_CAR")
        start.Environment.Remove("DIRT2VR_PACK_ENGINE")
        start.Environment.Remove("DIRT2VR_PACK_ENGINE_SAMPLES")
        If Not settings.DirectMode Then Return
        ' An external pack's own engine sound follows the revs only through the game's
        ' legacy engine controller, which the DLL selects for this car (src/f04_audio.cpp).
        Dim audio = CustomCars.ExternalCars.Find(settings.CarCode)?.Manifest.Car.Audio
        If audio IsNot Nothing Then
            start.Environment("DIRT2VR_PACK_ENGINE") = settings.CarCode
            start.Environment("DIRT2VR_PACK_ENGINE_SAMPLES") = audio.Samples.ToString(Globalization.CultureInfo.InvariantCulture)
        End If
        If Not CarPack.IsCustom(settings.CarCode) Then Return
        start.Environment("DIRT2VR_CUSTOM_CAR") = CarPack.Code
        If start.Environment.ContainsKey("DIRT2VR_DESKTOP_PRACTICE") AndAlso start.Environment("DIRT2VR_DESKTOP_PRACTICE") = "1" Then
            start.Environment("DIRT2VR_AUTO_COCKPIT") = "1"
        End If
    End Sub
    Public Event Changed As EventHandler
    Public Property Building As Boolean
    Public Sub Notify(context As InstallContext)
        RaiseEvent Changed(context, EventArgs.Empty)
    End Sub
    Public Function Staging(context As InstallContext, id As String) As String
        Dim parsed As Guid
        If Not Guid.TryParseExact(id, "N", parsed) Then Throw New IOException("Invalid custom-car build identifier.")
        Return SafeFiles.Inside(context.UserRoot, "custom-car-builds/" & id)
    End Function
    Public Function SourceFolder(context As InstallContext) As String
        Dim saved = CustomCarPreferences.Load(context).SourceFolder
        If saved <> "" Then Return saved
        Try
            For Each library In Discovery.SteamLibraries()
                Dim manifest = IO.Path.Combine(library, "steamapps/appmanifest_244210.acf")
                If Not File.Exists(manifest) Then Continue For
                Dim match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest), """installdir""\s+""([^""]+)""")
                If Not match.Success Then Continue For
                SafeFiles.Relative(match.Groups(1).Value)
                Dim candidate = SafeFiles.Inside(IO.Path.Combine(library, "steamapps/common"), match.Groups(1).Value & "/content/cars/ks_ferrari_f2004")
                If Directory.Exists(candidate) Then Return candidate
            Next
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            ' Manual source selection remains available.
        End Try
        Return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam/steamapps/common/assettocorsa/content/cars/ks_ferrari_f2004")
    End Function
    Public Function InstallAsync(context As InstallContext, source As String, progress As IProgress(Of CarProgress), cancel As CancellationToken) As Task
        Return Task.Run(Sub()
                            BuildDiagnostics.Run(context, "car", CarPack.Code, source,
                                                 Sub() CustomTrackService.WithSessionLock(Sub() Install(context, source, progress, cancel)))
                        End Sub, cancel)
    End Function
    Private Sub Install(context As InstallContext, source As String, progress As IProgress(Of CarProgress), cancel As CancellationToken)
        context.ValidateGame() : context.RequireClosed() : Worker.Invoke(context, "recover")
        Dim id = Guid.NewGuid().ToString("N"), stage = Staging(context, id)
        Directory.CreateDirectory(stage)
        Try
            If New DriveInfo(IO.Path.GetPathRoot(stage)).AvailableFreeSpace < 1073741824L Then Throw New IOException("Free at least 1 GB for the Ferrari build.")
            If New DriveInfo(IO.Path.GetPathRoot(context.GameRoot)).AvailableFreeSpace < 268435456L Then Throw New IOException("Free at least 256 MB beside DiRT 2 for installation and recovery.")
            Dim start As New ProcessStartInfo(Environment.ProcessPath) With {.UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = stage, .RedirectStandardOutput = True, .RedirectStandardError = True}
            For Each argument In {"--convert-f2004", IO.Path.GetFullPath(source), context.GameRoot, IO.Path.Combine(stage, "conversion")}
                start.ArgumentList.Add(argument)
            Next
            Using child = Process.Start(start)
                Dim errors = child.StandardError.ReadToEndAsync()
                AddHandler child.OutputDataReceived, Sub(sender, e)
                                                         If String.IsNullOrEmpty(e.Data) OrElse Not e.Data.StartsWith("{"c) Then Return
                                                         Try
                                                             Dim value = JsonSerializer.Deserialize(Of CarProgress)(e.Data)
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
                                                         End Try
                                                     End Sub)
                    child.WaitForExit()
                End Using
                cancel.ThrowIfCancellationRequested()
                If child.ExitCode <> 0 Then Throw New IOException("Ferrari conversion failed (exit code " & child.ExitCode.ToString() & "): " & errors.GetAwaiter().GetResult())
            End Using
            CarPack.Read(IO.Path.Combine(stage, "conversion/install"))
            cancel.ThrowIfCancellationRequested() : context.RequireClosed()
            progress.Report(New CarProgress(100, "Finishing installation — please wait.", False))
            Worker.Invoke(context, "install-car", workId:=id)
        Finally
            Try
                SafeFiles.DeleteWorkTree(context.UserRoot, "custom-car-builds/" & id)
            Catch ex As IOException
            Catch ex As UnauthorizedAccessException
            End Try
        End Try
    End Sub
    Public Function UninstallAsync(context As InstallContext) As Task
        Return Task.Run(Sub() CustomTrackService.WithSessionLock(Sub() Worker.Invoke(context, "remove-car")))
    End Function
End Module
