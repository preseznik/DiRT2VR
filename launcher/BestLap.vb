Imports System.Text.Json
Imports DiRT2VR.CustomTracks

Public Class BestLapRecord
    Public Property LayoutId As String = ""
    Public Property PackVersion As String = ""
    Public Property CarCode As String = ""
    Public Property CarGrade As String = ""
    Public Property CarConversionVersion As String = ""
    Public Property Milliseconds As Long
    Public Property CompletedUtc As DateTime
End Class
Public Class BestLapHistory
    Public Property Schema As Integer = 1
    Public Property Records As New List(Of BestLapRecord)
End Class
Public Module BestLapStore
    Public Function HistoryPath(context As InstallContext, Optional customCar As Boolean = False) As String
        Return IO.Path.Combine(context.UserRoot, If(customCar, "custom-car-best-laps.json", "custom-best-laps.json"))
    End Function
    Public Function Load(context As InstallContext, Optional customCar As Boolean = False) As BestLapHistory
        Dim filename = HistoryPath(context, customCar)
        If Not File.Exists(filename) Then Return New BestLapHistory()
        Dim history = Files.ReadJson(Of BestLapHistory)(filename)
        If history Is Nothing OrElse history.Schema <> 1 OrElse history.Records Is Nothing OrElse history.Records.Count > 100 OrElse
            history.Records.Any(Function(r) Not Valid(r, customCar)) OrElse history.Records.Select(Function(r) r.LayoutId & "/" & r.PackVersion & "/" & r.CarConversionVersion).Distinct().Count() <> history.Records.Count Then
            Throw New IOException("Best lap history is invalid. The saved file has been kept.")
        End If
        Return history
    End Function
    Public Function Valid(record As BestLapRecord, Optional customCar As Boolean = False) As Boolean
        Return record IsNot Nothing AndAlso (record.CarGrade = "" OrElse CarGrades.Values.Contains(record.CarGrade)) AndAlso TrackPacks.Nordschleife.IsLayout(record.LayoutId) AndAlso SafeFiles.Version(record.PackVersion) AndAlso
            If(customCar, CustomCars.CarPack.IsCustom(record.CarCode) AndAlso SafeFiles.Version(record.CarConversionVersion),
                record.CarConversionVersion = "" AndAlso RaceCatalog.Current.Cars.Any(Function(c) c.Code = record.CarCode)) AndAlso record.Milliseconds >= 1000 AndAlso record.Milliseconds <= 7200000 AndAlso
            record.CompletedUtc.Kind = DateTimeKind.Utc AndAlso record.CompletedUtc >= New DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    End Function
    Private Function TimedLayout(layoutId As String) As String
        ' These condition IDs share the same standard circuit and finish line.
        Select Case layoutId
            Case "nordschleife-daylight", "nordschleife-overcast", "nordschleife-evening"
                Return "nordschleife-standard"
            Case Else
                Return layoutId
        End Select
    End Function
    Private Function CompatibleVersion(saved As String, current As String) As Boolean
        ' 1.0.1 adds race grids; the timed course and finish line are unchanged.
        Return saved = current OrElse current = "1.0.1" AndAlso saved = "1.0.0"
    End Function
    Public Function Best(context As InstallContext, layoutId As String, version As String, Optional customCar As Boolean = False) As BestLapRecord
        Return Load(context, customCar).Records.Where(Function(r) TimedLayout(r.LayoutId) = TimedLayout(layoutId) AndAlso
            CompatibleVersion(r.PackVersion, version) AndAlso (Not customCar OrElse r.CarConversionVersion = CustomCars.CarPack.Version)).OrderBy(Function(r) r.Milliseconds).FirstOrDefault()
    End Function
    Public Function Record(context As InstallContext, lap As BestLapRecord) As Boolean
        Dim customCar = CustomCars.CarPack.IsCustom(lap.CarCode)
        If Not Valid(lap, customCar) Then Throw New IOException("Invalid completed lap result.")
        Dim history = Load(context, customCar)
        Dim bestTime = Best(context, lap.LayoutId, lap.PackVersion, customCar)
        If bestTime IsNot Nothing AndAlso bestTime.Milliseconds <= lap.Milliseconds Then Return False
        history.Records.RemoveAll(Function(r) TimedLayout(r.LayoutId) = TimedLayout(lap.LayoutId) AndAlso CompatibleVersion(r.PackVersion, lap.PackVersion) AndAlso r.CarConversionVersion = lap.CarConversionVersion)
        history.Records.Add(lap)
        Files.SaveJson(HistoryPath(context, customCar), history)
        Return True
    End Function
    Public Function Display(lap As BestLapRecord) As String
        Dim seconds = lap.Milliseconds \ 1000, hundredths = (lap.Milliseconds Mod 1000) \ 10
        Return (seconds \ 60).ToString() & ":" & (seconds Mod 60).ToString("00") & "." & hundredths.ToString("00") & " · " & RaceCatalog.Current.Car(lap.CarCode).Label & If(CustomCars.CarPack.IsCustom(lap.CarCode), "", " · " & If(lap.CarGrade = "", "Grade unspecified", CarGrades.GradeLabel(lap.CarGrade)))
    End Function
End Module
