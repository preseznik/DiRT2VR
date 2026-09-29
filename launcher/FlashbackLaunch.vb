Public NotInheritable Class FlashbackLaunch
    Public Shared Function Enabled(settings As VrSettings) As Boolean
        Return settings.ExperimentalFlashback AndAlso settings.LaunchMode <> "lan"
    End Function
    Public Shared Sub RequireDesktopRenderer(context As InstallContext, settings As VrSettings)
        If Not Enabled(settings) Then Return
        If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before enabling experimental rewind.")
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        Dim dx = TryCast(document.SelectSingleNode("/hardware_settings_config/graphics_card/directx"), Xml.XmlElement)
        If dx Is Nothing OrElse Not String.Equals(dx.GetAttribute("forcedx9"), "false", StringComparison.OrdinalIgnoreCase) Then
            Throw New IOException("Experimental rewind requires DirectX 11. Use the game's DX11 renderer or turn off Advanced → Rewind.")
        End If
    End Sub
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_FLASHBACK60")
        If Not Enabled(settings) Then Return
        start.Environment("DIRT2VR_FLASHBACK60") = "1"
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not start.Environment.ContainsKey("DIRT2VR_HEADSET") OrElse start.Environment("DIRT2VR_HEADSET") <> "1" Then
            start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
        End If
    End Sub
End Class
