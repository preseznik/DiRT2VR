Imports System.Drawing
Imports System.Windows.Forms

Public Class DrivingControlsForm
    Inherits Form
    Private ReadOnly context As InstallContext
    Private ReadOnly settings As DrivingControls
    Private ReadOnly enabledBox As New CheckBox With {.Text = "Use launcher driving bindings (all launcher modes, DX11)", .AutoSize = True}
    Private ReadOnly list As New ListView With {.View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .Dock = DockStyle.Fill}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(820, 0)}
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
        layout.Controls.Add(New Label With {.Text = "Start with the Binding wizard or Xbox preset. Unassigned actions use the game's saved controls. An assigned action replaces its saved bindings: assign both keyboard and controller inputs if you want both. Save below when finished.", .AutoSize = True, .MaximumSize = New Size(820, 0), .Margin = New Padding(0, 10, 0, 10)})
        list.Columns.Add("Action", 160) : list.Columns.Add("Keyboard", 200) : list.Columns.Add("Controller / wheel / pedals", 470)
        list.AccessibleName = "Driving bindings" : layout.Controls.Add(list)
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        AddButton(buttons, "Binding wizard…", Sub() BindActions())
        AddButton(buttons, "Xbox preset", Sub()
                                               settings.Bindings.RemoveAll(Function(b) Not b.Keyboard)
                                               settings.Bindings.AddRange(DrivingControls.XboxPreset())
                                               enabledBox.Checked = True : RefreshRows()
                                               status.Text = "Standard Xbox driving controls applied (left stick, RT/LT, A handbrake, B/X gears). Keyboard bindings retained. Save to use them."
                                           End Sub)
        AddButton(buttons, "Bind keyboard…", Sub() BindSelected(True))
        AddButton(buttons, "Bind device…", Sub() BindSelected(False))
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
        If settings.Problems().Count > 0 Then status.Text = String.Join(" ", settings.Problems())
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
            row.SubItems.Add(If(keyboard Is Nothing AndAlso bindings.Length > 0, "—", DrivingInput.Description(keyboard)))
            row.SubItems.Add(If(device Is Nothing AndAlso bindings.Length > 0, "—", DrivingInput.Description(device)))
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
        Try
            Using dialog As New DrivingBindingWizard(context, action, keyboard)
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                For Each binding In dialog.Bindings
                    settings.Bindings.RemoveAll(Function(b) b.Action = binding.Action AndAlso b.Keyboard = binding.Keyboard)
                    settings.Bindings.Add(binding)
                Next
                If dialog.Bindings.Count > 0 Then enabledBox.Checked = True
            End Using
            RefreshRows(action)
            status.Text = If(settings.Problems().Count > 0, String.Join(" ", settings.Problems()), "Bindings applied. Save driving controls to use them on the next launch.")
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

