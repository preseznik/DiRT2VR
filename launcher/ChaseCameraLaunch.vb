Public NotInheritable Class ChaseCameraLaunch
    Public Shared Sub RequireDesktopRenderer(context As InstallContext, settings As VrSettings)
        If Not settings.ChaseFreeLook Then Return
        If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before enabling chase-camera free look.")
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        If document.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value <> "false" Then
            Throw New IOException("Chase-camera free look requires DirectX 11. Use the game's DX11 renderer or turn off Controls → General → Chase camera → Free look.")
        End If
    End Sub

    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        For Each key In {"DIRT2VR_CHASE_FREE_LOOK", "DIRT2VR_CHASE_MOUSE_MODE", "DIRT2VR_CHASE_MOUSE_SENSITIVITY", "DIRT2VR_CHASE_STICK_SENSITIVITY", "DIRT2VR_CHASE_INVERT_VERTICAL", "DIRT2VR_CHASE_PROBE"}
            start.Environment.Remove(key)
        Next
        Dim headset = start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1"
        If Not settings.ChaseFreeLook OrElse (headset AndAlso Not settings.VrExtendedViews AndAlso Not settings.VrReplayCameras) Then Return
        start.Environment("DIRT2VR_CHASE_FREE_LOOK") = "1"
        start.Environment("DIRT2VR_CHASE_MOUSE_MODE") = settings.ChaseMouseMode
        start.Environment("DIRT2VR_CHASE_MOUSE_SENSITIVITY") = settings.ChaseMouseSensitivity.ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_CHASE_STICK_SENSITIVITY") = settings.ChaseStickSensitivity.ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_CHASE_INVERT_VERTICAL") = If(settings.ChaseInvertVertical, "1", "0")
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not headset Then start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
    End Sub
End Class
