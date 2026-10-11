Imports System.Drawing
Imports System.Windows.Forms

Public Class DrivingControlsForm
    Inherits LauncherForm
    Private ReadOnly context As InstallContext
    Private ReadOnly liveInput As DrivingInputMonitor
    Private ReadOnly settings As DrivingControls
    Private ReadOnly replayMode As Boolean
    Private ReadOnly actions As String()
    Private ReadOnly enabledBox As New CheckBox With {.Text = "Use launcher driving bindings (all launcher modes, DX11)", .AutoSize = True}
    Private ReadOnly list As New ListView With {.Name = "DrivingBindings", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .ShowItemToolTips = True, .Dock = DockStyle.Fill}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(820, 0)}
    Public Sub New(value As InstallContext, Optional replay As Boolean = False)
        context = value : replayMode = replay
        settings = If(replay, DirectCast(ReplayControls.Load(context), DrivingControls), DrivingControls.Load(context))
        actions = If(replay, ReplayControls.ReplayActions, DrivingControls.Actions)
        If replay Then enabledBox.Text = "Use launcher replay bindings (DX11)"
        liveInput = New DrivingInputMonitor(context)
        Text = If(replay, "DiRT2VR — Replay controls", "DiRT2VR — Driving controls") : Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(940, 820) : MinimumSize = New Size(720, 620)
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 6, .Padding = New Padding(16)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each sizing As SizeType In {SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize}
            layout.RowStyles.Add(New RowStyle(sizing, If(sizing = SizeType.Percent, 100, 0)))
        Next
        enabledBox.Checked = settings.Enabled : layout.Controls.Add(enabledBox)
        layout.Controls.Add(New Label With {.Text = If(replay, "Select a replay action, bind a key or device input, then save. Unassigned native actions keep the game's bindings; clearing zoom leaves the mouse wheel available. Avoid using the same input for different replay actions.", "Assigned controls apply in every launcher mode; unassigned actions keep the game's bindings. Use the wizard or select an action, then save."), .AutoSize = True, .MaximumSize = New Size(820, 0), .Margin = New Padding(0, 10, 0, 10)})
        list.Columns.Add("Action", 125) : list.Columns.Add("Keyboard", 100) : list.Columns.Add("Controller / wheel / pedals", 190)
        list.Columns.Add("Dead zone", 110) : list.Columns.Add("Saturation", 110)
        AddHandler list.Resize, Sub() list.Columns(2).Width = Math.Max(190, list.ClientSize.Width - 125 - 100 - 110 - 110 - 24)
        list.AccessibleName = "Driving bindings" : layout.Controls.Add(list)
        layout.Controls.Add(liveInput)
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        If Not replay Then AddButton(buttons, "Binding wizard…", Sub() BindActions())
        AddButton(buttons, If(replay, "Restore replay defaults", "Xbox preset"), Sub()
                                               If replayMode Then
                                                   DirectCast(settings, ReplayControls).ApplyReplayPreset()
                                               Else
                                                   settings.ApplyXboxPreset()
                                               End If
                                               enabledBox.Checked = True : RefreshRows()
                                               status.Text = If(replayMode, "Replay defaults restored, including left-stick up/down zoom. Save to apply.", "Xbox controls applied. Missing keyboard bindings filled with game defaults; your custom keys are kept. Save to use both.")
                                           End Sub)
        AddButton(buttons, "Bind keyboard…", Sub() BindSelected(True))
        AddButton(buttons, "Bind device…", Sub() BindSelected(False))
        AddButton(buttons, "Calibration…", AddressOf Calibrate)
        AddButton(buttons, If(replay, "Clear / use game binding", "Use game binding"), Sub()
                                                     Dim action = SelectedAction()
                                                     If action Is Nothing Then Return
                                                     settings.Bindings.RemoveAll(Function(b) b.Action = action)
                                                     RefreshRows(action)
                                                 End Sub)
        AddButton(buttons, If(replay, "Save replay controls", "Save driving controls"), Sub()
                                                          Try
                                                              context.RequireClosed()
                                                              settings.Enabled = enabledBox.Checked
                                                              If replayMode Then
                                                                  DirectCast(settings, ReplayControls).Save(context)
                                                              Else
                                                                  settings.Save(context)
                                                              End If
                                                              DialogResult = DialogResult.OK : Close()
                                                          Catch ex As Exception
                                                              status.Text = ex.Message
                                                          End Try
                                                      End Sub)
        AddButton(buttons, "Cancel", Sub() Close())
        layout.Controls.Add(buttons) : layout.Controls.Add(status) : Controls.Add(layout)
        status.Text = If(replay, "Zoom: left stick up/down or Page Up/Down by default; keyboard/Xbox bindings supported. Other actions also support wheels. Free look must be enabled. Changes apply on next launch.", "Choose transmission and assists in-game for clutch and H-pattern gears. Save to apply on the next launch.")
        RefreshRows()
        If settings.Problems().Count > 0 Then status.Text = String.Join(" ", settings.Problems())
    End Sub
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Dim area = Screen.FromControl(Me).WorkingArea
        MinimumSize = New Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height))
        Size = New Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height))
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
        For Each action In actions
            Dim bindings = settings.Bindings.Where(Function(b) b.Action = action).ToArray()
            Dim keyboard = bindings.FirstOrDefault(Function(b) b.Keyboard), device = bindings.FirstOrDefault(Function(b) Not b.Keyboard)
            Dim row As New ListViewItem(action)
            row.SubItems.Add(If(keyboard Is Nothing, If(bindings.Length > 0, "—", If(ReplayControls.ZoomActions.Contains(action), "Unassigned", "Game")), DrivingInput.Description(keyboard)))
            row.SubItems.Add(If(device Is Nothing, If(bindings.Length > 0, "—", If(ReplayControls.ZoomActions.Contains(action), "Unassigned", "Use game binding")), DrivingInput.Description(device)))
            row.SubItems.Add(If(device Is Nothing, "—", device.DeadZone.ToString("P0")))
            row.SubItems.Add(If(device Is Nothing, "—", device.Saturation.ToString("P0")))
            row.ToolTipText = String.Join(" · ", row.SubItems.Cast(Of ListViewItem.ListViewSubItem)().Select(Function(cell) cell.Text))
            list.Items.Add(row) : row.Selected = action = selected
        Next
        list.EndUpdate()
    End Sub
    Private Sub BindSelected(keyboard As Boolean)
        Dim action = SelectedAction()
        If action Is Nothing Then status.Text = "Select an action first." : Return
        BindActions(action, keyboard)
    End Sub
    Private Sub BindActions(Optional action As String = Nothing, Optional keyboard As Boolean = False)
        liveInput.SuspendMonitoring()
        Try
            Using dialog As New DrivingBindingWizard(context, action, keyboard)
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                If replayMode AndAlso ReplayControls.ZoomActions.Contains(action) AndAlso dialog.Bindings.Any(Function(b) Not b.Keyboard AndAlso b.Device <> "win_xinput") Then Throw New IOException("Custom chase zoom supports keyboard or Xbox inputs. Your previous binding was kept.")
                For Each binding In dialog.Bindings
                    settings.Bindings.RemoveAll(Function(b) b.Action = binding.Action AndAlso b.Keyboard = binding.Keyboard)
                    settings.Bindings.Add(binding)
                Next
                If dialog.Bindings.Count > 0 Then enabledBox.Checked = True
            End Using
            RefreshRows(action)
            status.Text = If(settings.Problems().Count > 0, String.Join(" ", settings.Problems()), "Bindings applied. Save controls to use them on the next launch.")
        Catch ex As Exception
            status.Text = ex.Message
        Finally
            liveInput.ResumeMonitoring()
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
        RefreshRows(action)
    End Sub
End Class

