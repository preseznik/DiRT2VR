' For wheels DiRT 2 doesn't recognise (newer wheels such as the Logitech G920),
' its force feedback option is off again at every launch, so the choice made in
' Options is lost. Switch it back on at each race start (ProJict 2ec8701d).
Public NotInheritable Class ForceFeedbackLaunch
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_FORCE_FEEDBACK")
        If Not settings.ForceFeedbackOn Then Return
        start.Environment("DIRT2VR_FORCE_FEEDBACK") = "1"
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not start.Environment.ContainsKey("DIRT2VR_HEADSET") OrElse start.Environment("DIRT2VR_HEADSET") <> "1" Then
            start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
        End If
    End Sub
End Class
