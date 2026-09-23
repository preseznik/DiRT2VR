Imports System.IO.MemoryMappedFiles
Imports System.Windows.Forms

Public Class SessionStatus
    Public Property State As String = ""
    Public Property Message As String = ""
    Public Property ProcessId As Integer
    Public Property StartupFocus As String = ""
    Public Property UpdatedUtc As DateTime = DateTime.UtcNow
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
    Public Sub New(value As InstallContext, Optional multiplayer As Boolean = False, Optional joinTarget As String = Nothing)
        context = value : settings = VrSettings.Load(context)
        driving = DrivingControls.Load(context)
        If joinTarget IsNot Nothing Then lanJoinTarget = LanBrowser.ParseEndpoint(joinTarget).ToString()
        If multiplayer OrElse joinTarget IsNot Nothing Then settings.LaunchMode = "lan"
    End Sub
    Private Sub Status(state As String, Optional message As String = "")
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "session.json"), New SessionStatus With {.State = state, .Message = message, .ProcessId = Environment.ProcessId, .StartupFocus = focusStatus})
    End Sub
    Public Sub Run(Optional vr As Boolean = True, Optional lanVr As Boolean = False)
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
                Do
                    Status("Checking")
                    context.ValidateGame() : context.RequireClosed()
                    ' Preserve desktop behavior for older LAN quick-launch commands; VR is an explicit choice.
                    If settings.LaunchMode = "lan" Then vr = vr AndAlso lanVr
                    If Not vr Then
                        Status("Restoring", "Checking for an interrupted session")
                        graphics.Recover() : Worker.Invoke(context, "recover")
                        Dim returnToMenus = RunDesktop()
                        Status("Restoring") : Worker.Invoke(context, "recover")
                        If returnToMenus Then
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
                    Worker.Invoke(context, "setup")
                    Status("Preparing")
                    Dim logFolder = CreateLogFolder(context, settings.LoggingEnabled OrElse Environment.GetCommandLineArgs().Contains("--diagnostic-capture"))
                    ProbeRuntime(logFolder)
                    Dim channel = "Local\DiRT2VR.Input." & Guid.NewGuid().ToString("N")
                    Using mapping = MemoryMappedFile.CreateNew(channel, 16), view = mapping.CreateViewAccessor()
                        view.Write(0, &H32565244) : view.Write(4, 1) : view.Write(8, 0UI) : view.Write(12, 0UI)
                        Dim start = VrStartInfo(context, settings, channel, logFolder, lanJoinTarget)
                        If settings.DirectMode Then
                            Worker.Invoke(context, "prepare", settings.CarCode, settings.TrackId, settings.GridOpponents, settings.OpponentCars)
                            start.ArgumentList.Add("-demo")
                            start.ArgumentList.Add(New AssetTransaction(context).PracticeConfig())
                            start.Environment("DIRT2VR_DIRECT_PRACTICE") = "1"
                            start.Environment("DIRT2VR_LAPS") = settings.SessionLaps.ToString(Globalization.CultureInfo.InvariantCulture)
                        Else
                            Worker.Invoke(context, "prepare")
                        End If
                        graphics.Prepare(settings)
                        If settings.LaunchMode = "lan" Then Worker.Invoke(context, "prepare-lan")
                        ' LAN validates states.bin against the game's original checksum.
                        PrepareMenus()
                        Dim returnToMenus As Boolean
                        Using input As New ControllerInput(), machine As New BindingMachine(settings.Bindings)
                            Dim counts As UInteger() = {0UI, 0UI}
                            AddHandler input.StateChanged, Sub(sample)
                                For Each action In machine.Update(sample)
                                    If Not ControllerInput.GameFocused() Then Continue For
                                    counts(action) = CUInt((CLng(counts(action)) + 1) And &HFFFFFFFFL)
                                    view.Write(8 + action * 4, counts(action))
                                Next
                            End Sub
                            returnToMenus = WaitForGame(start, AddressOf input.Poll)
                            If settings.LaunchMode = "lan" AndAlso Not File.Exists(LanSession.ReceiptPath(context)) Then Throw New IOException("The game exited before LAN startup was confirmed.")
                        End Using
                        Status("Restoring")
                        graphics.Recover() : Worker.Invoke(context, "recover")
                        If returnToMenus Then
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
                Status("Failed", message)
                Throw New IOException(message, ex)
            Finally
                guard.ReleaseMutex()
            End Try
        End Using
    End Sub
    Public Shared Function VrStartInfo(context As InstallContext, settings As VrSettings, channel As String, logFolder As String, Optional joinTarget As String = Nothing) As ProcessStartInfo
        ' Both factories clear inherited experiments before setting their explicit launch flags.
        Dim start = If(settings.LaunchMode = "lan", LanSession.StartInfo(context, settings.SkipIntroduction, joinTarget), DesktopStartInfo(context, Nothing, Nothing))
        For Each name In {"ACTIVE", "REPLAY_PROBE", "INNER_REPLAY", "CONTINUOUS_REPLAY", "HEADSET", "INTERACTIVE", "WIDE_VISIBILITY"}
            start.Environment("DIRT2VR_" & name) = "1"
        Next
        start.Environment("DIRT2VR_CAPTURE_DIAGNOSTICS") = "0"
        start.Environment("DIRT2VR_CAPTURE_REQUESTS") = If(Environment.GetCommandLineArgs().Contains("--diagnostic-capture"), "1", "0")
        start.Environment("DIRT2VR_WATER_REFLECTIONS") = "1"
        start.Environment("DIRT2VR_TRACE_LIGHTS") = "0"
        start.Environment("DIRT2VR_WORLD_SCALE") = "1"
        start.Environment("DIRT2VR_HEADSET_SCALE") = (settings.HeadsetScale / 100.0).ToString(Globalization.CultureInfo.InvariantCulture)
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
        If driving.Enabled AndAlso driving.Bindings.Count > 0 Then Worker.Invoke(context, "setup")
        If settings.LaunchMode = "lan" Then
            Status("Preparing", "LAN multiplayer — use the game's Multiplayer / LAN menus")
            Dim lanStart = LanSession.StartInfo(context, settings.SkipIntroduction, lanJoinTarget)
            ConfigureLogging(lanStart, CreateLogFolder(context, settings.LoggingEnabled))
            Worker.Invoke(context, "prepare-lan")
            WaitForGame(lanStart)
            If Not File.Exists(LanSession.ReceiptPath(context)) Then Throw New IOException("The game exited before LAN startup was confirmed.")
            Return False
        End If
        Dim config As String = Nothing
        Dim logFolder As String = Nothing
        If settings.DirectMode Then
            ' Human control is enabled by the DX11 proxy; desktop rendering settings
            ' stay untouched rather than silently running an AI-driven DX9 session.
            If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before using Direct practice or Race.")
            Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
            Dim dx = TryCast(document.SelectSingleNode("/hardware_settings_config/graphics_card/directx"), Xml.XmlElement)
            If dx Is Nothing OrElse Not String.Equals(dx.GetAttribute("forcedx9"), "false", StringComparison.OrdinalIgnoreCase) Then Throw New IOException("Desktop Direct practice and Race require the game's DX11 renderer (forcedx9=false). Use Game menus for normal DX9 play.")
            Worker.Invoke(context, "setup")
            Status("Preparing", If(settings.LaunchMode = "race", "Desktop race", "Desktop practice"))
            Worker.Invoke(context, "prepare-desktop", settings.CarCode, settings.TrackId, settings.GridOpponents, settings.OpponentCars)
            config = New AssetTransaction(context).PracticeConfig()
            logFolder = CreateLogFolder(context, settings.LoggingEnabled)
        End If
        Dim start = DesktopStartInfo(context, config, logFolder)
        PrepareMenus()
        If config IsNot Nothing Then start.Environment("DIRT2VR_LAPS") = settings.SessionLaps.ToString(Globalization.CultureInfo.InvariantCulture)
        Return WaitForGame(start)
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
        driving.ConfigureProcess(context, start, start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1")
        Dim focus As New StartupFocus(context)
        Using returnChannel As New DirectReturnChannel(start, settings.DirectMode)
            Using child = Process.Start(start)
                Status("Running")
                Dim seenGame As Boolean
                Dim gameAlive As Boolean = True
                Dim nextProcessCheck = DateTime.MinValue
                Dim deadline = DateTime.UtcNow.AddSeconds(30)
                Do
                    Application.DoEvents() : poll?.Invoke()
                    If DateTime.UtcNow >= nextProcessCheck Then
                        focus.Poll()
                        If focusStatus <> focus.Outcome Then
                            focusStatus = focus.Outcome : Status("Running")
                        End If
                        gameAlive = context.GameRunning()
                        seenGame = seenGame Or gameAlive
                        nextProcessCheck = DateTime.UtcNow.AddMilliseconds(250)
                    End If
                    If child.HasExited AndAlso Not gameAlive AndAlso (seenGame OrElse DateTime.UtcNow > deadline) Then Exit Do
                    Thread.Sleep(8)
                Loop
                If Not seenGame Then Throw New IOException("The game did not start. Check that your normal DiRT 2 installation works.")
            End Using
            Return returnChannel.Requested
        End Using
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
