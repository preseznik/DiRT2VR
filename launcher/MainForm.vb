Imports System.Windows.Forms
Imports System.Drawing

Public Class MainForm
    Inherits Form
    Private ReadOnly context As InstallContext
    Private settings As VrSettings
    Private ReadOnly runtimeBox As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly stateLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly inputLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly bindingsList As New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False}
    Private ReadOnly toggleButton As New Button With {.AutoSize = True}
    Private ReadOnly recenterButton As New Button With {.AutoSize = True}
    Private ReadOnly launchButton As New Button With {.Text = "Launch VR", .AutoSize = True}
    Private ReadOnly saveButton As New Button With {.Text = "Save settings", .AutoSize = True}
    Private ReadOnly recoverButton As New Button With {.Text = "Restore original files", .AutoSize = True}
    Private ReadOnly input As New ControllerInput()
    Private ReadOnly timer As New System.Windows.Forms.Timer With {.Interval = 100}
    Private keyboardCapture As Integer = -1
    Private controllerCapture As Integer = -1
    Private capturedDevice As ControllerSample
    Private capturedButtons As New HashSet(Of Integer)
    Private busy As Boolean
    Public Sub New(value As InstallContext)
        context = value
        settings = VrSettings.Load(context)
        Text = "DiRT2VR — Experimental launcher"
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        MinimumSize = New Size(820, 690)
        ClientSize = New Size(840, 700)
        StartPosition = FormStartPosition.CenterScreen
        KeyPreview = True
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(20), .ColumnCount = 1, .RowCount = 10}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label With {.Text = "DiRT 2 VR", .Font = New Font(Font.FontFamily, 20, FontStyle.Bold), .AutoSize = True})
        layout.Controls.Add(New Label With {.Text = context.GameRoot, .AutoSize = True, .MaximumSize = New Size(760, 0), .Margin = New Padding(0, 8, 0, 12)})
        layout.Controls.Add(stateLabel)
        Dim runtimeRow As New TableLayoutPanel With {.ColumnCount = 3, .Dock = DockStyle.Top, .AutoSize = True, .Margin = New Padding(0, 15, 0, 15)}
        runtimeRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        runtimeRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        runtimeRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        runtimeRow.Controls.Add(New Label With {.Text = "SteamVR runtime", .AutoSize = True, .Anchor = AnchorStyles.Left})
        runtimeBox.Text = settings.Runtime
        runtimeRow.Controls.Add(runtimeBox)
        Dim browse As New Button With {.Text = "Browse…", .AutoSize = True}
        AddHandler browse.Click, Sub()
                                     Using dialog As New OpenFileDialog With {.Filter = "SteamVR x86 runtime|steamxr_win32.json", .FileName = "steamxr_win32.json"}
                                         If dialog.ShowDialog(Me) = DialogResult.OK Then runtimeBox.Text = dialog.FileName
                                     End Using
                                 End Sub
        runtimeRow.Controls.Add(browse) : layout.Controls.Add(runtimeRow)
        Dim keysRow As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        keysRow.Controls.Add(New Label With {.Text = "Keyboard", .AutoSize = True, .Margin = New Padding(0, 8, 12, 0)})
        keysRow.Controls.Add(toggleButton) : keysRow.Controls.Add(recenterButton)
        AddHandler toggleButton.Click, Sub() BeginKeyCapture(0)
        AddHandler recenterButton.Click, Sub() BeginKeyCapture(1)
        layout.Controls.Add(keysRow)
        Dim bindingGroup As New GroupBox With {.Text = "Gamepad / wheel shortcuts", .Dock = DockStyle.Fill, .Height = 210}
        Dim bindingLayout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Padding = New Padding(8)}
        bindingLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        bindingLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        bindingLayout.Controls.Add(bindingsList)
        Dim bindingButtons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        Dim addToggle As New Button With {.Text = "Bind Toggle VR…", .AutoSize = True}
        Dim addRecenter As New Button With {.Text = "Bind Recenter…", .AutoSize = True}
        Dim remove As New Button With {.Text = "Remove", .AutoSize = True}
        AddHandler addToggle.Click, Sub() BeginControllerCapture(0)
        AddHandler addRecenter.Click, Sub() BeginControllerCapture(1)
        AddHandler remove.Click, Sub()
                                     If Not busy AndAlso bindingsList.SelectedIndex >= 0 Then
                                         settings.Bindings.RemoveAt(bindingsList.SelectedIndex) : RefreshBindings()
                                     End If
                                 End Sub
        bindingButtons.Controls.AddRange({addToggle, addRecenter, remove})
        bindingLayout.Controls.Add(bindingButtons) : bindingGroup.Controls.Add(bindingLayout)
        layout.Controls.Add(bindingGroup)
        layout.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(760, 0), .Text = "Bind one button or a two-button combination on the same device. These buttons still perform their normal game actions. Xbox slot changes may require rebinding.", .Margin = New Padding(0, 8, 0, 8)})
        layout.Controls.Add(inputLabel)
        Dim commands As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top, .Margin = New Padding(0, 16, 0, 8)}
        Dim logs As New Button With {.Text = "Open logs", .AutoSize = True}
        commands.Controls.AddRange({launchButton, saveButton, recoverButton, logs})
        AddHandler saveButton.Click, Sub() SafeAction(Sub() SaveSettings())
        AddHandler launchButton.Click, Sub() SafeAction(Sub()
                                                           SaveSettings()
                                                           Spawn("--launch", "--no-ui")
                                                       End Sub)
        AddHandler recoverButton.Click, Sub() SafeAction(Sub() Spawn("--recover"))
        AddHandler logs.Click, Sub() SafeAction(Sub()
                                                   Dim folder = IO.Path.Combine(context.UserRoot, "logs")
                                                   Directory.CreateDirectory(folder)
                                                   Process.Start(New ProcessStartInfo(folder) With {.UseShellExecute = True})
                                               End Sub)
        layout.Controls.Add(commands)
        layout.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(760, 0), .Text = "Start SteamVR and connect your headset before launching. Use the Subaru STI cockpit for the tested setup. The background session manager restores temporary files when the game exits; this window can be closed."})
        Controls.Add(layout)
        RefreshBindings()
        AddHandler input.StateChanged, AddressOf OnController
        AddHandler timer.Tick, Sub()
                                  input.Poll()
                                  RefreshStatus()
                              End Sub
        AddHandler FormClosed, Sub()
                                  timer.Stop() : timer.Dispose() : input.Dispose()
                              End Sub
        timer.Start() : RefreshStatus()
    End Sub
    Private Sub SafeAction(action As Action)
        Try
            action()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "DiRT2VR", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
    Private Sub SaveSettings()
        settings.Runtime = runtimeBox.Text.Trim()
        settings.Validate()
        Files.SaveJson(context.PreferencesPath, settings)
        stateLabel.Text = "Settings saved. Changes apply to the next VR session."
    End Sub
    Private Sub Spawn(ParamArray arguments As String())
        Dim start As New ProcessStartInfo(Environment.ProcessPath) With {.UseShellExecute = False}
        For Each arg In arguments.Concat({"--game", context.GameRoot})
            start.ArgumentList.Add(arg)
        Next
        Process.Start(start)
    End Sub
    Private Sub RefreshBindings()
        toggleButton.Text = "Toggle VR: " & KeyLabel(settings.ToggleKey, settings.ToggleModifiers)
        recenterButton.Text = "Recenter: " & KeyLabel(settings.RecenterKey, settings.RecenterModifiers)
        Dim selected = bindingsList.SelectedIndex
        Dim devices = input.Snapshot()
        bindingsList.Items.Clear()
        For Each binding In settings.Bindings
            Dim connected = devices.Any(Function(s) s.Source = binding.Source AndAlso s.Device = binding.Device AndAlso s.Connected)
            bindingsList.Items.Add(binding.ToString() & If(connected, "", " [disconnected / not detected]"))
        Next
        If selected >= 0 AndAlso selected < bindingsList.Items.Count Then bindingsList.SelectedIndex = selected
    End Sub
    Private Shared Function KeyLabel(key As Integer, modifiers As Integer) As String
        Return If((modifiers And 1) <> 0, "Ctrl+", "") & If((modifiers And 2) <> 0, "Alt+", "") & If((modifiers And 4) <> 0, "Shift+", "") & CType(key, Keys).ToString()
    End Function
    Private Sub BeginKeyCapture(action As Integer)
        If busy Then Return
        keyboardCapture = action : controllerCapture = -1
        inputLabel.Text = "Press a key with optional Ctrl/Alt/Shift. Escape cancels."
    End Sub
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Escape Then
            keyboardCapture = -1 : controllerCapture = -1 : capturedDevice = Nothing
            inputLabel.Text = "Binding cancelled." : e.SuppressKeyPress = True : Return
        End If
        If keyboardCapture >= 0 Then
            e.SuppressKeyPress = True
            If Not VrSettings.ValidKey(CInt(e.KeyCode)) Then Return
            Dim modifiers = If(e.Control, 1, 0) Or If(e.Alt, 2, 0) Or If(e.Shift, 4, 0)
            If e.Alt AndAlso (e.KeyCode = Keys.F4 OrElse e.KeyCode = Keys.Tab) Then Return
            If (keyboardCapture = 0 AndAlso CInt(e.KeyCode) = settings.RecenterKey AndAlso modifiers = settings.RecenterModifiers) OrElse (keyboardCapture = 1 AndAlso CInt(e.KeyCode) = settings.ToggleKey AndAlso modifiers = settings.ToggleModifiers) Then
                inputLabel.Text = "That shortcut is assigned to the other action." : Return
            End If
            If keyboardCapture = 0 Then
                settings.ToggleKey = CInt(e.KeyCode) : settings.ToggleModifiers = modifiers
            Else
                settings.RecenterKey = CInt(e.KeyCode) : settings.RecenterModifiers = modifiers
            End If
            keyboardCapture = -1 : inputLabel.Text = "Keyboard binding captured. Save settings to keep it."
            RefreshBindings() : Return
        End If
        MyBase.OnKeyDown(e)
    End Sub
    Private Sub BeginControllerCapture(action As Integer)
        If busy Then Return
        keyboardCapture = -1 : controllerCapture = action : capturedDevice = Nothing : capturedButtons.Clear()
        inputLabel.Text = "Press one or two buttons together, then release them. Escape cancels."
    End Sub
    Private Sub OnController(sample As ControllerSample)
        RefreshBindings()
        If controllerCapture < 0 Then
            Dim available = input.Snapshot().Where(Function(s) s.Connected).Select(Function(s) s.Label & If(s.Buttons.Count > 0, ": " & String.Join(" + ", s.Buttons.Select(Function(b) ControllerNames.ButtonName(s.Source, b))), ": connected")).ToArray()
            If keyboardCapture < 0 Then inputLabel.Text = If(available.Length = 0, "No controller detected. Connect a device; press and release a wheel button to detect it.", String.Join("; ", available))
            Return
        End If
        If Not sample.Connected Then Return
        If capturedDevice Is Nothing AndAlso sample.Buttons.Count > 0 Then capturedDevice = sample
        If capturedDevice Is Nothing OrElse capturedDevice.Device <> sample.Device OrElse capturedDevice.Source <> sample.Source Then Return
        capturedButtons.UnionWith(sample.Buttons)
        If sample.Buttons.Count > 0 Then Return
        Dim binding As New ControllerBinding With {.Action = controllerCapture, .Source = sample.Source, .Device = sample.Device, .Label = sample.Label, .Buttons = capturedButtons.Order().ToList()}
        controllerCapture = -1 : capturedDevice = Nothing
        settings.Bindings.Add(binding)
        Try
            settings.Validate()
            inputLabel.Text = "Controller binding captured. Save settings to keep it."
        Catch ex As Exception
            settings.Bindings.Remove(binding) : inputLabel.Text = ex.Message
        End Try
        RefreshBindings()
    End Sub
    Private Sub RefreshStatus()
        Try
            Dim statusPath = IO.Path.Combine(context.UserRoot, "session.json")
            Dim status = If(File.Exists(statusPath), Files.ReadJson(Of SessionStatus)(statusPath), Nothing)
            busy = False
            If status IsNot Nothing AndAlso status.State <> "Ready" AndAlso status.State <> "Failed" Then
                Try
                    Using owner = Process.GetProcessById(status.ProcessId)
                        busy = Not owner.HasExited AndAlso owner.ProcessName = Process.GetCurrentProcess().ProcessName
                    End Using
                Catch
                End Try
            End If
            launchButton.Enabled = Not busy : recoverButton.Enabled = Not busy : saveButton.Enabled = Not busy
            If busy Then
                stateLabel.Text = status.State & If(status.Message <> "", ": " & status.Message, "")
            ElseIf New AssetTransaction(context).Pending OrElse New GraphicsTransaction(context).Pending Then
                stateLabel.Text = "Recovery pending. Close the game and choose Restore original files."
            ElseIf status IsNot Nothing AndAlso status.State = "Failed" Then
                stateLabel.Text = "Failed: " & status.Message
            ElseIf Not File.Exists(IO.Path.Combine(context.GameRoot, "dirt2_game.exe")) Then
                stateLabel.Text = "Extract the complete package into your DiRT 2 game folder."
            Else
                stateLabel.Text = "Ready — the game build and files will be checked before launching."
            End If
        Catch ex As Exception
            stateLabel.Text = ex.Message
        End Try
    End Sub
End Class
