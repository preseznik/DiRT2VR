Imports System.Drawing
Imports System.Windows.Forms

Public Class DrivingBindingWizard
    Inherits Form
    Private ReadOnly context As InstallContext
    Private ReadOnly actions As String()
    Private ReadOnly answers As DrivingBinding()
    Private ReadOnly stepLabel As New Label With {.AutoSize = True}
    Private ReadOnly heading As New Label With {.AutoSize = True, .MaximumSize = New Size(540, 0), .Font = New Font(SystemFonts.MessageBoxFont.FontFamily, 30, FontStyle.Bold), .Anchor = AnchorStyles.Left}
    Private ReadOnly actionIcon As New DrivingActionIcon With {.Size = New Size(80, 80), .Margin = New Padding(0, 0, 12, 8)}
    Private ReadOnly instruction As New Label With {.AutoSize = True, .MaximumSize = New Size(650, 0)}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(650, 0)}
    Private ReadOnly source As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 290}
    Private ReadOnly devices As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 370}
    Private ReadOnly filter As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 200}
    Private ReadOnly travel As New ProgressBar With {.Width = 650, .Height = 22, .AccessibleName = "Detected input travel"}
    Private ReadOnly review As New ListView With {.View = View.Details, .FullRowSelect = True, .Width = 650, .Height = 260, .Visible = False}
    Private ReadOnly back As New Button With {.Text = "Back", .AutoSize = True}
    Private ReadOnly skip As New Button With {.Text = "Skip / keep current", .AutoSize = True}
    Private ReadOnly retry As New Button With {.Text = "Try again", .AutoSize = True}
    Private ReadOnly apply As New Button With {.Text = "Apply bindings", .AutoSize = True, .Visible = False}
    Private ReadOnly timer As New System.Windows.Forms.Timer With {.Interval = 40}
    Private ReadOnly captures As New Dictionary(Of String, DrivingCapture)
    Private input As DrivingInput
    Private active As DrivingCapture
    Private index As Integer
    Private key As Keys = Keys.None
    Private keyReleased As Long = -1
    Private pendingKey As DrivingBinding
    Private updating As Boolean
    <DllImport("user32.dll")> Private Shared Function GetAsyncKeyState(vKey As Integer) As Short
    End Function
    Public ReadOnly Property Bindings As List(Of DrivingBinding)
        Get
            Return answers.Where(Function(b) b IsNot Nothing).ToList()
        End Get
    End Property
    Public Sub New(value As InstallContext, Optional onlyAction As String = Nothing, Optional keyboard As Boolean = False)
        context = value : actions = If(onlyAction Is Nothing, DrivingControls.Actions.ToArray(), {onlyAction})
        answers = New DrivingBinding(actions.Length - 1) {}
        Text = If(onlyAction Is Nothing, "DiRT2VR — Binding wizard", "Bind " & onlyAction)
        ClientSize = New Size(710, 630) : MinimumSize = New Size(710, 630)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False : MaximizeBox = False
        Dim panel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoScroll = True, .Padding = New Padding(20)}
        source.Items.AddRange({"Controller / wheel / pedals", "Keyboard"}) : source.SelectedIndex = If(keyboard, 1, 0)
        filter.Items.AddRange({"Automatic input", "Axis only", "Button only"}) : filter.SelectedIndex = 0
        review.Columns.Add("Action", 150) : review.Columns.Add("Assignment", 470)
        Dim selection As New FlowLayoutPanel With {.AutoSize = True, .Width = 660}
        Dim refresh As New Button With {.Text = "Refresh devices", .AutoSize = True}
        selection.Controls.AddRange({devices, filter, refresh})
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Width = 660}
        Dim cancel As New Button With {.Text = "Cancel", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        buttons.Controls.AddRange({back, skip, retry, apply, cancel}) : CancelButton = cancel
        Dim title As New TableLayoutPanel With {.AutoSize = True, .ColumnCount = 2, .RowCount = 1, .Width = 660}
        title.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize)) : title.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        title.Controls.Add(actionIcon, 0, 0) : title.Controls.Add(heading, 1, 0)
        panel.Controls.AddRange({stepLabel, title, source, selection, instruction, status, travel, review, buttons,
            New Label With {.AutoSize = True, .MaximumSize = New Size(650, 0), .Text = "Skip keeps your current binding. Changes are reviewed before saving."}})
        Controls.Add(panel)
        AddHandler Shown, Sub() RefreshDevices()
        AddHandler refresh.Click, Sub() RefreshDevices()
        AddHandler source.SelectedIndexChanged, Sub() BeginStep()
        AddHandler devices.SelectedIndexChanged, Sub() If Not updating Then BeginStep()
        AddHandler filter.SelectedIndexChanged, Sub() BeginStep()
        AddHandler retry.Click, Sub() BeginStep()
        AddHandler back.Click, Sub()
                                   If index > 0 Then index -= 1 : BeginStep()
                               End Sub
        AddHandler skip.Click, Sub() CompleteStep(Nothing)
        AddHandler apply.Click, Sub()
                                    DialogResult = DialogResult.OK : Close()
                                End Sub
        AddHandler timer.Tick, AddressOf Poll
        BeginStep()
    End Sub
    Private Sub RefreshDevices()
        timer.Stop() : input?.Dispose() : input = Nothing : updating = True
        Dim selected = TryCast(devices.SelectedItem, DrivingInput.Device)?.Id
        devices.Items.Clear() : devices.Items.Add("All connected devices")
        Try
            input = New DrivingInput(context, Handle)
            For Each device In input.Devices
                devices.Items.Add(device)
            Next
            devices.SelectedIndex = 0
            For i = 1 To devices.Items.Count - 1
                If DirectCast(devices.Items(i), DrivingInput.Device).Id = selected Then devices.SelectedIndex = i
            Next
        Catch ex As Exception
            status.Text = ex.Message
        Finally
            updating = False : BeginStep() : timer.Start()
        End Try
    End Sub
    Private Sub BeginStep()
        captures.Clear() : active = Nothing : key = Keys.None : pendingKey = Nothing : keyReleased = -1 : travel.Value = 0
        Dim finished = index >= actions.Length
        review.Visible = finished : apply.Visible = finished : skip.Visible = Not finished : retry.Visible = Not finished
        source.Enabled = Not finished : devices.Enabled = Not finished AndAlso source.SelectedIndex = 0 : filter.Enabled = devices.Enabled
        back.Enabled = index > 0
        If finished Then
            stepLabel.Text = "Final step" : actionIcon.Visible = False
            heading.Text = "REVIEW BINDINGS"
            instruction.Text = "Check your assignments."
            review.Items.Clear()
            For i = 0 To actions.Length - 1
                Dim row As New ListViewItem(actions(i))
                row.SubItems.Add(If(answers(i) Is Nothing, "Unchanged (skipped)", DrivingInput.Description(answers(i)))) : review.Items.Add(row)
            Next
            status.Text = "Apply, then Save in the controls editor."
            Return
        End If
        stepLabel.Text = $"Step {index + 1} of {actions.Length}"
        heading.Text = actions(index).ToUpperInvariant()
        actionIcon.Visible = True : actionIcon.SetAction(actions(index))
        instruction.Text = If(source.SelectedIndex = 1, "Press and release a key.", "Center steering. Release pedals and handbrake. Shifter in neutral.")
        status.Text = If(source.SelectedIndex = 1, "Ready", "Keep still for a moment…")
    End Sub
    Private Shared Function Gesture(action As String) As String
        Select Case action
            Case "Steer Left" : Return "turn left and hold briefly"
            Case "Steer Right" : Return "turn right and hold briefly"
            Case "Accelerate" : Return "press the accelerator fully"
            Case "Brake" : Return "press the brake fully"
            Case "Clutch" : Return "press the clutch fully"
            Case "Hand Brake" : Return "pull the handbrake or press its button"
            Case Else : Return "press a button or select the gear"
        End Select
    End Function
    Private Sub CompleteStep(binding As DrivingBinding)
        answers(index) = binding : index += 1 : BeginStep()
    End Sub
    Protected Overrides Function ProcessCmdKey(ByRef message As Message, keyData As Keys) As Boolean
        If index >= actions.Length OrElse source.SelectedIndex <> 1 Then Return MyBase.ProcessCmdKey(message, keyData)
        Dim pressed = keyData And Keys.KeyCode
        If pressed = Keys.Escape OrElse pressed = Keys.Tab Then Return MyBase.ProcessCmdKey(message, keyData)
        If key <> Keys.None Then Return True
        Dim mapped = If(pressed = Keys.ShiftKey, Keys.LShiftKey, If(pressed = Keys.ControlKey, Keys.LControlKey, pressed))
        Dim native = DrivingControls.KeyInput(mapped)
        If native Is Nothing Then status.Text = "Unsupported key. Try another." : Return True
        key = pressed : keyReleased = -1
        pendingKey = New DrivingBinding With {.Action = actions(index), .DeviceId = "Keyboard", .Device = "Keyboard", .Input = native}
        status.Text = DrivingInput.Description(pendingKey) & " — release to continue." : Return True
    End Function
    Private Sub Poll(sender As Object, e As EventArgs)
        If index >= actions.Length OrElse Not ContainsFocus Then Return
        Dim now = Environment.TickCount64
        If source.SelectedIndex = 1 Then
            If key = Keys.None Then Return
            If GetAsyncKeyState(CInt(key)) < 0 Then keyReleased = -1 : Return
            If keyReleased < 0 Then keyReleased = now
            If now - keyReleased >= 160 Then CompleteStep(pendingKey)
            Return
        End If
        If input Is Nothing OrElse input.Devices.Count = 0 Then
            status.Text = "Connect a controller and Refresh, or choose Keyboard." : Return
        End If
        Dim selected = TryCast(devices.SelectedItem, DrivingInput.Device), ready As Integer, connected As Integer
        For Each device In input.Devices
            If selected IsNot Nothing AndAlso selected.Id <> device.Id Then Continue For
            If Not captures.ContainsKey(device.Id) Then captures(device.Id) = New DrivingCapture(device, actions(index)) With {.AxesOnly = filter.SelectedIndex = 1, .ButtonsOnly = filter.SelectedIndex = 2}
            Dim capture = captures(device.Id), sample = input.Read(device)
            capture.Update(sample, now)
            If sample.Connected <> 0 Then connected += 1
            If active Is capture AndAlso sample.Connected = 0 Then
                active = Nothing : status.Text = "Disconnected. Reconnect and try again." : Return
            End If
            If capture.Ready Then ready += 1
            If active Is Nothing AndAlso capture.WaitingForRelease Then active = capture
        Next
        If active IsNot Nothing Then
            travel.Value = active.Travel
            instruction.Text = "Release / return to rest to continue."
            status.Text = DrivingInput.Description(active.Detected)
            If active.Completed IsNot Nothing Then CompleteStep(active.Completed)
        Else
            instruction.Text = If(ready > 0, Gesture(actions(index)) & ".", "Center steering. Release pedals and handbrake. Shifter in neutral.")
            status.Text = If(ready > 0, "Ready", If(connected = 0, "Disconnected. Reconnect and Refresh.", "Keep still for a moment…"))
        End If
    End Sub
    Protected Overrides Sub OnDeactivate(e As EventArgs)
        MyBase.OnDeactivate(e)
        If index < actions.Length Then BeginStep()
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then timer.Dispose() : input?.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

