Imports System.Drawing
Imports System.Windows.Forms

' Read-only, nonexclusive input. Stop polling while another input dialog owns capture.
Public Class DrivingInputMonitor
    Inherits UserControl
    Private ReadOnly context As InstallContext
    Private ReadOnly devices As New ComboBox With {.Name = "LiveInputDevice", .DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill}
    Private ReadOnly refreshButton As New Button With {.Text = "Refresh", .AutoSize = True}
    Private ReadOnly stateLabel As New Label With {.AutoSize = True, .Dock = DockStyle.Fill}
    Private ReadOnly meters As InputAxisMeter() = Enumerable.Range(0, 8).Select(Function(i) New InputAxisMeter()).ToArray()
    Private ReadOnly polling As New System.Windows.Forms.Timer With {.Interval = 50}
    Private input As DrivingInput
    Private suspended As Boolean
    Private selectedId As String
    Public Sub New(value As InstallContext)
        context = value
        AutoSize = True : Dock = DockStyle.Top : Padding = New Padding(0, 8, 0, 8)
        Dim layout As New TableLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top, .ColumnCount = 2, .RowCount = 7}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim heading As New Label With {.Text = "Live device input", .AutoSize = True, .Font = New Font(SystemFonts.MessageBoxFont, FontStyle.Bold)}
        layout.Controls.Add(heading, 0, 0) : layout.SetColumnSpan(heading, 2)
        Dim picker As New TableLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1}
        picker.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100)) : picker.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        picker.Controls.Add(devices, 0, 0) : picker.Controls.Add(refreshButton, 1, 0)
        layout.Controls.Add(picker, 0, 1) : layout.SetColumnSpan(picker, 2)
        For i = 0 To meters.Length - 1
            meters(i).Dock = DockStyle.Fill
            layout.Controls.Add(meters(i), i Mod 2, 2 + i \ 2)
        Next
        layout.Controls.Add(stateLabel, 0, 6) : layout.SetColumnSpan(stateLabel, 2)
        Controls.Add(layout)
        AddHandler layout.SizeChanged, Sub() stateLabel.MaximumSize = New Size(Math.Max(100, layout.ClientSize.Width - 8), 0)
        devices.AccessibleName = "Live input device"
        AddHandler refreshButton.Click, Sub() RefreshDevices()
        AddHandler devices.SelectedIndexChanged, Sub() Poll()
        AddHandler polling.Tick, Sub() Poll()
    End Sub
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        If Not DesignMode Then ResumeMonitoring()
    End Sub
    Public Sub SuspendMonitoring()
        suspended = True : polling.Stop()
        selectedId = TryCast(devices.SelectedItem, DrivingInput.Device)?.Id
        input?.Dispose() : input = Nothing
    End Sub
    Public Sub ResumeMonitoring()
        If IsDisposed Then Return
        suspended = False : RefreshDevices()
    End Sub
    Private Sub RefreshDevices()
        If suspended OrElse IsDisposed Then Return
        polling.Stop()
        selectedId = If(TryCast(devices.SelectedItem, DrivingInput.Device)?.Id, selectedId)
        input?.Dispose() : input = Nothing
        devices.Items.Clear()
        Try
            input = New DrivingInput(context, Handle)
            devices.Items.AddRange(input.Devices.Cast(Of Object).ToArray())
            Dim previous = input.Devices.FirstOrDefault(Function(d) d.Id = selectedId)
            ' Prefer actual connected hardware over an empty Xbox slot on first opening.
            devices.SelectedItem = If(previous, input.Devices.FirstOrDefault(Function(d) input.Read(d).Connected <> 0))
            If devices.SelectedIndex < 0 AndAlso devices.Items.Count > 0 Then devices.SelectedIndex = 0
            Poll() : polling.Start()
        Catch ex As Exception
            input?.Dispose() : input = Nothing
            ShowSample(Nothing, Nothing)
            stateLabel.Text = "Live input unavailable: " & ex.Message
        End Try
    End Sub
    Private Sub Poll()
        If suspended OrElse input Is Nothing OrElse Not Visible Then Return
        Dim device = TryCast(devices.SelectedItem, DrivingInput.Device)
        Try
            ShowSample(device, If(device Is Nothing, Nothing, input.Read(device)))
        Catch ex As Exception
            polling.Stop() : input?.Dispose() : input = Nothing
            ShowSample(device, Nothing)
            stateLabel.Text = "Unable to read device. Reconnect it and choose Refresh."
        End Try
    End Sub
    Friend Sub ShowSample(device As DrivingInput.Device, sample As DrivingInput.Sample)
        Dim connected = device IsNot Nothing AndAlso sample.Connected <> 0
        Dim xbox = device?.Name = "win_xinput"
        Dim names = If(xbox, {"Left stick X", "Left stick Y", "Right stick X", "Right stick Y", "Left trigger", "Right trigger", "—", "—"},
                            {"X", "Y", "Z", "Rotation X", "Rotation Y", "Rotation Z", "Slider 1", "Slider 2"})
        For i = 0 To 7
            Dim available = connected AndAlso (sample.Axes And (1UI << i)) <> 0 AndAlso sample.Values IsNot Nothing AndAlso i < sample.Values.Length
            meters(i).SetReading(names(i), If(available, sample.Values(i), 0), available, xbox AndAlso i >= 4)
        Next
        Dim message = If(device Is Nothing, "Connect a controller, wheel or pedals, then choose Refresh.", "Disconnected — reconnect this device or choose Refresh.")
        If connected Then message = "Connected · Raw input, before calibration · " & PressedInputs(sample, xbox)
        If stateLabel.Text <> message Then stateLabel.Text = message
    End Sub
    Friend Shared Function PressedInputs(sample As DrivingInput.Sample, xbox As Boolean) As String
        Dim pressed As New List(Of String)
        Dim xboxNames = {"D-pad Up", "D-pad Down", "D-pad Left", "D-pad Right", "Start", "Back", "Left stick", "Right stick", "LB", "RB", "", "", "A", "B", "X", "Y"}
        If sample.Buttons IsNot Nothing Then
            For i = 0 To Math.Min(sample.Buttons.Length, 128) - 1
                If sample.Buttons(i) = 0 Then Continue For
                Dim name = If(xbox AndAlso i < xboxNames.Length, xboxNames(i), "Button " & (i + 1).ToString())
                If name <> "" Then pressed.Add(name)
            Next
        End If
        If sample.Pov IsNot Nothing Then
            Dim directions = {"Up", "Up-right", "Right", "Down-right", "Down", "Down-left", "Left", "Up-left"}
            For i = 0 To Math.Min(sample.Pov.Length, 4) - 1
                If sample.Pov(i) < 36000UI Then pressed.Add("POV " & (i + 1).ToString() & " " & directions(CInt(Math.Floor((sample.Pov(i) + 2250.0) / 4500)) Mod 8))
            Next
        End If
        Return If(pressed.Count = 0, "No buttons pressed", String.Join(" · ", pressed))
    End Function
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            polling.Stop() : polling.Dispose() : input?.Dispose() : input = Nothing
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class

