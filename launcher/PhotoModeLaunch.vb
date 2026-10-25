Public NotInheritable Class PhotoModeLaunch
    Public Shared Function Eligible(context As InstallContext, settings As VrSettings, vr As Boolean, Optional savedReplay As Boolean = False) As Boolean
        If settings.LaunchMode = "lan" OrElse savedReplay Then Return False
        If vr Then Return True
        If Not File.Exists(context.GraphicsPath) Then Return False
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        Return String.Equals(document.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value, "false", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Function PreserveChase(context As InstallContext, settings As VrSettings, vr As Boolean, Optional savedReplay As Boolean = False) As Boolean
        ' Photo Mode starts from the native external camera even when other VR views are disabled.
        Return settings.VrExtendedViews OrElse settings.VrReplayCameras OrElse Eligible(context, settings, vr, savedReplay)
    End Function

    Public Shared Sub Configure(start As ProcessStartInfo, enabled As Boolean)
        For Each key In start.Environment.Keys.Where(Function(name) name.StartsWith("DIRT2VR_PHOTO_", StringComparison.OrdinalIgnoreCase)).ToArray()
            start.Environment.Remove(key)
        Next
        If Not enabled Then Return
        start.Environment("DIRT2VR_PHOTO_MODE") = "1"
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not start.Environment.ContainsKey("DIRT2VR_HEADSET") OrElse start.Environment("DIRT2VR_HEADSET") <> "1" Then start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
    End Sub
End Class
