Imports DiRT2VR.CustomTracks

Public NotInheritable Class PhotoModeLaunch
    Public Shared Function Eligible(context As InstallContext, settings As VrSettings, vr As Boolean, Optional savedReplay As Boolean = False) As Boolean
        ' Saved replays get Photo Mode in their paused replay menu (Direct playback).
        If settings.LaunchMode = "lan" Then Return False
        If vr Then Return True
        If Not File.Exists(context.GraphicsPath) Then Return False
        Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
        Return String.Equals(document.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value, "false", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Function PreserveChase(context As InstallContext, settings As VrSettings, vr As Boolean, Optional savedReplay As Boolean = False) As Boolean
        ' Every VR LAN peer prepares the same camera file, whatever its own view
        ' options: a host keeping chase views dropped a client without them at the
        ' race start, through the game's race-loading check path (ProJict 8ac0f636).
        If settings.LaunchMode = "lan" Then Return False
        ' Photo Mode starts from the native external camera even when other VR views are disabled.
        ' Replay Photo Mode takes over the replay's own camera, so saved playback keeps its camera files.
        Return settings.VrExtendedViews OrElse settings.VrReplayCameras OrElse (Not savedReplay AndAlso Eligible(context, settings, vr))
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

    ' Desktop Photo Mode can apply the colour part of the player's filter presets
    ' (Graphics > Filters) as live looks. One line per preset, daytime values:
    ' name, brightness, contrast, saturation, gamma, tint RGB (hex) and tint strength.
    Public Shared Sub ConfigureLooks(start As ProcessStartInfo, context As InstallContext)
        start.Environment.Remove("DIRT2VR_PHOTO_LOOKS")
        Try
            Dim warnings As String = Nothing
            Dim lines As New List(Of String)
            For Each preset In New FilterStore(context).List(warnings)
                If preset.Id = "original" Then Continue For
                Dim day = preset.Day
                Dim value = Function(key As String) day.Value(FilterParameters.ByKey(key))
                Dim tint = day.TintEnabled AndAlso day.TintStrength > 0
                If value("brightness") = 0 AndAlso value("contrast") = 100 AndAlso value("saturation") = 100 AndAlso value("gamma") = 100 AndAlso Not tint Then Continue For
                lines.Add(String.Join(vbTab, preset.Name, value("brightness"), value("contrast"), value("saturation"), value("gamma"),
                                      If(tint, day.TintRgb, "FFFFFF"), If(tint, day.TintStrength, 0)))
                If lines.Count >= 64 Then Exit For
            Next
            If lines.Count = 0 Then Return
            Dim path = SafeFiles.Inside(context.UserRoot, "photo-looks.txt")
            SafeFiles.Atomic(path, New Text.UTF8Encoding(False).GetBytes(String.Join(vbLf, lines) & vbLf))
            start.Environment("DIRT2VR_PHOTO_LOOKS") = path
        Catch ex As IOException
            ' A damaged filter library must not stop the game: Photo Mode keeps its own looks.
        End Try
    End Sub
End Class
