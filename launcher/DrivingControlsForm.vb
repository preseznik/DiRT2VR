Imports System.Drawing
Imports System.Windows.Forms

Public Class DrivingControlsForm
    Inherits Form
    Private ReadOnly context As InstallContext
    Private ReadOnly settings As DrivingControls
    Private ReadOnly enabledBox As New CheckBox With {.Text = "Use launcher driving bindings (all launcher modes, DX11)", .AutoSize = True}
    Private ReadOnly list As New ListView With {.View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .Dock = DockStyle.Fill}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(820, 0)}
    Private keyAction As String
    Public Sub New(value As InstallContext)
        context = value : settings = DrivingControls.Load(context)
        Text = "DiRT2VR — Driving controls" : Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(900, 720) : MinimumSize = New Size(720, 560)
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5, .Padding = New Padding(16)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each sizing As SizeType In {SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize}
            layout.RowStyles.Add(New RowStyle(sizing, If(sizing = SizeType.Percent, 100, 0)))
        Next
        enabledBox.Checked = settings.Enabled : layout.Controls.Add(enabledBox)
        layout.Controls.Add(New Label With {.Text = "Unassigned actions use the game's saved controls. An assigned action replaces its saved bindings: assign both keyboard and controller inputs if you want both. Steering, pedals and shifters can use different devices.", .AutoSize = True, .MaximumSize = New Size(820, 0), .Margin = New Padding(0, 10, 0, 10)})
        list.Columns.Add("Action", 160) : list.Columns.Add("Keyboard", 200) : list.Columns.Add("Controller / wheel / pedals", 470)
        list.AccessibleName = "Driving bindings" : layout.Controls.Add(list)
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        AddButton(buttons, "Bind keyboard…", Sub()
                                                   keyAction = SelectedAction()
                                                   status.Text = If(keyAction Is Nothing, "Select an action first.", "Press a single key for " & keyAction & ". Escape cancels.")
                                               End Sub)
        AddButton(buttons, "Bind device…", AddressOf BindDevice)
        AddButton(buttons, "Calibration…", AddressOf Calibrate)
        AddButton(buttons, "Use game binding", Sub()
                                                     Dim action = SelectedAction()
                                                     If action Is Nothing Then Return
                                                     settings.Bindings.RemoveAll(Function(b) b.Action = action)
                                                     RefreshRows(action)
                                                 End Sub)
        AddButton(buttons, "Save driving controls", Sub()
                                                          Try
                                                              context.RequireClosed()
                                                              settings.Enabled = enabledBox.Checked
                                                              settings.Save(context)
                                                              DialogResult = DialogResult.OK : Close()
                                                          Catch ex As Exception
                                                              status.Text = ex.Message
                                                          End Try
                                                      End Sub)
        AddButton(buttons, "Cancel", Sub() Close())
        layout.Controls.Add(buttons) : layout.Controls.Add(status) : Controls.Add(layout)
        status.Text = "H-pattern and clutch bindings still require the appropriate transmission/assist settings in the game. Save to apply on the next launch. Turning overrides off does not undo controls already saved by the game."
        RefreshRows()
    End Sub
    Private Shared Sub AddButton(panel As FlowLayoutPanel, text As String, action As Action)
        Dim button As New Button With {.Text = text, .AutoSize = True}
        AddHandler button.Click, Sub() action()
        panel.Controls.Add(button)
    End Sub
    Private Function SelectedAction() As String
        Return If(list.SelectedItems.Count = 1, list.SelectedItems(0).Text, Nothing)
    End Function
    Private Sub RefreshRows(Optional selected As String = Nothing)
        list.BeginUpdate() : list.Items.Clear()
        For Each action In DrivingControls.Actions
            Dim bindings = settings.Bindings.Where(Function(b) b.Action = action).ToArray()
            Dim keyboard = bindings.FirstOrDefault(Function(b) b.Keyboard), device = bindings.FirstOrDefault(Function(b) Not b.Keyboard)
            Dim row As New ListViewItem(action)
            row.SubItems.Add(If(keyboard Is Nothing, "—", keyboard.Input.Replace("win_key_", "")))
            row.SubItems.Add(If(device Is Nothing, "—", device.Device & " / " & device.Input.Replace("win_con_", "")))
            list.Items.Add(row) : row.Selected = action = selected
        Next
        list.EndUpdate()
    End Sub
    Protected Overrides Function ProcessCmdKey(ByRef message As Message, keyData As Keys) As Boolean
        If keyAction Is Nothing Then Return MyBase.ProcessCmdKey(message, keyData)
        Dim key = keyData And Keys.KeyCode
        If key = Keys.Escape Then keyAction = Nothing : status.Text = "Binding cancelled." : Return True
        If key = Keys.ShiftKey Then key = Keys.LShiftKey
        If key = Keys.ControlKey Then key = Keys.LControlKey
        Dim native = DrivingControls.KeyInput(key)
        If native Is Nothing Then status.Text = "That key is not supported here. Try another key, or Escape to cancel." : Return True
        Dim action = keyAction : keyAction = Nothing
        settings.Bindings.RemoveAll(Function(b) b.Action = action AndAlso b.Keyboard)
        settings.Bindings.Add(New DrivingBinding With {.Action = action, .DeviceId = "Keyboard", .Device = "Keyboard", .Input = native})
        status.Text = "Assigned " & key.ToString() & " to " & action & "."
        RefreshRows(action) : Return True
    End Function
    Private Sub BindDevice()
        keyAction = Nothing
        Dim action = SelectedAction()
        If action Is Nothing Then status.Text = "Select an action first." : Return
        Try
            Using dialog As New DrivingCaptureForm(context, action)
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                settings.Bindings.RemoveAll(Function(b) b.Action = action AndAlso Not b.Keyboard)
                settings.Bindings.Add(dialog.Binding)
            End Using
            RefreshRows(action)
        Catch ex As Exception
            status.Text = ex.Message
        End Try
    End Sub
    Private Sub Calibrate()
        Dim action = SelectedAction(), binding = settings.Bindings.FirstOrDefault(Function(b) b.Action = action AndAlso Not b.Keyboard)
        If binding Is Nothing Then status.Text = "Assign a controller input first." : Return
        Using dialog As New Form With {.Text = action & " — Calibration", .ClientSize = New Size(460, 300), .StartPosition = FormStartPosition.CenterParent, .AutoScaleMode = AutoScaleMode.Dpi, .MinimizeBox = False, .MaximizeBox = False}
            Dim panel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .Padding = New Padding(12), .WrapContents = False}
            Dim mode As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 410}
            mode.Items.AddRange({"Button / pedal increasing", "Pedal inverted", "Centered axis — lower half", "Centered axis — upper half"})
            mode.SelectedIndex = Array.IndexOf(DrivingControls.Calibrations, binding.Calibration)
            Dim dead As New ValueSlider("Dead zone", 0, 90, CInt(binding.DeadZone * 100), "%") With {.Width = 410}
            Dim saturation As New ValueSlider("Saturation", 10, 100, CInt(binding.Saturation * 100), "%") With {.Width = 410}
            Dim apply As New Button With {.Text = "Apply", .AutoSize = True}
            Dim errorLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(410, 0)}
            AddHandler apply.Click, Sub()
                                        If dead.Value >= saturation.Value Then errorLabel.Text = "Dead zone must be below saturation." : Return
                                        binding.Calibration = DrivingControls.Calibrations(mode.SelectedIndex)
                                        binding.DeadZone = dead.Value / 100D : binding.Saturation = saturation.Value / 100D
                                        dialog.DialogResult = DialogResult.OK : dialog.Close()
                                    End Sub
            panel.Controls.AddRange({mode, New Label With {.Text = "Dead zone", .AutoSize = True}, dead, New Label With {.Text = "Saturation", .AutoSize = True}, saturation, apply, errorLabel})
            dialog.Controls.Add(panel) : dialog.ShowDialog(Me)
        End Using
    End Sub
