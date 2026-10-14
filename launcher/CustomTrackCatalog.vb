Imports DiRT2VR.CustomTracks

Public Class CustomTrackPack
    Public ReadOnly Id As String
    Public ReadOnly Name As String
    Public ReadOnly Description As String
    Public ReadOnly Available As Boolean
    Public ReadOnly Layouts As Layout()
    Public ReadOnly External As ExternalPack
    Public ReadOnly Property SourceGame As String
        Get
            Return If(External IsNot Nothing, "external", If(Available, TrackPacks.Get(Id).SourceGame, ""))
        End Get
    End Property
    Public Sub New(id As String, name As String, description As String, available As Boolean, layouts As Layout(), Optional external As ExternalPack = Nothing)
        Me.Id = id : Me.Name = name : Me.Description = description : Me.Available = available : Me.Layouts = layouts
        Me.External = external
    End Sub
    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Module CustomTrackCatalog
    Private ReadOnly builtIn As CustomTrackPack() = {
        New CustomTrackPack(AspenPack.Id, "Aspen", "Ten layouts · Direct practice and Race · Desktop and VR", True, AspenPack.Layouts),
        New CustomTrackPack("smelter", "Smelter", "Ten layouts · Direct practice and Race · Desktop and VR", True, TrackPacks.Smelter.Layouts),
        New CustomTrackPack("nordschleife", "Nordschleife", "Standard circuit · Desktop and VR · Daylight, overcast and evening", True, TrackPacks.Nordschleife.Layouts),
        New CustomTrackPack("mizu-mountain", "Mizu Mountain", "10.4 km point-to-point · Desktop and VR · Direct practice and Race", True, TrackPacks.Mizu.Layouts),
        New CustomTrackPack("misty-loch", "Misty Loch", "6.45 km circuit · Direct practice and Race · Desktop and VR", True, TrackPacks.Misty.Layouts),
        New CustomTrackPack("spa-francorchamps", "Spa-Francorchamps", "7 km GP circuit · Direct practice and Race · Desktop and VR", True, TrackPacks.Spa.Layouts)}
    Public ReadOnly Property Packs As CustomTrackPack()
        Get
            Return builtIn.Concat(TrackPacks.External.Where(Function(p) p.Kind = "track").Select(Function(p)
                Return New CustomTrackPack(p.Id, p.Name, If(p.Ready, p.Manifest.Description & " · " & p.Manifest.Author & " · External", p.Problem), p.Ready,
                    If(p.Ready, TrackPacks.Get(p.Id).Layouts, Array.Empty(Of Layout)()), p)
            End Function)).ToArray()
        End Get
    End Property
    Public Function Find(id As String) As CustomTrackPack
        Return Packs.SingleOrDefault(Function(p) p.Id = id)
    End Function
    Public Function RequireAvailable(id As String) As CustomTrackPack
        Dim pack = Find(id)
        If pack Is Nothing OrElse Not pack.Available Then Throw New IOException("The selected custom pack is not available to launch. Choose an installed pack in CUSTOM tracks.")
        Return pack
    End Function
End Module
