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
    Public Overrides Function ToString() As String
        Return Label & If(Code = "sti", " (tested cockpit)", " (experimental cockpit)")
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
        Dim result = Cars.SingleOrDefault(Function(c) c.Code = code)
        If result Is Nothing Then Throw New IOException("Unknown practice car.")
        Return result
    End Function
    Public Function Track(id As String) As PracticeTrack
        Dim result = Tracks.SingleOrDefault(Function(t) t.Id = id)
        If result Is Nothing Then Throw New IOException("Unknown practice track.")
        Return result
    End Function
    Public Sub ValidateInstalled(context As InstallContext, trackId As String, carCode As String)
        Dim route = Track(trackId).Folder(context)
        Dim camera = IO.Path.Combine(context.GameRoot, "cars", Car(carCode).Code, "cameras.xml")
        Files.NoLinks(route) : Files.NoLinks(camera)
        If Not Directory.Exists(route) OrElse Not File.Exists(camera) Then Throw New IOException("The selected track or car is missing from this game installation.")
    End Sub
    Public Function Config(trackId As String, carCode As String, Optional opponents As Integer = 0) As Byte()
        If opponents < 0 OrElse opponents > 7 Then Throw New IOException("Choose between zero and seven opponents.")
        Dim route = Track(trackId)
        Dim vehicle = Car(carCode)
        Dim document As New XDocument(New XElement("config", New XAttribute("skipreplays", "true"),
            New XElement("track", New XAttribute("country", route.Country), New XAttribute("name", route.Track), New XAttribute("route", route.Route),
                New XElement("car", New XAttribute("name", vehicle.Code), New XAttribute("number", opponents + 1)))))
        Return Text.Encoding.UTF8.GetBytes(document.ToString())
    End Function
End Class
