Imports System.Drawing
Imports System.Text.Json
Imports System.Windows.Forms

Public Class PracticeTimesForm
    Inherits LauncherForm
    Private ReadOnly context As InstallContext
    Private ReadOnly selectedTrack As String
    Private ReadOnly initialCar As String
    Private ReadOnly trackFilter As New ComboBox With {.Name = "RecordTrackFilter", .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 300}
    Private ReadOnly carFilter As New ComboBox With {.Name = "RecordCarFilter", .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 320}
    Private ReadOnly rows As New ListView With {.Name = "PracticeTimeRows", .View = View.Details, .FullRowSelect = True, .HideSelection = False, .MultiSelect = False, .Dock = DockStyle.Fill}
    Private ReadOnly status As New Label With {.Name = "PracticeTimeStatus", .AutoSize = True, .Dock = DockStyle.Bottom, .Padding = New Padding(0, 8, 0, 8)}
    Private records As New List(Of BestLapRecord)
    Private loading As Boolean
    Private Class Choice
        Public ReadOnly Id As String
        Private ReadOnly label As String
        Public Sub New(id As String, label As String)
            Me.Id = id : Me.label = label
        End Sub
        Public Overrides Function ToString() As String
            Return label
        End Function
    End Class
    Public Sub New(context As InstallContext, Optional trackId As String = "", Optional carCode As String = "")
        Me.context = context : selectedTrack = If(trackId, "") : initialCar = If(carCode, "")
        Text = If(selectedTrack = "", "Leaderboards", "Best times — " & PracticeTimes.TrackName(selectedTrack))
        Name = "PracticeTimesForm" : Font = New Font("Segoe UI", 10) : AutoScaleMode = AutoScaleMode.Dpi
        Size = New Size(1100, 560) : MinimumSize = New Size(760, 380) : Padding = New Padding(16)
        StartPosition = FormStartPosition.CenterParent : ShowInTaskbar = False
        Dim filters As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .WrapContents = True, .Padding = New Padding(0, 0, 0, 12)}
        trackFilter.AccessibleName = "Track filter" : carFilter.AccessibleName = "Car filter"
        trackFilter.DropDownWidth = 500 : carFilter.DropDownWidth = 500
        If selectedTrack = "" Then
            filters.Controls.Add(New Label With {.Text = "Track", .AutoSize = True, .Margin = New Padding(0, 8, 6, 0)})
        End If
        trackFilter.Visible = selectedTrack = "" : filters.Controls.Add(trackFilter)
        filters.Controls.Add(New Label With {.Text = "Car", .AutoSize = True, .Margin = New Padding(10, 8, 6, 0)})
        filters.Controls.Add(carFilter)
        rows.Columns.Add("Time", 95) : rows.Columns.Add("Car", 240) : rows.Columns.Add("Grade", 120)
        If selectedTrack = "" Then rows.Columns.Add("Track", 240)
        rows.Columns.Add("Recorded", 150) : rows.Columns.Add("Track version", 105) : rows.Columns.Add("Car version", 100)
        Dim actions As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .FlowDirection = FlowDirection.RightToLeft}
        Dim close As New Button With {.Text = "Close", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        Dim refresh As New Button With {.Text = "Refresh", .Name = "RefreshPracticeTimes", .AutoSize = True}
        actions.Controls.AddRange({close, refresh}) : CancelButton = close
        Controls.Add(rows) : Controls.Add(filters) : Controls.Add(status) : Controls.Add(actions)
        AddHandler refresh.Click, Sub() Reload()
        AddHandler trackFilter.SelectedIndexChanged, Sub() RenderRows()
        AddHandler carFilter.SelectedIndexChanged, Sub() RenderRows()
        Reload()
    End Sub
    Private Sub Reload()
        Dim track = If(TryCast(trackFilter.SelectedItem, Choice)?.Id, BestLapStore.TimedLayout(selectedTrack))
        Dim car = If(TryCast(carFilter.SelectedItem, Choice)?.Id, initialCar)
        loading = True
        Try
            records = PracticeTimes.Entries(context)
            trackFilter.Items.Clear() : trackFilter.Items.Add(New Choice("", "All tracks"))
            For Each id In records.Select(Function(r) BestLapStore.TimedLayout(r.LayoutId)).Concat({BestLapStore.TimedLayout(selectedTrack)}).
                Where(Function(value) value <> "").Distinct().OrderBy(Function(value) PracticeTimes.TrackName(value))
                trackFilter.Items.Add(New Choice(id, PracticeTimes.TrackName(id)))
            Next
            carFilter.Items.Clear() : carFilter.Items.Add(New Choice("", "All cars"))
            For Each code In records.Select(Function(r) r.CarCode).Concat({initialCar}).Where(Function(value) value <> "").Distinct().OrderBy(Function(value) PracticeTimes.CarName(value))
                carFilter.Items.Add(New Choice(code, PracticeTimes.CarName(code)))
            Next
            trackFilter.SelectedItem = trackFilter.Items.Cast(Of Choice)().FirstOrDefault(Function(item) item.Id = track)
            carFilter.SelectedItem = carFilter.Items.Cast(Of Choice)().FirstOrDefault(Function(item) item.Id = car)
            If trackFilter.SelectedIndex < 0 Then trackFilter.SelectedIndex = 0
            If carFilter.SelectedIndex < 0 Then carFilter.SelectedIndex = 0
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException
            records.Clear() : rows.Items.Clear() : status.Text = "Times unavailable. " & ex.Message
            Return
        Finally
            loading = False
        End Try
        RenderRows()
    End Sub
    Private Sub RenderRows()
        If loading Then Return
        Dim track = If(selectedTrack <> "", selectedTrack, If(TryCast(trackFilter.SelectedItem, Choice)?.Id, ""))
        Dim car = If(TryCast(carFilter.SelectedItem, Choice)?.Id, "")
        Dim visible = PracticeTimes.Filter(records, track, car, bestOnly:=selectedTrack = "")
        rows.BeginUpdate()
        Try
            rows.Items.Clear()
            For Each lap As BestLapRecord In visible
                Dim cells As New List(Of String) From {BestLapStore.Time(lap.Milliseconds), PracticeTimes.CarName(lap.CarCode),
                    If(lap.CarGrade = "", "Unspecified", CarGrades.GradeLabel(lap.CarGrade))}
                If selectedTrack = "" Then cells.Add(PracticeTimes.TrackName(lap.LayoutId))
                cells.Add(lap.CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                cells.Add(If(lap.PackVersion = "", "Stock", lap.PackVersion))
                cells.Add(If(lap.CarConversionVersion = "", "Stock", lap.CarConversionVersion))
                rows.Items.Add(New ListViewItem(cells.ToArray()) With {.Tag = lap})
            Next
        Finally
            rows.EndUpdate()
        End Try
        status.Text = If(visible.Length = 0, "No saved results match these filters. ", visible.Length & " saved result(s). ") &
            If(selectedTrack = "", "Best per car, grade and track version, saved on this PC.", "Personal-best improvement history saved on this PC.")
    End Sub
End Class
