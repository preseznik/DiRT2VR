Public Module DirectRaceDifficulty
    Public ReadOnly Labels As String() = {"Use game setting", "Easy", "Casual", "Serious", "Savage", "Extreme", "Hardcore"}
    Public Sub Validate(value As Integer)
        If value < -1 OrElse value > 5 Then Throw New IOException("Choose a valid race difficulty.")
    End Sub
    Public Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        Validate(settings.RaceDifficulty)
        start.Environment.Remove("DIRT2VR_RACE_DIFFICULTY")
        If settings.LaunchMode = "race" AndAlso settings.RaceDifficulty >= 0 Then
            start.Environment("DIRT2VR_RACE_DIFFICULTY") = settings.RaceDifficulty.ToString(Globalization.CultureInfo.InvariantCulture)
        End If
    End Sub
End Module
