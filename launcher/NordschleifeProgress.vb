Imports DiRT2VR.CustomTracks

Public Module NordschleifeProgress
    Public Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_NORDSCHLEIFE_CHECKPOINTS")
        If settings.DirectMode AndAlso TrackPacks.Nordschleife.IsLayout(settings.TrackId) AndAlso settings.SessionLaps = 1 Then start.Environment("DIRT2VR_NORDSCHLEIFE_CHECKPOINTS") = "1"
    End Sub
    Public Function Patch(original As Byte()) As Byte()
        Return Nordschleife.CircuitProgress.Patch(original)
    End Function
End Module
