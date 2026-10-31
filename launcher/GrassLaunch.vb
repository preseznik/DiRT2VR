Public NotInheritable Class GrassLaunch
    ' Extended grass distance (default) needs the proxy active on desktop too.
    ' Off keeps DiRT 2's original grass budget, and VR's 0.18.0 budget.
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_EXTENDED_GRASS")
        If Not settings.ExtendedGrass Then Return
        start.Environment("DIRT2VR_EXTENDED_GRASS") = "1"
        start.Environment("DIRT2VR_ACTIVE") = "1"
        If Not start.Environment.ContainsKey("DIRT2VR_HEADSET") OrElse start.Environment("DIRT2VR_HEADSET") <> "1" Then start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
    End Sub
End Class
