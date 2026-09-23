Public NotInheritable Class PrototypeTrack
    Private Sub New()
    End Sub
    Public Const Id As String = "d2vr_test"
    Public Shared ReadOnly RequiredFiles As String() = {"routesplit.pssg", "track.jpk", "grids.pssg", "ai_track.xml", "dev_ai_track.xml", "ai_vehicle_track.xml", "progress_track.xml", "boundarylines.cqtc", "resetlines.cqtc", "cameralines.cqtc", "route_overrides.xml", "track.vis", "objects.ens", "ornaments.xml", "ornaments.bin"}

    Public Class Receipt
        Public Property Schema As Integer
        Public Property TrackId As String = ""
        Public Property Files As New Dictionary(Of String, String)
    End Class

    Public Shared Function Installed(track As PracticeTrack, context As InstallContext) As Boolean
        Return Directory.Exists(track.Folder(context)) AndAlso (track.Id <> Id OrElse File.Exists(IO.Path.Combine(track.Folder(context), "prototype.json")))
    End Function

    Public Shared Sub Validate(route As String)
        Dim path = IO.Path.Combine(route, "prototype.json")
        Files.NoLinks(path)
        If Not File.Exists(path) Then Throw New IOException("The prototype track installation is incomplete.")
        Dim receipt = Files.ReadJson(Of Receipt)(path)
        If receipt Is Nothing OrElse receipt.Schema <> 1 OrElse receipt.TrackId <> Id OrElse receipt.Files Is Nothing Then Throw New IOException("Unsupported prototype track receipt.")
        For Each name In RequiredFiles
            Dim asset = IO.Path.Combine(route, name)
            Files.NoLinks(asset)
            If Not receipt.Files.ContainsKey(name) OrElse Not File.Exists(asset) OrElse Files.Hash(asset) <> receipt.Files(name) Then Throw New IOException("Prototype track file is missing or changed: " & name)
        Next
    End Sub

    Public Shared Sub ValidateMode(trackId As String, mode As String, vr As Boolean)
        If trackId = Id AndAlso (mode <> "practice" OrElse vr) Then Throw New IOException("The experimental prototype track supports Direct practice with Desktop launch only.")
    End Sub
End Class
