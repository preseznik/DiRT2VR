Public NotInheritable Class AnisotropicFiltering
    Public Shared ReadOnly Levels As Integer() = {0, 1, 2, 4, 8, 16}
    Public Shared Sub RequireDesktopRenderer(context As InstallContext, settings As VrSettings)
        If settings.DesktopAnisotropy = 0 Then Return
        If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before overriding anisotropic filtering.")
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        If document.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value <> "false" Then
            Throw New IOException("Anisotropic filtering overrides require DirectX 11. Use the game's DX11 renderer or choose Game under Graphics → Main → Desktop → Anisotropic filtering.")
        End If
    End Sub
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_ANISOTROPY")
        Dim headset = start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1"
        Dim strength = If(headset, settings.VrAnisotropy, settings.DesktopAnisotropy)
        If Not Levels.Contains(strength) Then Throw New IOException("Choose Game, Off, 2×, 4×, 8× or 16× anisotropic filtering.")
        If strength = 0 Then Return
        start.Environment("DIRT2VR_ANISOTROPY") = strength.ToString(Globalization.CultureInfo.InvariantCulture)
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not headset Then start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
    End Sub
End Class
