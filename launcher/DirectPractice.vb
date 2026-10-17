Imports System.Text.Json
Imports System.Xml.Linq

Public Class PracticeTrack
    Public Property Id As String = ""
    Public Property [Event] As String = ""
    Public Property Label As String = ""
    Public Property Country As String = ""
    Public Property Track As String = ""
    Public Property Route As String = ""
    Public Property Circuit As Boolean
    Public Overrides Function ToString() As String
        Return Label
    End Function
    Public Function Folder(context As InstallContext) As String
        Return IO.Path.Combine(context.GameRoot, "tracks", Country, Track, Route)
    End Function
End Class
Public Class PracticeCar
    Public Property Code As String = ""
    Public Property [Event] As String = ""
    Public Property Label As String = ""
    Public Property PackId As String = ""
    Public Property ClassId As String = ""
    Public Property ClassName As String = ""
    Public Overrides Function ToString() As String
        Return Label
    End Function
End Class
Public Class RaceCatalog
    Public Property Tracks As New List(Of PracticeTrack)
    Public Property Cars As New List(Of PracticeCar)
    Public Shared ReadOnly Current As RaceCatalog = Load()
    Private Shared Function Load() As RaceCatalog
        Using stream = GetType(RaceCatalog).Assembly.GetManifestResourceStream("DiRT2VR.race-catalog.json")
            Return JsonSerializer.Deserialize(Of RaceCatalog)(stream)
        End Using
    End Function
    Public Function Car(code As String) As PracticeCar
        If CustomCars.CarPack.IsCustom(code) Then Return New PracticeCar With {.Code = code, .Label = CustomCars.CarPack.Name, .ClassId = "f2004", .ClassName = "Formula 1 (approximation)"}
        Dim external = CustomCars.ExternalCars.Find(code)
        If external IsNot Nothing Then Return New PracticeCar With {.Code = code, .PackId = external.Id, .Label = external.Name & " — External", .ClassId = Car(external.Manifest.Car.Donor).ClassId, .ClassName = external.Manifest.Car.ClassName}
        Dim result = Cars.SingleOrDefault(Function(c) c.Code = code)
        If result Is Nothing Then Throw New IOException("Unknown practice car.")
        Return result
    End Function
    Public Function Track(id As String) As PracticeTrack
        If CustomTracks.TrackPacks.IsLayout(id) Then
            Dim layout = CustomTracks.TrackPacks.ForLayout(id).GetLayout(id)
            Return New PracticeTrack With {.Id = id, .Event = layout.Discipline, .Label = CustomTracks.TrackPacks.ForLayout(id).Name & " — " & layout.Name,
                .Country = "usa", .Track = layout.Folder, .Route = "route_0", .Circuit = layout.Circuit}
        End If
        Dim result = Tracks.SingleOrDefault(Function(t) t.Id = id)
        If result Is Nothing Then Throw New IOException("Unknown practice track.")
        Return result
    End Function
    Public Sub ValidateInstalled(context As InstallContext, trackId As String, carCode As String)
        Dim route = Track(trackId).Folder(context)
        If CustomTracks.TrackPacks.IsLayout(trackId) Then
            Dim external = CustomTracks.TrackPacks.ForLayout(trackId).External
            If external IsNot Nothing Then route = CustomTracks.SafeFiles.Inside(external.Root, "payload/tracks/usa/" & external.Layout(trackId).Folder & "/route_0")
        End If
        Dim camera = IO.Path.Combine(context.GameRoot, "cars", Car(carCode).Code, "cameras.xml")
        Dim externalCar = CustomCars.ExternalCars.Find(carCode)
        If externalCar IsNot Nothing Then
            externalCar.Verify()
            camera = CustomTracks.SafeFiles.Inside(externalCar.Root, "payload/cars/" & carCode & "/cameras.xml")
        End If
        Files.NoLinks(route) : Files.NoLinks(camera)
        If Not Directory.Exists(route) OrElse Not File.Exists(camera) Then Throw New IOException("The selected track or car is missing from this game installation.")
        If CustomCars.CarPack.IsCustom(carCode) Then CustomCars.CarPack.Read(context.GameRoot)
        If trackId = PrototypeTrack.Id Then PrototypeTrack.Validate(route)
        If CustomTracks.TrackPacks.IsLayout(trackId) Then CustomTracks.TrackPacks.ForLayout(trackId).Read(context.GameRoot)
    End Sub
    Public Function Config(trackId As String, carCode As String, Optional opponents As Integer = 0, Optional opponentCars As String = "same", Optional context As InstallContext = Nothing, Optional carGrade As String = "rookie") As Byte()
        CarGrades.Validate(carGrade)
        If opponents < 0 OrElse opponents > 7 Then Throw New IOException("Choose between zero and seven opponents.")
        If Not {"same", "mixed", "class"}.Contains(opponentCars) Then Throw New IOException("Unknown opponent car selection.")
        Dim route = Track(trackId)
        If CustomTracks.TrackPacks.IsLayout(trackId) Then CustomTracks.TrackPacks.ForLayout(trackId).RequireMode(trackId, False, If(opponents = 0, "practice", "race"), carCode, opponents, 1)
        If trackId = PrototypeTrack.Id AndAlso opponents <> 0 Then Throw New IOException("The prototype track supports solo driving only.")
        If CustomCars.CarPack.IsCustom(carCode) Then CustomCars.CarPack.RequireMode(trackId, False, If(opponents = 0, "practice", "race"), opponents)
        Dim externalCar = CustomCars.ExternalCars.Find(carCode)
        If externalCar IsNot Nothing Then CustomCars.ExternalCars.RequireMode(externalCar, False, If(opponents = 0, "practice", "race"))
        Dim vehicle = Car(carCode)
        Dim entries As New List(Of XElement)
        If opponents = 0 OrElse opponentCars = "same" Then
            entries.Add(New XElement("car", New XAttribute("name", vehicle.Code), New XAttribute("number", opponents + 1)))
        Else
            If context Is Nothing Then Throw New IOException("Opponent selection requires a game installation.")
            Dim pool = Cars.Where(Function(c) c.Code <> vehicle.Code AndAlso
                (opponentCars = "mixed" OrElse c.ClassId = vehicle.ClassId) AndAlso
                File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).ToList()
            ' Prefer other models, repeating the pool only when the requested grid is larger.
            If pool.Count = 0 Then pool.Add(vehicle)
            entries.Add(New XElement("car", New XAttribute("name", vehicle.Code), New XAttribute("number", 1)))
            Dim remaining As New List(Of PracticeCar)
            For i = 1 To opponents
                If remaining.Count = 0 Then remaining.AddRange(pool)
                Dim index = Random.Shared.Next(remaining.Count)
                Dim opponent = remaining(index)
                Files.NoLinks(IO.Path.Combine(context.GameRoot, "cars", opponent.Code, "cameras.xml"))
                entries.Add(New XElement("car", New XAttribute("name", opponent.Code), New XAttribute("number", 1)))
                remaining.RemoveAt(index)
            Next
        End If
        Dim document As New XDocument(New XElement("config", New XAttribute("skipreplays", "false"),
            New XElement("track", New XAttribute("country", route.Country), New XAttribute("name", route.Track), New XAttribute("route", route.Route),
                entries)))
        If context IsNot Nothing Then CarGrades.ValidateGrid(context, document, carCode, carGrade)
        Return Text.Encoding.UTF8.GetBytes(document.ToString())
    End Function
End Class
