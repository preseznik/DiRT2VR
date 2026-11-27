Imports System.Text.RegularExpressions
Imports DiRT2VR.CustomTracks

Public Class ExternalCarTimeHistory
    Public Property Schema As Integer = 1
    Public Property Records As New List(Of BestLapRecord)
End Class

' Practice times driven with External cars. Each record names the car pack and its version, which the stock and
' CUSTOM files cannot hold, so they have a file of their own: older launchers validate every record in the files
' they read and never open this one. Every personal-best improvement is kept, per track version, car pack version
' and grade.
Public Module ExternalCarTimes
    Private Const MaximumRecords As Integer = 20000
    Public Function HistoryPath(context As InstallContext) As String
        Return IO.Path.Combine(context.UserRoot, "external-car-lap-records.json")
    End Function
    Public Function Valid(record As BestLapRecord) As Boolean
        Return record IsNot Nothing AndAlso record.ExternalCarId IsNot Nothing AndAlso
            Regex.IsMatch(record.ExternalCarId, "\Aexternal-car-[a-z0-9][a-z0-9-]{0,47}\z") AndAlso SafeFiles.Version(record.ExternalCarVersion) AndAlso
            record.CarCode IsNot Nothing AndAlso Regex.IsMatch(record.CarCode, "\A[a-z0-9]{3}\z") AndAlso record.CarConversionVersion = "" AndAlso
            BestLapStore.ValidTrackAndTime(record)
    End Function
    Public Function Load(context As InstallContext) As ExternalCarTimeHistory
        Dim filename = HistoryPath(context)
        If Not File.Exists(filename) Then Return New ExternalCarTimeHistory()
        Dim history = Files.ReadJson(Of ExternalCarTimeHistory)(filename)
        If history Is Nothing OrElse history.Schema <> 1 OrElse history.Records Is Nothing OrElse history.Records.Count > MaximumRecords OrElse
            history.Records.Any(Function(r) Not Valid(r)) Then
            Throw New IOException("External car times are invalid. The saved file has been kept.")
        End If
        Return history
    End Function
    Private Function SameRun(a As BestLapRecord, b As BestLapRecord) As Boolean
        Return BestLapStore.TimedLayout(a.LayoutId) = BestLapStore.TimedLayout(b.LayoutId) AndAlso BestLapStore.CompatibleVersion(b.LayoutId, a.PackVersion, b.PackVersion) AndAlso
            a.ExternalCarId = b.ExternalCarId AndAlso a.ExternalCarVersion = b.ExternalCarVersion
    End Function
    ' Saves a personal best for this track version, car pack version and grade; True when it was one.
    Public Function Record(context As InstallContext, lap As BestLapRecord) As Boolean
        If Not Valid(lap) Then Throw New IOException("Invalid completed lap result.")
        Dim history = Load(context)
        If history.Records.Any(Function(r) SameRun(r, lap) AndAlso r.CarGrade = lap.CarGrade AndAlso r.Milliseconds <= lap.Milliseconds) Then Return False
        If history.Records.Count >= MaximumRecords Then Throw New IOException("External car times are full. The saved file has been kept.")
        history.Records.Add(lap)
        Files.SaveJson(HistoryPath(context), history)
        Return True
    End Function
    ' The fastest time with this car pack version on this track version, across grades.
    Public Function Best(context As InstallContext, layoutId As String, version As String, carId As String, carVersion As String) As BestLapRecord
        Dim probe As New BestLapRecord With {.LayoutId = layoutId, .PackVersion = version, .ExternalCarId = carId, .ExternalCarVersion = carVersion}
        Return Load(context).Records.Where(Function(r) SameRun(r, probe)).OrderBy(Function(r) r.Milliseconds).ThenBy(Function(r) r.CompletedUtc).FirstOrDefault()
    End Function
End Module
