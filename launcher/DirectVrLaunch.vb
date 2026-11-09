Imports System.Threading.Tasks
Imports System.Windows.Forms

Friend Module DirectVrLaunch
    Public Async Function ChooseAsync(owner As Form, context As InstallContext, packageRoot As String) As Task(Of Action(Of ProcessStartInfo, String))
        Dim settings = VrSettings.Load(context)
        Dim runtime = VrRuntime.Choose(context, settings)
        If runtime Is Nothing Then Return Nothing
        settings.Runtime = runtime.ManifestPath : settings.RuntimeMode = runtime.Mode
        Dim executable = VrPreflight.VerifiedProbe(packageRoot)
        Dim preflight As New VrPreflight(context, runtime, Nothing)
        Dim review As Func(Of VrLayerRecoveryRequest, VrLayerRecoveryChoice) =
            Function(request)
                If owner.IsDisposed OrElse Not owner.IsHandleCreated Then Return Nothing
                Return DirectCast(owner.Invoke(New Func(Of VrLayerRecoveryChoice)(Function() VrLayerRecoveryForm.Confirm(request))), VrLayerRecoveryChoice)
            End Function
        Dim passed = Await Task.Run(Function() preflight.Check(Function(layers) VrPreflight.RunProbe(executable, runtime, layers), review))
        If Not passed OrElse owner.IsDisposed Then Return Nothing
        settings.HeadsetEye = VrResolution.EyeFromReport(preflight.Report)
        Return Sub(start, run) ConfigurePrivateGame(start, run, settings, preflight.Bypasses)
    End Function

    Friend Sub ConfigurePrivateGame(start As ProcessStartInfo, run As String, settings As VrSettings, layers As IReadOnlyList(Of VrLayerBypass))
        Dim hardware = IO.Path.Combine(run, "Documents", "My Games", "DiRT2", "hardwaresettings", "hardware_settings_config.xml")
        Dim privateContext As New InstallContext(start.WorkingDirectory, run, hardware)
        Call (New GraphicsTransaction(privateContext)).Prepare(settings, forceDx11:=True)
        Session.ConfigureVrRenderer(start, settings, Nothing, run, layers)
    End Sub
End Module