End Class

Public Class DrivingCaptureForm
    Inherits Form
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Binding As DrivingBinding
    Private ReadOnly context As InstallContext
    Private ReadOnly action As String
    Private ReadOnly devices As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 530}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(530, 0)}
    Private ReadOnly live As New Label With {.AutoSize = True, .MaximumSize = New Size(530, 0)}
    Private ReadOnly timer As New System.Windows.Forms.Timer With {.Interval = 40}
    Private input As DrivingInput
    Private baseline As DrivingInput.Sample
    Private capturing As Boolean
    Public Sub New(value As InstallContext, actionName As String)
        context = value : action = actionName
        Text = "Bind " & actionName : ClientSize = New Size(570, 330)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False : MaximizeBox = False
        Dim panel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .Padding = New Padding(16)}
        Dim capture As New Button With {.Text = "Capture input", .AutoSize = True}
        Dim refresh As New Button With {.Text = "Refresh devices", .AutoSize = True}
        Dim cancel As New Button With {.Text = "Cancel", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        CancelButton = cancel
        status.Text = "Choose the device. Center the wheel / release the pedal, then click Capture input. Turn or press in the direction for this action. Buttons also work. The controller still reaches the game."
        AddHandler devices.SelectedIndexChanged, Sub() capturing = False
        AddHandler capture.Click, Sub()
                                      If devices.SelectedItem Is Nothing Then Return
                                      baseline = input.Read(DirectCast(devices.SelectedItem, DrivingInput.Device))
                                      capturing = baseline.Connected <> 0
                                      status.Text = If(capturing, "Move the selected input now. Escape cancels.", "Device disconnected. Reconnect and refresh devices.")
                                  End Sub
        AddHandler refresh.Click, AddressOf RefreshDevices
        AddHandler Shown, AddressOf RefreshDevices
        AddHandler timer.Tick, AddressOf Poll
        panel.Controls.AddRange({devices, status, capture, refresh, live, cancel}) : Controls.Add(panel)
    End Sub
    Private Sub RefreshDevices(sender As Object, e As EventArgs)
        timer.Stop() : capturing = False : input?.Dispose() : input = Nothing : devices.Items.Clear()
        Try
            input = New DrivingInput(context, Handle)
            devices.Items.AddRange(input.Devices.Cast(Of Object)().ToArray())
            If devices.Items.Count > 0 Then devices.SelectedIndex = 0
            If devices.Items.Count = 0 Then status.Text = "No connected controllers found. Connect the wheel/pedals or wake the gamepad, then Refresh devices."
            timer.Start()
        Catch ex As Exception
            status.Text = ex.Message
        End Try
    End Sub
    Private Sub Poll(sender As Object, e As EventArgs)
        If devices.SelectedItem Is Nothing OrElse input Is Nothing Then Return
        Dim device = DirectCast(devices.SelectedItem, DrivingInput.Device), current = input.Read(device)
        If current.Connected = 0 Then
            capturing = False : live.Text = "Disconnected — capture cancelled. Reconnect and capture again." : Return
        End If
        live.Text = "Axes: " & String.Join("  ", Enumerable.Range(0, 8).Where(Function(i) (current.Axes And (1UI << i)) <> 0).Select(Function(i) current.Values(i).ToString("0.00"))) & vbCrLf &
            "Pressed buttons: " & String.Join(", ", Enumerable.Range(0, 128).Where(Function(i) current.Buttons(i) <> 0).Select(Function(i) (i + 1).ToString()))
        If Not capturing Then Return
        For i = 0 To baseline.Buttons.Length - 1
            If current.Buttons(i) = 0 Then baseline.Buttons(i) = 0
        Next
        Binding = DrivingInput.Detect(action, device, baseline, current)
        If Binding IsNot Nothing Then timer.Stop() : DialogResult = DialogResult.OK : Close()
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then timer.Dispose() : input?.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class

