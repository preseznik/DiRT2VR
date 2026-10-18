Public NotInheritable Class DesktopShadows
    Public Shared ReadOnly Distances As Integer() = {0, 160, 240, 320, 400, 480, 560, 640}
    Public Shared Sub RequireDesktopRenderer(context As InstallContext, settings As VrSettings)
        If settings.DesktopShadowDistance = 0 Then Return
        If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before overriding shadow distance.")
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        If document.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value <> "false" Then
            Throw New IOException("Shadow distance overrides require DirectX 11. Use the game's DX11 renderer or choose Game under Graphics → Main → Desktop → Shadow distance.")
        End If
    End Sub
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_DESKTOP_SHADOW_DISTANCE")
        start.Environment.Remove("DIRT2VR_VR_SHADOW_DISTANCE")
        Dim distance = settings.DesktopShadowDistance
        If Not Distances.Contains(distance) Then Throw New IOException("Choose Game or a listed shadow distance up to 640 m.")
        If distance = 0 Then Return
        If start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1" Then
            If settings.DesktopShadowDistanceInVr AndAlso settings.VrShadows Then
                start.Environment("DIRT2VR_VR_SHADOW_DISTANCE") = distance.ToString(Globalization.CultureInfo.InvariantCulture)
            End If
            Return
        End If
        start.Environment("DIRT2VR_DESKTOP_SHADOW_DISTANCE") = distance.ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_ACTIVE") = "1"
        start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
    End Sub
End Class
