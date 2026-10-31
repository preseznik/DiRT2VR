Imports System.Text.Json
Imports System.Text.RegularExpressions
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
    ' Up to 0.18.0 only Nordschleife was timed, in the legacy files. Those stay
    ' readable by older launchers; this launcher reads them but never writes them.
    Public Function HistoryPath(context As InstallContext, Optional customCar As Boolean = False, Optional legacy As Boolean = False) As String
        Return IO.Path.Combine(context.UserRoot, If(legacy, If(customCar, "custom-car-best-laps.json", "custom-best-laps.json"), If(customCar, "custom-car-lap-records.json", "lap-records.json")))
    End Function
    Public Function Load(context As InstallContext, Optional customCar As Boolean = False, Optional legacy As Boolean = False) As BestLapHistory
        Dim filename = HistoryPath(context, customCar, legacy)
        If Not File.Exists(filename) Then Return New BestLapHistory()
        Dim history = Files.ReadJson(Of BestLapHistory)(filename)
        If history Is Nothing OrElse history.Schema <> 1 OrElse history.Records Is Nothing OrElse history.Records.Count > If(legacy, 100, 1000) OrElse
            history.Records.Any(Function(r) Not Valid(r, customCar)) OrElse history.Records.Select(Function(r) r.LayoutId & "/" & r.PackVersion & "/" & r.CarConversionVersion).Distinct().Count() <> history.Records.Count Then
            Throw New IOException("Best lap history is invalid. The saved file has been kept.")
        End If
        Return history
    End Function
    ' Stock routes, built-in conversions and External layouts. External IDs are
    ' checked by form so removing a pack keeps the rest of the history readable.
    Public Function Timed(trackId As String) As Boolean
        If String.IsNullOrEmpty(trackId) OrElse trackId = PrototypeTrack.Id Then Return False
        Return RaceCatalog.Current.Tracks.Any(Function(t) t.Id = trackId) OrElse TrackPacks.BuiltIn.Any(Function(p) p.IsLayout(trackId)) OrElse
            Regex.IsMatch(trackId, "\Aexternal-track-[a-z0-9][a-z0-9-]{0,47}/[a-z0-9][a-z0-9-]{0,47}\z")
    End Function
    Private Function Stock(trackId As String) As Boolean
        Return RaceCatalog.Current.Tracks.Any(Function(t) t.Id = trackId)
    End Function
    Public Function Valid(record As BestLapRecord, Optional customCar As Boolean = False) As Boolean
        Return record IsNot Nothing AndAlso (record.CarGrade = "" OrElse CarGrades.Values.Contains(record.CarGrade)) AndAlso Timed(record.LayoutId) AndAlso
            If(Stock(record.LayoutId), record.PackVersion = "", SafeFiles.Version(record.PackVersion)) AndAlso
            If(customCar, CustomCars.CarPack.IsCustom(record.CarCode) AndAlso SafeFiles.Version(record.CarConversionVersion),
                record.CarConversionVersion = "" AndAlso RaceCatalog.Current.Cars.Any(Function(c) c.Code = record.CarCode)) AndAlso record.Milliseconds >= 1000 AndAlso record.Milliseconds <= 7200000 AndAlso
            record.CompletedUtc.Kind = DateTimeKind.Utc AndAlso record.CompletedUtc >= New DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    End Function
    ' Stock routes have no pack version. Selective builds record each layout's own
    ' version; a pack that is not installed shows results for its current version.
    Public Function TrackVersion(context As InstallContext, trackId As String, Optional receipt As PackReceipt = Nothing) As String
        If Not TrackPacks.IsLayout(trackId) Then Return ""
        Dim pack = TrackPacks.ForLayout(trackId)
        If receipt Is Nothing Then
            If Not File.Exists(SafeFiles.Inside(context.GameRoot, pack.Receipt)) Then Return pack.Version
            receipt = pack.Read(context.GameRoot, False)
        End If
        Return If(receipt.Sessions.FirstOrDefault(Function(s) s.LayoutId = trackId)?.Version, receipt.Version)
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
    Private Function CompatibleVersion(layoutId As String, saved As String, current As String) As Boolean
        ' Nordschleife 1.0.1 adds race grids; the timed course and finish line are unchanged.
        Return saved = current OrElse TrackPacks.Nordschleife.IsLayout(layoutId) AndAlso current = "1.0.1" AndAlso saved = "1.0.0"
    End Function
    Public Function Best(context As InstallContext, layoutId As String, version As String, Optional customCar As Boolean = False) As BestLapRecord
        Return Load(context, customCar, True).Records.Concat(Load(context, customCar).Records).Where(Function(r) TimedLayout(r.LayoutId) = TimedLayout(layoutId) AndAlso
            CompatibleVersion(layoutId, r.PackVersion, version) AndAlso (Not customCar OrElse r.CarConversionVersion = CustomCars.CarPack.Version)).OrderBy(Function(r) r.Milliseconds).FirstOrDefault()
    End Function
    Public Function Record(context As InstallContext, lap As BestLapRecord) As Boolean
        Dim customCar = CustomCars.CarPack.IsCustom(lap.CarCode)
        If Not Valid(lap, customCar) Then Throw New IOException("Invalid completed lap result.")
        Dim bestTime = Best(context, lap.LayoutId, lap.PackVersion, customCar)
        If bestTime IsNot Nothing AndAlso bestTime.Milliseconds <= lap.Milliseconds Then Return False
        Dim history = Load(context, customCar)
        history.Records.RemoveAll(Function(r) TimedLayout(r.LayoutId) = TimedLayout(lap.LayoutId) AndAlso CompatibleVersion(lap.LayoutId, r.PackVersion, lap.PackVersion) AndAlso r.CarConversionVersion = lap.CarConversionVersion)
        history.Records.Add(lap)
        Files.SaveJson(HistoryPath(context, customCar), history)
        Return True
    End Function
    Public Function Time(milliseconds As Long) As String
        Dim seconds = milliseconds \ 1000, hundredths = (milliseconds Mod 1000) \ 10
        Return (seconds \ 60).ToString() & ":" & (seconds Mod 60).ToString("00") & "." & hundredths.ToString("00")
    End Function
    Public Function Display(lap As BestLapRecord) As String
        Return Time(lap.Milliseconds) & " · " & RaceCatalog.Current.Car(lap.CarCode).Label & If(CustomCars.CarPack.IsCustom(lap.CarCode), "", " · " & If(lap.CarGrade = "", "Grade unspecified", CarGrades.GradeLabel(lap.CarGrade)))
    End Function
    ' Launcher summary: the best lap across cars and the world record for the selected car's class.
    Public Function Summary(context As InstallContext, trackId As String, carCode As String, Optional receipt As PackReceipt = Nothing) As String
        If Not Timed(trackId) OrElse Not Stock(trackId) AndAlso Not TrackPacks.IsLayout(trackId) Then Return ""
        Dim lines As New List(Of String)
        Dim circuit = RaceCatalog.Current.Track(trackId).Circuit
        Dim customCar = CustomCars.CarPack.IsCustom(carCode)
        Try
            If CustomCars.ExternalCars.Find(carCode) IsNot Nothing Then
                lines.Add("Best times are not recorded for External cars")
            Else
                Dim lap = Best(context, trackId, TrackVersion(context, trackId, receipt), customCar)
                lines.Add(If(circuit, "Best lap: ", "Best time: ") & If(lap Is Nothing, If(circuit, "no completed lap yet", "no completed run yet"), Display(lap)))
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException
            lines.Add("Best lap history unavailable; saved file kept")
        End Try
        lines.Add(WorldRecords.Describe(context, trackId, carCode, receipt))
        Return String.Join(Environment.NewLine, lines.Where(Function(line) line <> ""))
    End Function
End Module
