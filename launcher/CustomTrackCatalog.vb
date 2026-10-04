Imports DiRT2VR.CustomTracks

Public Class CustomTrackPack
    Public ReadOnly Id As String
    Public ReadOnly Name As String
    Public ReadOnly Description As String
    Public ReadOnly Available As Boolean
    Public ReadOnly Layouts As Layout()
    Public Sub New(id As String, name As String, description As String, available As Boolean, layouts As Layout())
        Me.Id = id : Me.Name = name : Me.Description = description : Me.Available = available : Me.Layouts = layouts
    End Sub
    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class

Public Module CustomTrackCatalog
    Public ReadOnly Packs As CustomTrackPack() = {
        New CustomTrackPack(AspenPack.Id, "Aspen", "Four Rallycross layouts plus six desktop practice tests", True, AspenPack.Layouts),
        New CustomTrackPack("smelter", "Smelter", "Ten layouts · Desktop practice tests · Rendering checks pending", True, TrackPacks.Smelter.Layouts)}
    Public Function Find(id As String) As CustomTrackPack
        Return Packs.SingleOrDefault(Function(p) p.Id = id)
    End Function
    Public Function RequireAvailable(id As String) As CustomTrackPack
        Dim pack = Find(id)
        If pack Is Nothing OrElse Not pack.Available Then Throw New IOException("The selected custom pack is not available to launch. Choose an installed pack in CUSTOM tracks.")
        Return pack
    End Function
End Module
