Public Module DirectRaceStart
    Public ReadOnly Values As String() = {"auto", "staggered", "grid"}
    Public ReadOnly Labels As String() = {"Automatic", "Rally (staggered)", "Grid race"}

    Public Sub Validate(value As String)
        If Not Values.Contains(value) Then Throw New IOException("Choose a valid race start format.")
    End Sub

    Public Function SupportsStaggered(trackId As String) As Boolean
        ' Prepared custom routes do not yet declare staggered placement support.
        ' A point-to-point route alone is not sufficient (Raid also uses them).
        Dim track = RaceCatalog.Current.Tracks.SingleOrDefault(Function(t) t.Id = trackId)
        Return track IsNot Nothing AndAlso Not track.Circuit AndAlso {"Rally", "Trailblazer"}.Contains(track.Event)
    End Function

    Public Function Resolve(settings As VrSettings) As String
        Validate(settings.RaceStart)
        If settings.LaunchMode <> "race" Then Return "grid"
        Dim supported = SupportsStaggered(settings.TrackId)
        If settings.RaceStart = "staggered" AndAlso Not supported Then
            Throw New IOException("Staggered starts require a supported stock Rally or Trailblazer stage. Choose Grid race for this track.")
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
