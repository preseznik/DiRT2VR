Public Module DirectRaceStart
    Public ReadOnly Values As String() = {"auto", "staggered", "grid"}
    Public ReadOnly Labels As String() = {"Automatic", "Rally (staggered)", "Grid race"}

    Public Sub Validate(value As String)
        If Not Values.Contains(value) Then Throw New IOException("Choose a valid race start format.")
    End Sub

    Public Function SupportsStaggered(trackId As String) As Boolean
        ' Custom routes require explicit authored placement/timing capability.
        If CustomTracks.TrackPacks.IsLayout(trackId) Then Return CustomTracks.TrackPacks.ForLayout(trackId).SupportsStaggered(trackId)
        Dim track = RaceCatalog.Current.Tracks.SingleOrDefault(Function(t) t.Id = trackId)
        Return track IsNot Nothing AndAlso Not track.Circuit AndAlso {"Rally", "Trailblazer"}.Contains(track.Event)
    End Function

    Public Function Resolve(settings As VrSettings) As String
        Validate(settings.RaceStart)
        If settings.LaunchMode <> "race" Then Return "grid"
        Dim supported = SupportsStaggered(settings.TrackId)
        If settings.RaceStart = "staggered" AndAlso Not supported Then
            Throw New IOException("Staggered starts require a supported Rally or Trailblazer stage.")
        End If
        If settings.RaceStart = "grid" AndAlso supported AndAlso CustomTracks.TrackPacks.IsLayout(settings.TrackId) AndAlso Not CustomTracks.TrackPacks.ForLayout(settings.TrackId).SupportsGridRace(settings.TrackId) Then
            Throw New IOException("This custom layout supports Rally (staggered) races. Choose Automatic or Rally (staggered).")
        End If
        Return If(settings.RaceStart = "staggered" OrElse (settings.RaceStart = "auto" AndAlso supported), "staggered", "grid")
    End Function

    Public Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_STAGGERED_DISCIPLINE")
        If Resolve(settings) = "staggered" Then
            start.Environment("DIRT2VR_STAGGERED_DISCIPLINE") = RaceCatalog.Current.Track(settings.TrackId).Event.ToLowerInvariant()
        End If
    End Sub
End Module
