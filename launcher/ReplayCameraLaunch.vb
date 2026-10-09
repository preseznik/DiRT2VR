Public NotInheritable Class ReplayCameraLaunch
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_REPLAY_CAMERAS")
        start.Environment.Remove("DIRT2VR_REPLAY_STEREO")
        If settings.LaunchMode = "lan" Then Return
        Dim headset = start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1"
        Dim stereo = headset AndAlso settings.VrReplayCameras
        Dim orbit = settings.ChaseFreeLook AndAlso (Not headset OrElse settings.VrExtendedViews OrElse stereo)
        If Not stereo AndAlso Not orbit Then Return
        start.Environment("DIRT2VR_REPLAY_CAMERAS") = "1"
        If stereo Then start.Environment("DIRT2VR_REPLAY_STEREO") = "1"
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not headset Then start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
    End Sub
End Class
