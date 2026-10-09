Imports System.IO.MemoryMappedFiles
Imports System.Windows.Forms

Public Class SessionStatus
    Public Property State As String = ""
    Public Property Message As String = ""
    Public Property ProcessId As Integer
    Public Property StartupFocus As String = ""
    Public Property DisplayWarning As String = ""
    Public Property UpdatedUtc As DateTime = DateTime.UtcNow
    Public Property ErrorDetails As String = ""
    Public Function FailureDescription(gameRunning As Boolean) As String
        ' Older sessions embedded a process-state claim in their saved error message.
        Dim detail = Message.Replace("DiRT 2 is still running. Close it, then use Restore original files.", "").Trim()
        Return "Previous launch failed: " & detail & If(gameRunning, Environment.NewLine & "DiRT 2 is running. Close it before restoring original files.", "")
    End Function
End Class
Public Class HeadsetStatus
    Public Property RefreshHz As String = ""
    Public Property UpdatedUtc As DateTime = DateTime.UtcNow
End Class
Public Class Session
    Private ReadOnly context As InstallContext
    Private ReadOnly settings As VrSettings
    Private ReadOnly driving As DrivingControls
    Private ReadOnly lanJoinTarget As String
    Private focusStatus As String = ""
    Private displayWarning As String = ""
    Private desktopBounds As Drawing.Rectangle?
    Private customTrack As Boolean
    Private ReadOnly postProcessTest As String = "normal"
    Private profile As ProfileSession
    Private filterSnapshot As String
    Public Sub New(value As InstallContext, Optional multiplayer As Boolean = False, Optional joinTarget As String = Nothing)
        context = value : settings = VrSettings.Load(context)
        Dim custom = CustomTrackPreferences.Load(context)
        If custom.Enabled Then
            If multiplayer OrElse joinTarget IsNot Nothing Then Throw New IOException("Turn CUSTOM tracks off before starting LAN multiplayer.")
            customTrack = True
            custom.ApplyTo(settings)
            postProcessTest = If(custom.ForPack(custom.SelectedPackId).PostProcessTest, ButtermilkPostProcess.DefaultProfile(settings.TrackId))
        End If
        driving = DrivingControls.Load(context)
        If joinTarget IsNot Nothing Then lanJoinTarget = LanBrowser.ParseEndpoint(joinTarget).ToString()
        If multiplayer OrElse joinTarget IsNot Nothing Then settings.LaunchMode = "lan"
    End Sub
    Private Sub Status(state As String, Optional message As String = "", Optional failure As Exception = Nothing)
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "session.json"), New SessionStatus With {.State = state, .Message = message & If(displayWarning = "", "", " " & displayWarning), .ProcessId = Environment.ProcessId, .StartupFocus = focusStatus, .DisplayWarning = displayWarning, .ErrorDetails = If(settings.LoggingEnabled AndAlso failure IsNot Nothing, failure.ToString(), "")})
    End Sub
    Public Sub Run(Optional vr As Boolean = True, Optional lanVr As Boolean = False)
        If customTrack Then CustomTracks.TrackPacks.ForLayout(settings.TrackId).RequireMode(settings.TrackId, vr, settings.LaunchMode, settings.CarCode, settings.GridOpponents, settings.SessionLaps)
        ButtermilkPostProcess.Validate(postProcessTest, settings.TrackId)
        Using guard As New Mutex(False, "Global\DiRT2VR.Session")
            Dim held As Boolean
            Try
                held = guard.WaitOne(0)
            Catch ex As AbandonedMutexException
                held = True
            End Try
            If Not held Then Throw New IOException("A DiRT2VR session or recovery is already running.")
            Dim graphics As New GraphicsTransaction(context)
            Try
                profile = New ProfileSession(context)
                filterSnapshot = FilterLaunch.Create(context, settings, vr AndAlso (settings.LaunchMode <> "lan" OrElse lanVr))
                Do
                    Status("Checking")
                    context.ValidateGame() : context.RequireClosed()
                    If settings.DirectMode Then PrototypeTrack.ValidateMode(settings.TrackId, settings.LaunchMode, vr)
                    ' Preserve desktop behavior for older LAN quick-launch commands; VR is an explicit choice.
                    If settings.LaunchMode = "lan" Then vr = vr AndAlso lanVr
                    desktopBounds = Nothing
                    If Not vr Then
                        Status("Restoring", "Checking for an interrupted session")
                        graphics.Recover() : Worker.Invoke(context, "recover")
                        FilterLaunch.ValidateFiles(context, filterSnapshot)
                        If settings.BorderlessDesktop Then
                            If Screen.PrimaryScreen Is Nothing Then Throw New IOException("The primary display is unavailable.")
                            desktopBounds = Screen.PrimaryScreen.Bounds
                            Status("Preparing", "Desktop borderless fullscreen")
                        End If
                        If File.Exists(context.GraphicsPath) OrElse settings.BorderlessDesktop Then
                            graphics.PrepareDesktop(desktopBounds.GetValueOrDefault().Width, desktopBounds.GetValueOrDefault().Height, settings.DesktopVSync)
                        Else
                            ' Let a first Normal Launch create its graphics file; apply the preference on the next session.
                            displayWarning = "Run the game once to create graphics settings; desktop VSync will apply on your next launch."
                        End If
                        Dim returnToMenus = RunDesktop()
                        Status("Restoring") : graphics.Recover() : Worker.Invoke(context, "recover")
                        If returnToMenus Then
                            customTrack = False
                            settings.LaunchMode = "menus"
                            Continue Do
                        End If
                        Status("Ready", "Desktop session ended")
                        Return
                    End If
                    If Not File.Exists(settings.Runtime) OrElse IO.Path.GetFileName(settings.Runtime) <> "steamxr_win32.json" Then Throw New IOException("Select SteamVR's steamxr_win32.json runtime, then start SteamVR and connect your headset.")
                    If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once to create graphics settings.")
                    Status("Restoring", "Checking for an interrupted session")
                    graphics.Recover() : Worker.Invoke(context, "recover")
                    FilterLaunch.ValidateFiles(context, filterSnapshot)
                    Worker.Invoke(context, "setup")
                    Status("Preparing")
                    Dim logFolder = CreateSessionLog(True)
                    ProbeRuntime(logFolder)
                    Dim channel = "Local\DiRT2VR.Input." & Guid.NewGuid().ToString("N")
                    Using mapping = MemoryMappedFile.CreateNew(channel, 16), view = mapping.CreateViewAccessor(), seat As New SeatChannel(context, settings)
                        view.Write(0, &H32565244) : view.Write(4, 1) : view.Write(8, 0UI) : view.Write(12, 0UI)
                        Dim start = VrStartInfo(context, settings, channel, logFolder, lanJoinTarget)
                        start.Environment("DIRT2VR_SEAT_CHANNEL") = seat.Name
                        DirectRaceDifficulty.Configure(start, settings)
                        If settings.DirectMode Then
                            Worker.Invoke(context, "prepare", settings.CarCode, settings.TrackId, settings.GridOpponents, settings.OpponentCars, postProcessTest:=postProcessTest)
                            start.ArgumentList.Add("-demo")
                            start.ArgumentList.Add(New AssetTransaction(context).PracticeConfig())
                            start.Environment("DIRT2VR_DIRECT_PRACTICE") = "1"
                            start.Environment("DIRT2VR_LAPS") = settings.SessionLaps.ToString(Globalization.CultureInfo.InvariantCulture)
                        Else
                            Worker.Invoke(context, "prepare")
                        End If
                        graphics.Prepare(settings)
                        profile.Configure(start, settings.LaunchMode = "lan")
                        profile.Prepare(settings.LaunchMode = "lan")
                        ' LAN validates states.bin against the game's original checksum.
                        PrepareMenus()
                        Dim returnToMenus As Boolean
                        Using input As New ControllerInput(context), machine As New BindingMachine(settings.Bindings)
                            Dim counts As UInteger() = {0UI, 0UI}
                            AddHandler input.StateChanged, Sub(sample)
                                For Each action In machine.Update(sample)
                                    If action > 1 OrElse Not ControllerInput.GameFocused() OrElse seat.ConsumesShortcut(sample, action) Then Continue For
                                    counts(action) = CUInt((CLng(counts(action)) + 1) And &HFFFFFFFFL)
                                    view.Write(8 + action * 4, counts(action))
                                Next
                            End Sub
                            returnToMenus = WaitForGame(start, Sub()
                                input.Poll()
                                seat.Poll(input.Snapshot(), ControllerInput.GameFocused())
                            End Sub)
                            profile.ConfirmStartup(settings.LaunchMode = "lan")
                        End Using
                        Status("Restoring")
                        graphics.Recover() : Worker.Invoke(context, "recover")
                        If returnToMenus Then
                            customTrack = False
                            settings.LaunchMode = "menus"
                            Continue Do
                        End If
                    End Using
                    Status("Ready", "Original files restored")
                    Exit Do
                Loop
            Catch ex As Exception
                Dim message = ex.Message
                If Not context.GameRunning() Then
                    Try
                        graphics.Recover()
                    Catch recovery As Exception
                        message &= Environment.NewLine & recovery.Message
                    End Try
                    Try
                        Worker.Invoke(context, "recover")
                    Catch recovery As Exception
                        message &= Environment.NewLine & recovery.Message
                    End Try
                Else
                    message &= Environment.NewLine & "DiRT 2 is still running. Close it, then use Restore original files."
                End If
                Status("Failed", message, ex)
                Throw New IOException(message, ex)
            Finally
                If filterSnapshot IsNot Nothing Then
                    Try
                        File.Delete(FilterLaunch.SnapshotPath(context, filterSnapshot))
                    Catch ex As IOException
                        ' An interrupted snapshot is harmless; never obscure recovery errors.
                    Catch ex As UnauthorizedAccessException
                    End Try
                End If
                guard.ReleaseMutex()
            End Try
        End Using
    End Sub
    Public Shared Function VrStartInfo(context As InstallContext, settings As VrSettings, channel As String, logFolder As String, Optional joinTarget As String = Nothing) As ProcessStartInfo
        ' Both factories clear inherited experiments before setting their explicit launch flags.
        Dim start = If(settings.LaunchMode = "lan", LanSession.StartInfo(context, settings.SkipIntroduction, joinTarget), DesktopStartInfo(context, Nothing, Nothing))
        For Each name In {"ACTIVE", "REPLAY_PROBE", "INNER_REPLAY", "CONTINUOUS_REPLAY", "HEADSET", "INTERACTIVE", "WIDE_VISIBILITY", "AUTO_COCKPIT"}
            start.Environment("DIRT2VR_" & name) = "1"
        Next
        start.Environment("DIRT2VR_CAPTURE_DIAGNOSTICS") = "0"
        start.Environment("DIRT2VR_CAPTURE_REQUESTS") = If(Environment.GetCommandLineArgs().Contains("--diagnostic-capture"), "1", "0")
        start.Environment("DIRT2VR_WATER_REFLECTIONS") = "1"
        start.Environment("DIRT2VR_SHADOWS") = If(settings.VrShadows, "1", "0")
        SteeringAnimationLaunch.Configure(start, settings)
        ChaseCameraLaunch.Configure(start, settings)
        start.Environment("DIRT2VR_EXTENDED_VIEWS") = If(settings.VrExtendedViews, "1", "0")
        ReplayCameraLaunch.Configure(start, settings)
        start.Environment("DIRT2VR_TRACE_LIGHTS") = "0"
        start.Environment("DIRT2VR_WORLD_SCALE") = "1"
        start.Environment("DIRT2VR_HEADSET_SCALE") = (settings.HeadsetScale / 100.0).ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_SCENE_SIZE") = $"{settings.RenderWidth}x{settings.RenderHeight}"
        start.Environment("DIRT2VR_FOV_SCALE") = (settings.FieldOfView / 100.0).ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_HUD_FOLLOW") = If(settings.HudFollowView, "1", "0")
        start.Environment("DIRT2VR_HUD_DISTANCE") = settings.HudDistance.ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_HUD_HIDE") = settings.HiddenHudElements.ToString(Globalization.CultureInfo.InvariantCulture)
        ConfigureLogging(start, logFolder)
        start.Environment("DIRT2VR_INPUT_CHANNEL") = channel
        start.Environment("DIRT2VR_KEYS") = $"{settings.ToggleKey}:{settings.ToggleModifiers},{settings.RecenterKey}:{settings.RecenterModifiers}"
        start.Environment("XR_RUNTIME_JSON") = settings.Runtime
        Return start
    End Function
    Private Function RunDesktop() As Boolean
        FlashbackLaunch.RequireDesktopRenderer(context, settings)
        SteeringAnimationLaunch.RequireDesktopRenderer(context, settings)
        ChaseCameraLaunch.RequireDesktopRenderer(context, settings)
        If (driving.Enabled AndAlso driving.Bindings.Count > 0) OrElse FlashbackLaunch.Enabled(settings) OrElse settings.VrSteeringAnimation OrElse settings.ChaseFreeLook Then Worker.Invoke(context, "setup")
        If settings.LaunchMode = "lan" Then
            Status("Preparing", "LAN multiplayer — use the game's Multiplayer / LAN menus")
            Dim lanStart = LanSession.StartInfo(context, settings.SkipIntroduction, lanJoinTarget)
            ConfigureLogging(lanStart, CreateSessionLog(False))
            profile.Configure(lanStart, True)
            profile.Prepare(True)
            WaitForGame(lanStart)
            profile.ConfirmStartup(True)
            Return False
        End If
        Dim config As String = Nothing
        Dim logFolder As String = CreateSessionLog(False)
        If settings.DirectMode Then
            ' Human control is enabled by the DX11 proxy; desktop rendering settings
            ' stay untouched rather than silently running an AI-driven DX9 session.
            If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before using Direct practice or Race.")
            Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
            Dim dx = TryCast(document.SelectSingleNode("/hardware_settings_config/graphics_card/directx"), Xml.XmlElement)
            If dx Is Nothing OrElse Not String.Equals(dx.GetAttribute("forcedx9"), "false", StringComparison.OrdinalIgnoreCase) Then Throw New IOException("Desktop Direct practice and Race require the game's DX11 renderer (forcedx9=false). Use Game menus for normal DX9 play.")
            Worker.Invoke(context, "setup")
            Status("Preparing", If(settings.LaunchMode = "race", "Desktop race", "Desktop practice"))
            Worker.Invoke(context, "prepare-desktop", settings.CarCode, settings.TrackId, settings.GridOpponents, settings.OpponentCars, postProcessTest:=postProcessTest)
            config = New AssetTransaction(context).PracticeConfig()
        End If
        Dim start = DesktopStartInfo(context, config, logFolder)
        If FlashbackLaunch.Enabled(settings) OrElse settings.VrSteeringAnimation Then ConfigureLogging(start, logFolder)
        profile.Configure(start, False)
        profile.Prepare(False)
        PrepareMenus()
        If config IsNot Nothing Then start.Environment("DIRT2VR_LAPS") = settings.SessionLaps.ToString(Globalization.CultureInfo.InvariantCulture)
        DirectRaceDifficulty.Configure(start, settings)
        Dim returnToMenus = WaitForGame(start)
        profile.ConfirmStartup(False)
        Return returnToMenus
    End Function
    Private Sub PrepareMenus()
        If settings.DirectMode Then
            Worker.Invoke(context, If(settings.SkipStartupMovies, "prepare-direct-menus-movies", "prepare-direct-menus"))
        ElseIf settings.SkipStartupMovies AndAlso settings.LaunchMode <> "lan" Then
            Worker.Invoke(context, "prepare-movies")
        End If
    End Sub
    Public Shared Function DesktopStartInfo(context As InstallContext, config As String, logFolder As String) As ProcessStartInfo
        Dim start As New ProcessStartInfo(IO.Path.Combine(context.GameRoot, "dirt2.exe")) With {.UseShellExecute = False, .WorkingDirectory = context.GameRoot}
        For Each name In start.Environment.Keys.Where(Function(k) k.StartsWith("DIRT2VR_", StringComparison.OrdinalIgnoreCase)).ToArray()
            start.Environment.Remove(name)
        Next
        start.Environment("DIRT2VR_ACTIVE") = "0"
        If config IsNot Nothing Then
            start.ArgumentList.Add("-demo") : start.ArgumentList.Add(config)
            start.Environment("DIRT2VR_ACTIVE") = "1"
            start.Environment("DIRT2VR_DESKTOP_PRACTICE") = "1"
            start.Environment("DIRT2VR_DIRECT_PRACTICE") = "1"
            start.Environment("DIRT2VR_CAPTURE_DIAGNOSTICS") = "0"
            ConfigureLogging(start, logFolder)
        End If
        Return start
    End Function
    Private Function WaitForGame(start As ProcessStartInfo, Optional poll As Action = Nothing) As Boolean
        ' Last effect layer for every launch path, including LAN and return to menus.
        If filterSnapshot IsNot Nothing Then Worker.Invoke(context, "prepare-filter", filterSnapshot:=filterSnapshot)
        If Not BloomTransaction.Enabled(settings, start) Then Worker.Invoke(context, "prepare-bloom")
        NordschleifeProgress.Configure(start, settings)
        driving.ConfigureProcess(context, start, start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1")
        FlashbackLaunch.Configure(start, settings)
        SteeringAnimationLaunch.Configure(start, settings)
        ChaseCameraLaunch.Configure(start, settings)
        Dim focus As New StartupFocus(context)
        ReplayCameraLaunch.Configure(start, settings)
        Dim borderless = If(desktopBounds.HasValue, New BorderlessWindow(context, desktopBounds.GetValueOrDefault()), Nothing)
        Using returnChannel As New DirectReturnChannel(start, settings.DirectMode), resolution As New ResolutionChannel(context, start, settings),
            lap As New BestLapChannel(context, start, If(customTrack AndAlso settings.DirectMode AndAlso settings.LaunchMode = "practice", settings.TrackId, ""), settings.CarCode)
            Using child = Process.Start(start)
                Status("Running")
                Dim seenGame As Boolean
                Dim gameAlive As Boolean = True
                Dim nextProcessCheck = DateTime.MinValue
                Dim deadline = DateTime.UtcNow.AddSeconds(30)
                Do
                    Application.DoEvents() : poll?.Invoke()
                    If DateTime.UtcNow >= nextProcessCheck Then
                        lap.Poll()
                        resolution.Poll()
                        borderless?.Poll()
                        If borderless IsNot Nothing AndAlso borderless.Warning <> "" AndAlso displayWarning <> borderless.Warning Then
                            displayWarning = borderless.Warning : Status("Running")
                            Console.Error.WriteLine(displayWarning)
                        End If
                        focus.Poll()
                        If focusStatus <> focus.Outcome Then
                            focusStatus = focus.Outcome : Status("Running")
                        End If
                        gameAlive = context.GameRunning()
                        seenGame = seenGame Or gameAlive
                        nextProcessCheck = DateTime.UtcNow.AddMilliseconds(250)
                    End If
                    ' GameRunning includes both the bootstrapper and actual game. Avoid
                    ' querying a protected/obsolete child handle while monitoring them.
                    If Not gameAlive AndAlso (seenGame OrElse DateTime.UtcNow > deadline) Then Exit Do
                    Thread.Sleep(8)
                Loop
                If Not seenGame Then Throw New IOException("The game did not start. Check that your normal DiRT 2 installation works.")
            End Using
            If returnChannel.ProfileLoadFailed Then Throw New IOException("The career could not be loaded for this direct event, or sign-in was canceled. Use Normal Launch to sign in to your usual GFWL profile and confirm it loads. No replacement career was created.")
            Return returnChannel.Requested
        End Using
    End Function
    Private Function CreateSessionLog(vr As Boolean) As String
        Dim folder = CreateLogFolder(context, settings.LoggingEnabled OrElse (vr AndAlso Environment.GetCommandLineArgs().Contains("--diagnostic-capture")))
        SessionDiagnostics.Save(context, folder, settings, driving, vr)
        Return folder
    End Function
    Public Shared Function CreateLogFolder(context As InstallContext, enabled As Boolean) As String
        If Not enabled Then Return Nothing
        Dim folder = IO.Path.Combine(context.UserRoot, "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))
        Directory.CreateDirectory(folder)
        Return folder
    End Function
    Public Shared Sub ConfigureLogging(start As ProcessStartInfo, logFolder As String)
        start.Environment("DIRT2VR_LOGGING") = If(logFolder Is Nothing, "0", "1")
        start.Environment.Remove("DIRT2VR_OUTPUT")
        If logFolder IsNot Nothing Then start.Environment("DIRT2VR_OUTPUT") = logFolder
    End Sub
    Public Shared Sub SavePreflightReport(context As InstallContext, report As String, logFolder As String)
        If logFolder IsNot Nothing Then File.WriteAllText(IO.Path.Combine(logFolder, "preflight.txt"), report)
        Dim match = System.Text.RegularExpressions.Regex.Match(report, "(?m)^display_refresh_hz=([0-9.]+)")
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "headset.json"), New HeadsetStatus With {.RefreshHz = If(match.Success, match.Groups(1).Value, "")})
    End Sub
    Private Sub ProbeRuntime(logFolder As String)
        Dim executable = IO.Path.Combine(context.ModRoot, "payload\xr_probe.exe")
        Dim manifest = Files.ReadJson(Of PackageManifest)(IO.Path.Combine(context.ModRoot, "package.json"))
        Const relative As String = "DiRT2VR/payload/xr_probe.exe"
        If Not File.Exists(executable) OrElse Not manifest.Files.ContainsKey(relative) OrElse Files.Hash(executable) <> manifest.Files(relative) Then Throw New IOException("Headset preflight tool is missing or damaged. Extract the complete package again.")
        Dim start As New ProcessStartInfo(executable) With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
        start.Environment("XR_RUNTIME_JSON") = settings.Runtime
        start.Environment("DIRT2VR_ACTIVE") = "0"
        Using probe = Process.Start(start)
            Dim stdout = probe.StandardOutput.ReadToEndAsync()
            Dim stderr = probe.StandardError.ReadToEndAsync()
            If Not probe.WaitForExit(30000) Then
                probe.Kill() : probe.WaitForExit()
                Throw New IOException("SteamVR preflight timed out. Start SteamVR, connect your headset, then retry.")
            End If
            Dim report = stdout.GetAwaiter().GetResult() & stderr.GetAwaiter().GetResult()
            SavePreflightReport(context, report, logFolder)
            If probe.ExitCode <> 0 Then Throw New IOException("SteamVR could not open a headset session. Connect your headset and check SteamVR. " & If(logFolder Is Nothing, "Enable diagnostic logging in Settings and retry for details.", "Details are in " & IO.Path.Combine(logFolder, "preflight.txt")))
        End Using
    End Sub
End Class
