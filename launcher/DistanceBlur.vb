' Desktop distance blur while driving: far scenery and the sky soften with distance (DiRT2VR's own pass on the
' scene depth; the HUD stays sharp). 0 Off, 1 Slight, 2 Medium, 3 Strong. VR launches never get it.
Public NotInheritable Class DistanceBlur
    Public Shared ReadOnly Labels As String() = {"Off", "Slight", "Medium", "Strong"}
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_DISTANCE_BLUR")
        If settings.DistanceBlur < 0 OrElse settings.DistanceBlur > 3 Then Throw New IOException("Choose Off, Slight, Medium or Strong distance blur.")
        If settings.DistanceBlur = 0 Then Return
        If start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1" Then Return
        start.Environment("DIRT2VR_DISTANCE_BLUR") = settings.DistanceBlur.ToString(Globalization.CultureInfo.InvariantCulture)
    End Sub
End Class
