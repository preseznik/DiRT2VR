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
    Private ReadOnly combinedGearsBox As New CheckBox With {.Text = "Use sequential and H-pattern shifts together (manual transmission, experimental)", .AutoSize = True}
    Private ReadOnly transmissionBox As New ComboBox With {.Name = "DrivingTransmission", .AccessibleName = "Transmission", .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 240}
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
        enabledBox.Checked = settings.Enabled
        Dim options As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        options.Controls.Add(enabledBox)
        If Not replay Then
            combinedGearsBox.Checked = settings.CombinedManualGears
            options.Controls.Add(combinedGearsBox)
            transmissionBox.Items.AddRange(DrivingControls.TransmissionNames)
            transmissionBox.SelectedIndex = Array.IndexOf(DrivingControls.Transmissions, settings.Transmission)
            Dim transmissionRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 6, 0, 0)}
            transmissionRow.Controls.Add(New Label With {.Text = "Transmission (experimental)", .AutoSize = True, .Margin = New Padding(0, 6, 8, 0)})
            transmissionRow.Controls.Add(transmissionBox)
            NewFeatures.MarkIfNew(transmissionRow, transmissionBox)
            options.Controls.Add(transmissionRow)
        End If
        layout.Controls.Add(options)
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
        If Not replay Then AddButton(buttons, "Wheel rotation…", AddressOf WheelRotation, "DrivingWheelRotation")
        AddButton(buttons, If(replay, "Clear / use game binding", "Use game binding"), Sub()
                                                     Dim action = SelectedAction()
                                                     If action Is Nothing Then Return
                                                     settings.Bindings.RemoveAll(Function(b) b.Action = action)
                                                     RefreshRows(action)
                                                 End Sub)
        If Not replay Then
            AddButton(buttons, "Save preset…", AddressOf SavePreset, "DrivingSavePreset")
            AddButton(buttons, "Load preset…", AddressOf LoadPreset, "DrivingLoadPreset")
        End If
        AddButton(buttons, If(replay, "Save replay controls", "Save driving controls"), Sub()
                                                          Try
                                                              context.RequireClosed()
                                                              settings.Enabled = enabledBox.Checked
                                                              If replayMode Then
                                                                  DirectCast(settings, ReplayControls).Save(context)
                                                              Else
                                                                  ReadOptions()
                                                                  settings.Save(context)
                                                              End If
                                                              DialogResult = DialogResult.OK : Close()
                                                          Catch ex As Exception
                                                              status.Text = ex.Message
                                                          End Try
                                                      End Sub)
        AddButton(buttons, "Cancel", Sub() Close())
        layout.Controls.Add(buttons) : layout.Controls.Add(status) : Controls.Add(layout)
        status.Text = If(replay, "Zoom: left stick up/down or Page Up/Down by default; keyboard/Xbox bindings supported. Other actions also support wheels. Free look must be enabled. Changes apply on next launch.", "Transmission applies to your car in every launcher mode without changing your career's saved setting; Ferrari F2004 always uses sequential. Combined shifts use the most recently moved shifter or shift button with a manual transmission. Save to apply next launch.")
        RefreshRows()
        If settings.Problems().Count > 0 Then status.Text = String.Join(" ", settings.Problems())
    End Sub
    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        Dim area = Screen.FromControl(Me).WorkingArea
        MinimumSize = New Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height))
        Size = New Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height))
    End Sub
    Private Shared Function AddButton(panel As FlowLayoutPanel, text As String, action As Action, Optional name As String = Nothing) As Button
        Dim button As New Button With {.Text = text, .AutoSize = True, .Name = If(name, "")}
        AddHandler button.Click, Sub() action()
        panel.Controls.Add(button)
        NewFeatures.MarkIfNew(panel, button)
        Return button
    End Function
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
    Private Sub SavePreset()
        Try
            Dim folder = DrivingControls.PresetFolder(context)
            Directory.CreateDirectory(folder)
            Using dialog As New SaveFileDialog With {.Title = "Save driving controls preset", .Filter = "Driving controls preset (*.json)|*.json", .DefaultExt = "json", .AddExtension = True,
                                                     .InitialDirectory = folder, .FileName = "Driving controls.json", .OverwritePrompt = True}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                settings.Enabled = enabledBox.Checked : ReadOptions()
                settings.SavePreset(dialog.FileName)
                status.Text = "Preset saved: " & IO.Path.GetFileNameWithoutExtension(dialog.FileName) & ". Load preset… restores it later, on any DiRT2VR installation on this PC."
            End Using
        Catch ex As Exception
            status.Text = ex.Message
        End Try
    End Sub
    Private Sub ReadOptions()
        settings.CombinedManualGears = combinedGearsBox.Checked
        settings.Transmission = DrivingControls.Transmissions(transmissionBox.SelectedIndex)
    End Sub
    Private Sub LoadPreset()
        Try
            Dim folder = DrivingControls.PresetFolder(context)
            Using dialog As New OpenFileDialog With {.Title = "Load driving controls preset", .Filter = "Driving controls preset (*.json)|*.json", .InitialDirectory = If(Directory.Exists(folder), folder, "")}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim preset = DrivingControls.LoadPreset(dialog.FileName)
                settings.Bindings.Clear() : settings.Bindings.AddRange(preset.Bindings)
                enabledBox.Checked = preset.Enabled : combinedGearsBox.Checked = preset.CombinedManualGears
                transmissionBox.SelectedIndex = Array.IndexOf(DrivingControls.Transmissions, preset.Transmission)
                settings.WheelRotation = preset.WheelRotation
                RefreshRows(SelectedAction())
                status.Text = If(settings.Problems().Count > 0, String.Join(" ", settings.Problems()), "Preset loaded: " & IO.Path.GetFileNameWithoutExtension(dialog.FileName) & ". Save driving controls to use it on the next launch.")
            End Using
        Catch ex As Exception
            status.Text = ex.Message
        End Try
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
    Private Sub WheelRotation()
        Dim steering = settings.SteeringWheel()
        If steering Is Nothing Then status.Text = "Bind Steer Left and Steer Right to your wheel first, with the Binding wizard or Bind device…." : Return
        Dim device = steering(0).Device, suggested = DrivingControls.SuggestedWheelRotation(device)
        Dim wheelValue = If(settings.WheelRotation <> 0, settings.WheelRotation, If(suggested <> 0, suggested, 900))
        Dim gameValue = Math.Clamp(CInt(Math.Round(steering.Max(Function(b) b.Saturation) * wheelValue)), 90, wheelValue)
        Using dialog As New Form With {.Name = "WheelRotationDialog", .Text = "Wheel rotation", .ClientSize = New Size(520, 500), .StartPosition = FormStartPosition.CenterParent, .AutoScaleMode = AutoScaleMode.Dpi, .MinimizeBox = False, .MaximizeBox = False}
            Dim panel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .Padding = New Padding(12), .WrapContents = False, .AutoScroll = True}
            Dim note = If(suggested <> 0, $"Suggested: {suggested}°, this wheel's maximum. If you lowered it in the wheel's software, use that value.",
                If(device.ToUpperInvariant().Contains("FANATEC"), "Use the SEN value from your wheelbase's tuning menu.", "Use the rotation set in your wheel's software."))
            Dim wheel As New ValueSlider("WheelRotationHardware", DrivingControls.MinimumWheelRotation, DrivingControls.MaximumWheelRotation, wheelValue, "°") With {.Width = 470}
            Dim game As New ValueSlider("WheelRotationGame", 90, wheelValue, gameValue, "°") With {.Width = 470}
            Dim result As New Label With {.AutoSize = True, .MaximumSize = New Size(470, 0), .Margin = New Padding(3, 8, 3, 8)}
            Dim errorLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(470, 0)}
            Dim show = Sub() result.Text = $"Full steering lock at {game.Value}° lock to lock: {Math.Round(CDec(game.Value) / wheel.Value, 2):P0} of your wheel's travel (Steer Left/Right saturation)."
            AddHandler wheel.ValueChanged, Sub()
                                               game.Maximum = wheel.Value
                                               show()
                                           End Sub
            AddHandler game.ValueChanged, Sub() show()
            show()
            Dim apply As New Button With {.Text = "Apply", .AutoSize = True}
            AddHandler apply.Click, Sub()
                                        Try
                                            settings.ApplyWheelRotation(wheel.Value, game.Value)
                                            dialog.DialogResult = DialogResult.OK : dialog.Close()
                                        Catch ex As IOException
                                            errorLabel.Text = ex.Message
                                        End Try
                                    End Sub
            panel.Controls.AddRange({New Label With {.Text = "Wheel: " & device, .AutoSize = True, .MaximumSize = New Size(470, 0)},
                New Label With {.Text = "Your wheel's rotation, lock to lock", .AutoSize = True, .Margin = New Padding(3, 10, 3, 0)}, wheel,
                New Label With {.Text = note, .AutoSize = True, .MaximumSize = New Size(470, 0)},
                New Label With {.Text = "Steering rotation in DiRT 2, lock to lock", .AutoSize = True, .Margin = New Padding(3, 10, 3, 0)}, game, result,
                New Label With {.Text = "The cockpit wheel can't turn further than each car's animation. Lower DiRT 2's rotation until the cockpit wheel keeps up with your hands. If your wheel's software can set its rotation, setting it to the same value also stops the wheel at full lock.", .AutoSize = True, .MaximumSize = New Size(470, 0)},
                apply, errorLabel})
            dialog.Controls.Add(panel)
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
        End Using
        RefreshRows("Steer Left")
        status.Text = $"Wheel rotation applied: full lock at {CInt(Math.Round(steering(0).Saturation * settings.WheelRotation))}° of your {settings.WheelRotation}° wheel. Save driving controls to use it on the next launch."
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

