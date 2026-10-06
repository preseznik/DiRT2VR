Imports System.Text.Json
Imports DiRT2VR.CustomTracks

Public Class BestLapRecord
    Public Property LayoutId As String = ""
    Public Property PackVersion As String = ""
    Public Property CarCode As String = ""
    Public Property Milliseconds As Long
    Public Property CompletedUtc As DateTime
End Class
Public Class BestLapHistory
    Public Property Schema As Integer = 1
    Public Property Records As New List(Of BestLapRecord)
End Class
Public Module BestLapStore
    Public Function HistoryPath(context As InstallContext) As String
        Return IO.Path.Combine(context.UserRoot, "custom-best-laps.json")
    End Function
    Public Function Load(context As InstallContext) As BestLapHistory
        Dim filename = HistoryPath(context)
        If Not File.Exists(filename) Then Return New BestLapHistory()
        Dim history = Files.ReadJson(Of BestLapHistory)(filename)
        If history Is Nothing OrElse history.Schema <> 1 OrElse history.Records Is Nothing OrElse history.Records.Count > 100 OrElse
            history.Records.Any(Function(r) Not Valid(r)) OrElse history.Records.Select(Function(r) r.LayoutId & "/" & r.PackVersion).Distinct().Count() <> history.Records.Count Then
            Throw New IOException("Best lap history is invalid. The saved file has been kept.")
        End If
        Return history
    End Function
    Public Function Valid(record As BestLapRecord) As Boolean
        Return record IsNot Nothing AndAlso TrackPacks.Nordschleife.IsLayout(record.LayoutId) AndAlso SafeFiles.Version(record.PackVersion) AndAlso
            RaceCatalog.Current.Cars.Any(Function(c) c.Code = record.CarCode) AndAlso record.Milliseconds >= 1000 AndAlso record.Milliseconds <= 7200000 AndAlso
            record.CompletedUtc.Kind = DateTimeKind.Utc AndAlso record.CompletedUtc >= New DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    End Function
    Public Function Best(context As InstallContext, layoutId As String, version As String) As BestLapRecord
        Return Load(context).Records.SingleOrDefault(Function(r) r.LayoutId = layoutId AndAlso r.PackVersion = version)
    End Function
    Public Function Record(context As InstallContext, lap As BestLapRecord) As Boolean
        If Not Valid(lap) Then Throw New IOException("Invalid completed lap result.")
        Dim history = Load(context)
        Dim previous = history.Records.SingleOrDefault(Function(r) r.LayoutId = lap.LayoutId AndAlso r.PackVersion = lap.PackVersion)
        If previous IsNot Nothing AndAlso previous.Milliseconds <= lap.Milliseconds Then Return False
        If previous IsNot Nothing Then history.Records.Remove(previous)
        history.Records.Add(lap)
        Files.SaveJson(HistoryPath(context), history)
        Return True
    End Function
    Public Function Display(lap As BestLapRecord) As String
        Dim seconds = lap.Milliseconds \ 1000, hundredths = (lap.Milliseconds Mod 1000) \ 10
        Return (seconds \ 60).ToString() & ":" & (seconds Mod 60).ToString("00") & "." & hundredths.ToString("00") & " · " & RaceCatalog.Current.Car(lap.CarCode).Label
    End Function
End Module