Public Class InputAxisMeter
    Inherits Control
    Private caption As String = "—"
    Private percent As Integer
    Private available As Boolean
    Private trigger As Boolean
    Public Sub New()
        DoubleBuffered = True : Height = 28 : Margin = New Padding(3, 1, 8, 1)
        SetStyle(ControlStyles.ResizeRedraw, True)
    End Sub
    Public Sub SetReading(name As String, value As Single, present As Boolean, unipolar As Boolean)
        present = present AndAlso Single.IsFinite(value)
        Dim nextPercent = If(present, CInt(Math.Round(Math.Clamp(value, If(unipolar, 0.0F, -1.0F), 1.0F) * 100)), 0)
        If Not present Then nextPercent = 0
        If caption = name AndAlso percent = nextPercent AndAlso available = present AndAlso trigger = unipolar Then Return
        caption = name : percent = nextPercent : available = present : trigger = unipolar
        AccessibleName = name : AccessibleDescription = If(present, percent.ToString() & "%", "Unavailable")
        Invalidate()
    End Sub
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim text = caption & "  " & If(available, percent.ToString("+0;-0;0") & "%", "—")
        TextRenderer.DrawText(e.Graphics, text, Font, New Rectangle(0, 0, Width, Height - 7), ForeColor, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
        Dim track As New Rectangle(1, Height - 6, Math.Max(1, Width - 2), 4)
        Using brush As New SolidBrush(SystemColors.GrayText)
            e.Graphics.FillRectangle(brush, track)
        End Using
        If Not available Then Return
        Dim origin = If(trigger, 0, track.Width \ 2)
        Dim target = CInt(track.Width * If(trigger, percent / 100.0, (percent + 100) / 200.0))
        Dim ink = If(SystemInformation.HighContrast, SystemColors.Highlight, If(BackColor.GetBrightness() < 0.5F, Color.FromArgb(150, 230, 110), Color.FromArgb(30, 110, 25)))
        Using brush As New SolidBrush(ink)
            e.Graphics.FillRectangle(brush, track.X + Math.Min(origin, target), track.Y, Math.Max(2, Math.Abs(target - origin)), track.Height)
        End Using
    End Sub
End Class
