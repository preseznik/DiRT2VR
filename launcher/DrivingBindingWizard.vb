Imports System.Drawing
Imports System.Windows.Forms

Public Class DrivingBindingWizard
    Inherits Form
    Private ReadOnly context As InstallContext
    Private ReadOnly actions As String()
    Private ReadOnly answers As DrivingBinding()
    Private ReadOnly heading As New Label With {.AutoSize = True, .Font = New Font(SystemFonts.MessageBoxFont.FontFamily, 16, FontStyle.Bold)}
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
        panel.Controls.AddRange({heading, source, selection, instruction, status, travel, review, buttons,
            New Label With {.AutoSize = True, .MaximumSize = New Size(650, 0), .Text = "Skip leaves the current assignment unchanged. Back revisits a step. Cancel discards this wizard. Review and Apply at the end, then Save driving controls. Controller input still reaches other applications."}})
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
        devices.Items.Clear() : devices.Items.Add("All connected devices (automatic)")
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
            heading.Text = "Review your bindings"
            instruction.Text = "Nothing has been saved yet. Check the assignments below."
            review.Items.Clear()
            For i = 0 To actions.Length - 1
                Dim row As New ListViewItem(actions(i))
                row.SubItems.Add(If(answers(i) Is Nothing, "Unchanged (skipped)", DrivingInput.Description(answers(i)))) : review.Items.Add(row)
            Next
            status.Text = "Apply returns these changes to the controls editor. Save there to use them on the next launch."
            Return
        End If
        heading.Text = $"{index + 1} / {actions.Length} — {actions(index)}"
        instruction.Text = If(source.SelectedIndex = 1, "Press and release the key you want to assign. Escape cancels; Tab moves focus.",
            "First center the wheel / stick, release pedals and handbrake, and leave the shifter in neutral. Wait for ‘Ready’, then " & Gesture(actions(index)) & ". Return to rest to continue automatically.")
        status.Text = If(source.SelectedIndex = 1, "Ready for a key.", "Learning the resting positions… Nonzero resting axes and held switches are ignored.")
    End Sub
    Private Shared Function Gesture(action As String) As String
        Select Case action
            Case "Steer Left" : Return "turn left and hold briefly"
            Case "Steer Right" : Return "turn right and hold briefly"
            Case "Accelerate" : Return "press the accelerator fully"
            Case "Brake" : Return "press the brake fully"
            Case "Clutch" : Return "press the clutch fully"
            Case "Hand Brake" : Return "pull the handbrake fully (or hold its button)"
            Case Else : Return "press the button or select the gear and hold briefly"
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
        If native Is Nothing Then status.Text = "That key is not supported. Try another key." : Return True
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
            status.Text = "No controllers connected. Connect a device and Refresh, or choose Keyboard." : Return
        End If
        Dim selected = TryCast(devices.SelectedItem, DrivingInput.Device), ready As Integer, connected As Integer
        For Each device In input.Devices
            If selected IsNot Nothing AndAlso selected.Id <> device.Id Then Continue For
            If Not captures.ContainsKey(device.Id) Then captures(device.Id) = New DrivingCapture(device, actions(index)) With {.AxesOnly = filter.SelectedIndex = 1, .ButtonsOnly = filter.SelectedIndex = 2}
            Dim capture = captures(device.Id), sample = input.Read(device)
            capture.Update(sample, now)
            If sample.Connected <> 0 Then connected += 1
            If active Is capture AndAlso sample.Connected = 0 Then
                active = Nothing : status.Text = "Device disconnected. Capture cancelled; reconnect and try again." : Return
            End If
            If capture.Ready Then ready += 1
            If active Is Nothing AndAlso capture.WaitingForRelease Then active = capture
        Next
        If active IsNot Nothing Then
            travel.Value = active.Travel
            status.Text = DrivingInput.Description(active.Detected) & " — release / return to rest to continue."
            If active.Completed IsNot Nothing Then CompleteStep(active.Completed)
        Else
            status.Text = If(ready > 0, "Ready — " & Gesture(actions(index)) & ". Resting inputs will not be assigned.",
                If(connected = 0, "Disconnected. Reconnect and Refresh devices.", "Keep controls at rest briefly. Center Xbox sticks and release triggers."))
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
