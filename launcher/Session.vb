Imports System.IO.MemoryMappedFiles
Imports System.Windows.Forms

Public Class SessionStatus
    Public Property State As String = ""
    Public Property Message As String = ""
    Public Property ProcessId As Integer
    Public Property UpdatedUtc As DateTime = DateTime.UtcNow
End Class
Public Class Session
    Private ReadOnly context As InstallContext
    Private ReadOnly settings As VrSettings
    Public Sub New(value As InstallContext)
        context = value : settings = VrSettings.Load(context)
    End Sub
    Private Sub Status(state As String, Optional message As String = "")
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "session.json"), New SessionStatus With {.State = state, .Message = message, .ProcessId = Environment.ProcessId})
    End Sub
    Public Sub Run()
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
                Status("Checking")
                context.ValidateGame() : context.RequireClosed()
                If Not File.Exists(settings.Runtime) OrElse IO.Path.GetFileName(settings.Runtime) <> "steamxr_win32.json" Then Throw New IOException("Select SteamVR's steamxr_win32.json runtime, then start SteamVR and connect your headset.")
                If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once to create graphics settings.")
                Status("Restoring", "Checking for an interrupted session")
                graphics.Recover() : Worker.Invoke(context, "recover")
                Worker.Invoke(context, "setup")
                Status("Preparing")
                Dim logFolder = IO.Path.Combine(context.UserRoot, "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"))
                Directory.CreateDirectory(logFolder)
                ProbeRuntime(logFolder)
                Dim channel = "Local\DiRT2VR.Input." & Guid.NewGuid().ToString("N")
                Using mapping = MemoryMappedFile.CreateNew(channel, 16), view = mapping.CreateViewAccessor()
                    view.Write(0, &H32565244) : view.Write(4, 1) : view.Write(8, 0UI) : view.Write(12, 0UI)
                    Dim start As New ProcessStartInfo(IO.Path.Combine(context.GameRoot, "dirt2.exe")) With {.UseShellExecute = False, .WorkingDirectory = context.GameRoot}
                    ' Whitelist launch options. Do not inherit developer experiments.
                    For Each name In start.Environment.Keys.Where(Function(k) k.StartsWith("DIRT2VR_", StringComparison.OrdinalIgnoreCase)).ToArray()
                        start.Environment.Remove(name)
                    Next
                    For Each name In {"ACTIVE", "REPLAY_PROBE", "INNER_REPLAY", "CONTINUOUS_REPLAY", "HEADSET", "INTERACTIVE", "SKIP_WATER", "WIDE_VISIBILITY"}
                        start.Environment("DIRT2VR_" & name) = "1"
                    Next
                    start.Environment("DIRT2VR_CAPTURE_DIAGNOSTICS") = "0"
                    start.Environment("DIRT2VR_TRACE_LIGHTS") = "0"
                    start.Environment("DIRT2VR_WORLD_SCALE") = "1"
                    start.Environment("DIRT2VR_OUTPUT") = logFolder
                    start.Environment("DIRT2VR_INPUT_CHANNEL") = channel
                    start.Environment("DIRT2VR_KEYS") = $"{settings.ToggleKey}:{settings.ToggleModifiers},{settings.RecenterKey}:{settings.RecenterModifiers}"
                    start.Environment("XR_RUNTIME_JSON") = settings.Runtime
                    Worker.Invoke(context, "prepare")
                    graphics.Prepare()
                    Using input As New ControllerInput(), machine As New BindingMachine(settings.Bindings)
                        Dim counts As UInteger() = {0UI, 0UI}
                        AddHandler input.StateChanged, Sub(sample)
                            For Each action In machine.Update(sample)
                                If Not ControllerInput.GameFocused() Then Continue For
                                counts(action) = CUInt((CLng(counts(action)) + 1) And &HFFFFFFFFL)
                                view.Write(8 + action * 4, counts(action))
                            Next
                        End Sub
                        Using child = Process.Start(start)
                            Status("Running")
                            Dim seenGame As Boolean
                            Dim gameAlive As Boolean = True
                            Dim nextProcessCheck = DateTime.MinValue
                            Dim deadline = DateTime.UtcNow.AddSeconds(30)
                            Do
                                Application.DoEvents() : input.Poll()
                                If DateTime.UtcNow >= nextProcessCheck Then
                                    gameAlive = context.GameRunning()
                                    seenGame = seenGame Or gameAlive
                                    nextProcessCheck = DateTime.UtcNow.AddMilliseconds(250)
                                End If
                                If child.HasExited AndAlso Not gameAlive AndAlso (seenGame OrElse DateTime.UtcNow > deadline) Then Exit Do
                                Thread.Sleep(8)
                            Loop
                            If Not seenGame Then Throw New IOException("The game did not start. Check that your normal DiRT 2 installation works.")
                        End Using
                    End Using
                End Using
                Status("Restoring")
                graphics.Recover() : Worker.Invoke(context, "recover")
                Status("Ready", "Original files restored")
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
            File.WriteAllText(IO.Path.Combine(logFolder, "preflight.txt"), report)
            If probe.ExitCode <> 0 Then Throw New IOException("SteamVR could not open a headset session. Connect your headset and check SteamVR. Details are in " & IO.Path.Combine(logFolder, "preflight.txt"))
        End Using
    End Sub
End Class