' Simple vector symbols remain sharp at high DPI and follow the Windows text color.
Friend Class DrivingActionIcon
    Inherits Control
    Private action As String = ""
    Public Sub New()
        DoubleBuffered = True : AccessibleRole = AccessibleRole.Graphic
    End Sub
    Public Sub SetAction(value As String)
        action = value : AccessibleName = value : Invalidate()
    End Sub
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        g.ScaleTransform(ClientSize.Width / 80.0F, ClientSize.Height / 80.0F)
        Using pen As New Pen(ForeColor, 3.5F), brush As New SolidBrush(ForeColor)
            pen.StartCap = Drawing2D.LineCap.Round : pen.EndCap = Drawing2D.LineCap.Round
            Select Case action
                Case "Steer Left", "Steer Right"
                    g.DrawEllipse(pen, 17, 23, 46, 46) : g.DrawEllipse(pen, 35, 41, 10, 10)
                    g.DrawLine(pen, 18, 39, 35, 45) : g.DrawLine(pen, 62, 39, 45, 45) : g.DrawLine(pen, 40, 51, 40, 68)
                    Dim left = action = "Steer Left", tip = If(left, 13, 67), tail = If(left, 62, 18), wing = If(left, 24, 56)
                    g.DrawLine(pen, tail, 12, tip, 12) : g.DrawLine(pen, tip, 12, wing, 5) : g.DrawLine(pen, tip, 12, wing, 19)
                Case "Accelerate", "Brake", "Clutch"
                    Dim selected = If(action = "Clutch", 0, If(action = "Brake", 1, 2))
                    For i = 0 To 2
                        Dim box As New Rectangle(9 + i * 24, 16, 14, 35)
                        If i = selected Then g.FillRectangle(brush, box) Else g.DrawRectangle(pen, box)
                        g.DrawLine(pen, box.X + 7, 53, box.X + 7, 65)
                    Next
                Case "Hand Brake"
                    g.DrawLine(pen, 16, 66, 64, 66) : g.DrawEllipse(pen, 23, 52, 14, 14)
                    g.DrawLine(pen, 30, 55, 53, 21) : g.DrawLine(pen, 49, 17, 61, 25)
                Case "Gear Up", "Gear Down"
                    Dim tip = If(action = "Gear Up", 13, 65), tail = If(action = "Gear Up", 65, 13), wing = If(action = "Gear Up", 28, 50)
                    g.DrawLine(pen, 40, tail, 40, tip) : g.DrawLine(pen, 25, wing, 40, tip) : g.DrawLine(pen, 55, wing, 40, tip)
                Case "Change View"
                    g.DrawRectangle(pen, 12, 24, 56, 38) : g.DrawEllipse(pen, 30, 32, 20, 20) : g.DrawRectangle(pen, 22, 16, 20, 8)
                Case "Look Back"
                    g.DrawArc(pen, 20, 18, 44, 42, -90, 270) : g.DrawLine(pen, 20, 39, 12, 29) : g.DrawLine(pen, 20, 39, 30, 31)
                Case "Horn"
                    g.DrawPolygon(pen, {New Point(15, 31), New Point(28, 31), New Point(42, 20), New Point(42, 60), New Point(28, 49), New Point(15, 49)})
                    g.DrawArc(pen, 40, 25, 20, 30, -65, 130) : g.DrawArc(pen, 45, 17, 28, 46, -65, 130)
                Case Else
                    g.DrawLine(pen, 16, 40, 64, 40)
                    For Each x In {16, 40, 64}
                        g.DrawLine(pen, x, 18, x, 62)
                    Next
            End Select
        End Using
    End Sub
End Class
