Public Class PracticeTimeHistory
    Public Property Schema As Integer = 1
    Public Property Records As New List(Of BestLapRecord)
End Class

' Keep personal-best improvements independently of the older per-track summary.
' Older launchers can still read their files; no missing laps are manufactured.
Public Module PracticeTimes
    Private Const MaximumRecords As Integer = 50000
    Public Function HistoryPath(context As InstallContext) As String
        Return IO.Path.Combine(context.UserRoot, "practice-time-history.json")
    End Function
    Private Function Identity(record As BestLapRecord) As String
        Return String.Join("|", record.LayoutId, record.PackVersion, record.CarCode, record.CarGrade,
                           record.CarConversionVersion, record.Milliseconds.ToString(), record.CompletedUtc.Ticks.ToString(), record.ExternalCarId, record.ExternalCarVersion)
    End Function
    Private Function Configuration(record As BestLapRecord) As String
        Return String.Join("|", BestLapStore.TimedLayout(record.LayoutId), record.PackVersion,
                           record.CarCode, record.CarGrade, record.CarConversionVersion, record.ExternalCarId, record.ExternalCarVersion)
    End Function
    Public Function Entries(context As InstallContext) As List(Of BestLapRecord)
        Dim history As New PracticeTimeHistory()
        If File.Exists(HistoryPath(context)) Then
            history = Files.ReadJson(Of PracticeTimeHistory)(HistoryPath(context))
            If history Is Nothing OrElse history.Schema <> 1 OrElse history.Records Is Nothing OrElse history.Records.Count > MaximumRecords OrElse
                history.Records.Any(Function(r) Not BestLapStore.Valid(r, r IsNot Nothing AndAlso CustomCars.CarPack.IsCustom(r.CarCode))) Then
                Throw New IOException("Practice time history is invalid. The saved file has been kept.")
            End If
        End If
        Dim records = history.Records.AsEnumerable()
        For Each customCar In {False, True}
            For Each legacy In {False, True}
                records = records.Concat(BestLapStore.Load(context, customCar, legacy).Records)
            Next
        Next
        Return records.GroupBy(AddressOf Identity).Select(Function(group) group.First()).ToList()
    End Function
    ' Everything the history and leaderboard windows list: the files above plus External car times. Only Entries is
    ' written back to practice-time-history.json, so External car records never reach the files older launchers read.
    Public Function AllEntries(context As InstallContext) As List(Of BestLapRecord)
        Return Entries(context).Concat(ExternalCarTimes.Load(context).Records).ToList()
    End Function
    Public Function RecordPersonalBest(context As InstallContext, lap As BestLapRecord) As Boolean
        If lap Is Nothing OrElse Not BestLapStore.Valid(lap, CustomCars.CarPack.IsCustom(lap.CarCode)) Then Throw New IOException("Invalid completed lap result.")
        Dim records = Entries(context)
        If records.Any(Function(r) BestLapStore.TimedLayout(r.LayoutId) = BestLapStore.TimedLayout(lap.LayoutId) AndAlso
            BestLapStore.CompatibleVersion(lap.LayoutId, r.PackVersion, lap.PackVersion) AndAlso r.CarCode = lap.CarCode AndAlso
            r.CarGrade = lap.CarGrade AndAlso r.CarConversionVersion = lap.CarConversionVersion AndAlso r.Milliseconds <= lap.Milliseconds) Then Return False
        If records.Count >= MaximumRecords Then Throw New IOException("Practice time history is full. The saved file has been kept.")
        records.Add(lap)
        Files.SaveJson(HistoryPath(context), New PracticeTimeHistory With {.Records = records})
        Return True
    End Function
    Public Function Filter(records As IEnumerable(Of BestLapRecord), Optional trackId As String = "", Optional carCode As String = "", Optional bestOnly As Boolean = False) As BestLapRecord()
        Dim selected = records.Where(Function(r) (String.IsNullOrEmpty(trackId) OrElse BestLapStore.TimedLayout(r.LayoutId) = BestLapStore.TimedLayout(trackId)) AndAlso
                                                 (String.IsNullOrEmpty(carCode) OrElse r.CarCode = carCode))
        If bestOnly Then
            Return selected.GroupBy(AddressOf Configuration).Select(Function(group) group.OrderBy(Function(r) r.Milliseconds).ThenBy(Function(r) r.CompletedUtc).First()).
                OrderBy(Function(r) BestLapStore.TimedLayout(r.LayoutId)).ThenBy(Function(r) r.PackVersion).ThenBy(Function(r) r.Milliseconds).ToArray()
        End If
        Return selected.OrderByDescending(Function(r) r.CompletedUtc).ToArray()
    End Function
    Public Function TrackName(trackId As String) As String
        If BestLapStore.TimedLayout(trackId) = "nordschleife-standard" Then Return "Nordschleife (all lighting)"
        Try
            Return RaceCatalog.Current.Track(trackId).Label
        Catch ex As IOException
            Return trackId
        End Try
    End Function
    Public Function CarName(carCode As String) As String
        Dim external = CustomCars.ExternalCars.Find(carCode)
        If external IsNot Nothing Then Return external.Name & " — External"
        Return If(RaceCatalog.Current.Cars.FirstOrDefault(Function(car) car.Code = carCode)?.Label, carCode)
    End Function
    ' A record's car: an External car by its pack, also after the pack was removed or another pack took its code.
    Public Function CarLabel(record As BestLapRecord) As String
        If record.ExternalCarId Is Nothing Then Return CarName(record.CarCode)
        Dim external = CustomCars.ExternalCars.Find(record.CarCode)
        Return If(external IsNot Nothing AndAlso external.Id = record.ExternalCarId, external.Name, record.ExternalCarId.Substring("external-car-".Length)) & " — External"
    End Function
End Module
