Public Class VrProbeResult
    Public Property ExitCode As Integer
    Public Property Report As String = ""
End Class
Public Class VrLayerRecoveryChoice
    Public Property Retry As Boolean
    Public Property Remember As Boolean
End Class
Public Class VrLayerRecoveryRequest
    Public Property Layers As IReadOnlyList(Of VrLayerBypass)
    Public Property Report As String
End Class

Public Class VrPreflight
    Private ReadOnly context As InstallContext
    Private ReadOnly runtime As VrRuntimeInfo
    Private ReadOnly logFolder As String
    Public ReadOnly Bypasses As New List(Of VrLayerBypass)()
    Public Property Report As String = ""
    Public Property Warning As String = ""

    Public Sub New(context As InstallContext, runtime As VrRuntimeInfo, logFolder As String)
        Me.context = context : Me.runtime = runtime : Me.logFolder = logFolder
    End Sub
    Public Shared Function ProbeStartInfo(executable As String, runtime As VrRuntimeInfo, layers As IReadOnlyList(Of VrLayerBypass)) As ProcessStartInfo
        Dim start As New ProcessStartInfo(executable) With {.UseShellExecute = False, .CreateNoWindow = True, .RedirectStandardOutput = True, .RedirectStandardError = True}
        runtime.Configure(start)
        start.Environment("DIRT2VR_ACTIVE") = "0"
        Dim verbosity As String = Nothing
        start.Environment.TryGetValue("XR_LOADER_DEBUG", verbosity)
        If Not {"warn", "info", "verbose", "all"}.Contains(verbosity) Then start.Environment("XR_LOADER_DEBUG") = "warn"
        VrApiLayers.Configure(start, layers)
        Return start
    End Function
    Public Shared Function RunProbe(executable As String, runtime As VrRuntimeInfo, layers As IReadOnlyList(Of VrLayerBypass)) As VrProbeResult
        Using probe = Process.Start(ProbeStartInfo(executable, runtime, layers))
            Dim stdout = probe.StandardOutput.ReadToEndAsync()
            Dim stderr = probe.StandardError.ReadToEndAsync()
            If Not probe.WaitForExit(30000) Then
                probe.Kill() : probe.WaitForExit()
                Throw New IOException(runtime.Name & " preflight timed out. Start the selected headset software, connect your headset, then retry. You can change VR runtime in Settings.")
            End If
            Return New VrProbeResult With {.ExitCode = probe.ExitCode, .Report = stdout.GetAwaiter().GetResult() & stderr.GetAwaiter().GetResult()}
        End Using
    End Function
    Private Sub Record(result As VrProbeResult, retry As Boolean)
        Report = "selected_runtime_mode=" & runtime.Mode & Environment.NewLine & "selected_runtime_manifest=" & IO.Path.GetFileName(runtime.ManifestPath) & Environment.NewLine &
            "disabled_api_layers=" & String.Join(",", Bypasses.Select(Function(layer) layer.LayerName)) & Environment.NewLine & result.Report
        If logFolder IsNot Nothing Then File.WriteAllText(IO.Path.Combine(logFolder, If(retry, "preflight-retry.txt", "preflight.txt")), Report)
    End Sub
    Private Function Failure(result As VrProbeResult) As String
        Dim layers = VrApiLayers.Failures(result.ExitCode, result.Report)
        If layers.Count > 0 Then Return "An OpenXR layer could not load: " & String.Join("; ", layers.Select(Function(layer) layer.LayerName & " (Windows error " & layer.WindowsError & ")")) & "."
        Return runtime.Name & " could not open a headset session. Connect your headset and check its software. Change VR runtime in Settings to try SteamVR or system OpenXR."
    End Function
    Private Function Details() As String
        Return If(logFolder Is Nothing, " Enable diagnostic logging in Settings to save the full preflight report.", " Details are in " & logFolder & ".")
    End Function
    Public Function Check(run As Func(Of IReadOnlyList(Of VrLayerBypass), VrProbeResult),
                          confirm As Func(Of VrLayerRecoveryRequest, VrLayerRecoveryChoice),
                          Optional discover As Func(Of List(Of VrLayerBypass)) = Nothing) As Boolean
        If discover Is Nothing Then discover = AddressOf VrApiLayers.Discover
        Warning = ""
        Dim catalog = discover()
        Dim valid = VrApiLayers.Revalidate(Bypasses.Concat(VrLayerPreferences.Load(context).Bypasses), catalog)
        Bypasses.Clear() : Bypasses.AddRange(valid)
        Dim first = run(Bypasses)
        Record(first, False)
        If first.ExitCode = 0 Then Return True
        Dim original = Report
        Dim failures = VrApiLayers.Failures(first.ExitCode, first.Report)
        Dim candidates = VrApiLayers.Resolve(failures, catalog)
        If candidates.Count = 0 OrElse candidates.Any(Function(layer) Bypasses.Any(Function(item) item.SameIdentity(layer))) Then
            Throw New IOException(Failure(first) & If(failures.Count > 0, " No supported automatic layer bypass is available.", "") & Details())
        End If
        If confirm Is Nothing Then Throw New IOException(Failure(first) & " Launch interactively to approve disabling the layer for DiRT2VR." & Details())
        Dim choice = confirm(New VrLayerRecoveryRequest With {.Layers = candidates, .Report = original})
        If choice Is Nothing OrElse Not choice.Retry Then Return False
        ' Re-read after the dialog: never apply approval to a manifest changed while it was open.
        If VrApiLayers.Revalidate(Bypasses.Concat(candidates), discover()).Count <> Bypasses.Count + candidates.Count Then Throw New IOException("The OpenXR layer registration changed. Launch VR again to review it.")
        Bypasses.AddRange(candidates)
        Dim retry As VrProbeResult
        Try
            retry = run(Bypasses)
        Catch ex As IOException
            Throw New IOException("VR retry failed: " & ex.Message & Environment.NewLine & "Original failure: " & Failure(first) & Details(), ex)
        End Try
        Record(retry, True)
        If retry.ExitCode <> 0 Then Throw New IOException("VR still could not start after disabling " & String.Join(", ", candidates.Select(Function(layer) layer.DisplayName)) & ". " &
            Failure(retry) & Environment.NewLine & "Original failure: " & Failure(first) & Details())
        If choice.Remember Then
            Try
                VrLayerPreferences.Remember(context, candidates)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Warning = "The OpenXR layer bypass works for this session, but could not be remembered: " & ex.Message
            End Try
        End If
        Return True
    End Function
End Class
