Public NotInheritable Class SteeringAnimationLaunch
    Public Shared Sub RequireDesktopRenderer(context As InstallContext, settings As VrSettings)
        If Not settings.VrSteeringAnimation Then Return
        If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before enabling cockpit steering animation.")
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        If document.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value <> "false" Then
            Throw New IOException("Cockpit steering animation requires DirectX 11. Use the game's DX11 renderer or turn off Controls → Remove artificial steering corrections.")
        End If
    End Sub
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_STEERING_ANIMATION")
        If Not settings.VrSteeringAnimation Then Return
        start.Environment("DIRT2VR_STEERING_ANIMATION") = If(settings.SteeringObserveOnly, "2", "1")
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not start.Environment.ContainsKey("DIRT2VR_HEADSET") OrElse start.Environment("DIRT2VR_HEADSET") <> "1" Then
            start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
        End If
    End Sub
End Class
