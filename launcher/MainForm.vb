Imports System.Windows.Forms
Imports System.Drawing

Public Class MainForm
    Inherits Form
    Private ReadOnly context As InstallContext
    Private settings As VrSettings
    Private ReadOnly runtimeBox As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly stateLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly inputLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly bindingLists As ListBox() = {New ListBox(), New ListBox()}
    Private ReadOnly tabs As New TabControl With {.Dock = DockStyle.Fill, .Name = "LauncherTabs"}
    Private ReadOnly renderScale As NumericUpDown = Percentage("RenderScale", 50, 150, 100)
    Private ReadOnly headsetScale As NumericUpDown = Percentage("HeadsetScale", 25, 100, 50)
    Private ReadOnly fieldOfView As NumericUpDown = Percentage("FieldOfView", 70, 100, 100)
    Private ReadOnly mirrors As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top, .DropDownWidth = 230, .Name = "Mirrors"}
    Private ReadOnly graphicsSummary As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0)}
    Private ReadOnly refreshLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0)}
    Private lastStatus As String = ""
    Private ReadOnly toggleButton As New Button With {.AutoSize = True}
    Private ReadOnly recenterButton As New Button With {.AutoSize = True}
    Private ReadOnly launchButton As New Button With {.Text = "Launch VR", .AutoSize = True}
    Private ReadOnly saveButton As New Button With {.Text = "Save settings", .AutoSize = True, .Name = "SaveSettings"}
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
        Using stream = GetType(MainForm).Assembly.GetManifestResourceStream("DiRT2VR.ico"), appIcon As New Icon(stream)
            Icon = DirectCast(appIcon.Clone(), Icon)
        End Using
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        MinimumSize = New Size(820, 690)
        ClientSize = New Size(840, 700)
        StartPosition = FormStartPosition.CenterScreen
        KeyPreview = True
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(20), .ColumnCount = 1, .RowCount = 4}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label With {.Text = "DiRT 2 VR", .Font = New Font(Font.FontFamily, 20, FontStyle.Bold), .AutoSize = True})
        stateLabel.Margin = New Padding(0, 8, 0, 16)
        layout.Controls.Add(stateLabel)
        layout.Controls.Add(tabs)
        BuildLaunchTab()
        BuildGraphicsTab()
        BuildControlsTab()
        AddHandler tabs.SelectedIndexChanged, Sub()
                                                  keyboardCapture = -1 : controllerCapture = -1 : capturedDevice = Nothing
                                                  inputLabel.Text = "Select a binding to change it."
                                                  RefreshBindings()
                                              End Sub
        Dim commands As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .Margin = New Padding(0, 16, 0, 0)}
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
        Controls.Add(layout)
        RefreshBindings() : RefreshDisplayRate()
        AddHandler input.StateChanged, AddressOf OnController
        AddHandler timer.Tick, Sub()
                                  input.Poll()
                                  RefreshStatus()
                              End Sub
        AddHandler FormClosed, Sub()
                                  timer.Stop() : timer.Dispose() : input.Dispose()
                                  Icon.Dispose()
                              End Sub
        timer.Start() : RefreshStatus()
    End Sub
    Private Shared Function Percentage(name As String, low As Integer, high As Integer, value As Integer) As NumericUpDown
        Return New NumericUpDown With {.Name = name, .AccessibleName = name, .Minimum = low, .Maximum = high, .Value = value, .Increment = 5, .Width = 90}
    End Function
    Private Function TabLayout(title As String) As TableLayoutPanel
        Dim page As New TabPage(title) With {.Padding = New Padding(16), .UseVisualStyleBackColor = True, .AutoScroll = True}
        Dim content As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 1}
        content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        page.Controls.Add(content) : tabs.TabPages.Add(page)
        Return content
    End Function
    Private Shared Function Note(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(710, 0), .Margin = New Padding(0, 8, 0, 12)}
    End Function
    Private Sub BuildLaunchTab()
        Dim content = TabLayout("Launch")
        content.Controls.Add(Note("Game folder" & Environment.NewLine & context.GameRoot))
        Dim runtimeRow As New TableLayoutPanel With {.ColumnCount = 3, .Dock = DockStyle.Top, .AutoSize = True, .Margin = New Padding(0, 12, 0, 12)}
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
        runtimeRow.Controls.Add(browse) : content.Controls.Add(runtimeRow)
        content.Controls.Add(Note("Start SteamVR and connect your headset before launching. Use the Subaru STI cockpit for the tested setup."))
        content.Controls.Add(Note("The game starts on the virtual menu screen. Use Toggle VR to enter cockpit VR, and Recenter when seated facing forward. Pause menus return to the screen automatically."))
        content.Controls.Add(Note("Quit the game normally to restore temporary files. You can close this settings window while playing; the background session manager stays running."))
    End Sub
    Private Sub BuildGraphicsTab()
        Dim content = TabLayout("Graphics")
        graphicsSummary.Margin = New Padding(0, 0, 0, 12)
        content.Controls.Add(graphicsSummary)
        Dim grid As New TableLayoutPanel With {.ColumnCount = 3, .Dock = DockStyle.Top, .AutoSize = True}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 215))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 195))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        renderScale.Value = settings.RenderScale : headsetScale.Value = settings.HeadsetScale : fieldOfView.Value = settings.FieldOfView
        mirrors.Items.AddRange({"Use game setting", "Enabled", "Disabled"})
        mirrors.SelectedIndex = Array.IndexOf({"game", "on", "off"}, settings.Mirrors)
        AddGraphicsRow(grid, "Render resolution (%)", renderScale, "Lower for performance; higher for detail. Default: 100%.")
        AddGraphicsRow(grid, "Headset texture (%)", headsetScale, "Relative to SteamVR's recommended size. Default: 50%.")
        AddGraphicsRow(grid, "Field of view (%)", fieldOfView, "Experimental crop. 100% = full view. Lower = fewer pixels, narrower view.")
        AddGraphicsRow(grid, "Car mirrors", mirrors, "Disabling mirrors may reduce GPU work.")
        content.Controls.Add(grid)
        content.Controls.Add(Note("Render resolution controls scene detail. Raising headset texture scale alone cannot add missing detail. Cropping reduces peripheral vision; performance gains depend on the scene."))
        content.Controls.Add(New Label With {.Text = "Headset refresh rate", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        content.Controls.Add(refreshLabel)
        content.Controls.Add(Note("Set refresh rate in SteamVR or your headset connection software before launching. DiRT2VR follows the runtime; the game's desktop refresh setting does not select headset Hz."))
        Dim defaults As New Button With {.Text = "Restore graphics defaults", .AutoSize = True}
        AddHandler defaults.Click, Sub()
                                       renderScale.Value = 100 : headsetScale.Value = 50 : fieldOfView.Value = 100 : mirrors.SelectedIndex = 0
                                   End Sub
        content.Controls.Add(defaults)
        content.Controls.Add(Note("Save settings to apply on the next launch. Crowds, particles, shadows and motion blur retain the current reduced-effects setup."))
        AddHandler renderScale.ValueChanged, Sub() RefreshGraphicsSummary()
        AddHandler headsetScale.ValueChanged, Sub() RefreshGraphicsSummary()
        AddHandler fieldOfView.ValueChanged, Sub() RefreshGraphicsSummary()
        RefreshGraphicsSummary()
    End Sub
    Private Shared Sub AddGraphicsRow(grid As TableLayoutPanel, title As String, control As Control, description As String)
        Dim row = grid.RowCount
        grid.RowCount += 1 : grid.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        grid.Controls.Add(New Label With {.Text = title, .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, row)
        control.Margin = New Padding(3, 6, 3, 12)
        grid.Controls.Add(control, 1, row)
        grid.Controls.Add(New Label With {.Text = description, .AutoSize = True, .MaximumSize = New Size(290, 0), .Margin = New Padding(4, 6, 0, 12)}, 2, row)
    End Sub
    Private Sub RefreshGraphicsSummary()
        Dim preview As New VrSettings With {.RenderScale = CInt(renderScale.Value), .FieldOfView = CInt(fieldOfView.Value)}
        Dim pixels = preview.RenderWidth * CDbl(preview.RenderHeight) / (1600 * 1200)
        graphicsSummary.Text = $"Scene per eye: {preview.RenderWidth} × {preview.RenderHeight} ({pixels:P0} of default pixels). Headset texture: {headsetScale.Value}% of recommended width and height."
    End Sub
    Private Sub RefreshDisplayRate()
        refreshLabel.Text = "Runtime-controlled. Launch once to record the headset's reported refresh rate."
        Try
            Dim logs = IO.Path.Combine(context.UserRoot, "logs")
            If Not Directory.Exists(logs) Then Return
            Dim latest = Directory.GetDirectories(logs).OrderByDescending(Function(path) IO.Path.GetFileName(path)).FirstOrDefault()
            If latest Is Nothing Then Return
            Dim report = IO.Path.Combine(latest, "preflight.txt")
            If Not File.Exists(report) Then Return
            Dim match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(report), "(?m)^display_refresh_hz=([0-9.]+)")
            refreshLabel.Text = If(match.Success, $"Last launch reported {match.Groups(1).Value} Hz ({IO.Path.GetFileName(latest)}). This is not a live reading.", "The last preflight did not report headset Hz. Check SteamVR or your headset connection software.")
        Catch ex As IOException
            refreshLabel.Text = "Headset refresh report is unavailable. Check SteamVR or your headset connection software."
        Catch ex As UnauthorizedAccessException
            refreshLabel.Text = "Cannot read the previous headset report."
        End Try
    End Sub
    Private Sub BuildControlsTab()
        Dim content = TabLayout("Controls")
        inputLabel.Margin = New Padding(0, 0, 0, 12)
        content.Controls.Add(inputLabel)
        Dim grid As New TableLayoutPanel With {.ColumnCount = 3, .RowCount = 3, .Dock = DockStyle.Top, .AutoSize = True}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        For Each title In {"Action", "Keyboard", "Controller / wheel"}
            grid.Controls.Add(New Label With {.Text = title, .AutoSize = True, .Font = New Font(Font, FontStyle.Bold), .Margin = New Padding(3, 0, 3, 10)})
        Next
        For action = 0 To 1
            Dim selectedAction = action
            grid.Controls.Add(New Label With {.Text = If(action = 0, "Toggle VR", "Recenter"), .AutoSize = True, .Margin = New Padding(3, 9, 3, 0)}, 0, action + 1)
            Dim keyButton = If(action = 0, toggleButton, recenterButton)
            keyButton.AccessibleName = If(action = 0, "Toggle VR keyboard binding", "Recenter keyboard binding")
            AddHandler keyButton.Click, Sub() BeginKeyCapture(selectedAction)
            grid.Controls.Add(keyButton, 1, action + 1)
            Dim cell As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 1, .Margin = New Padding(3, 3, 0, 14)}
            Dim list = bindingLists(action)
            list.Dock = DockStyle.Fill : list.Height = 78 : list.IntegralHeight = False : list.HorizontalScrollbar = True
            list.AccessibleName = If(action = 0, "Toggle VR controller bindings", "Recenter controller bindings")
            cell.Controls.Add(list)
            Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
            Dim bind As New Button With {.Text = "Bind…", .AutoSize = True}
            Dim remove As New Button With {.Text = "Remove selected", .AutoSize = True}
            AddHandler bind.Click, Sub() BeginControllerCapture(selectedAction)
            AddHandler remove.Click, Sub()
                                         If busy OrElse list.SelectedIndex < 0 Then Return
                                         Dim assignments = settings.Bindings.Where(Function(b) b.Action = selectedAction).ToArray()
                                         settings.Bindings.Remove(assignments(list.SelectedIndex)) : RefreshBindings()
                                     End Sub
            buttons.Controls.AddRange({bind, remove}) : cell.Controls.Add(buttons)
            grid.Controls.Add(cell, 2, action + 1)
        Next
        content.Controls.Add(grid)
        content.Controls.Add(Note("Click a keyboard binding to assign a key with optional Ctrl/Alt/Shift. Use Bind… for one controller button or a two-button combination, then release. Escape cancels. Multiple devices can be assigned to an action."))
        content.Controls.Add(Note("Controller buttons still perform their normal game actions. Avoid driving/menu conflicts. Disconnected assignments are kept; Xbox slot changes may require rebinding."))
        content.Controls.Add(Note("Save settings to keep changes. Bindings apply on the next launch."))
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
        settings.RenderScale = CInt(renderScale.Value) : settings.HeadsetScale = CInt(headsetScale.Value)
        settings.FieldOfView = CInt(fieldOfView.Value) : settings.Mirrors = {"game", "on", "off"}(mirrors.SelectedIndex)
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
        toggleButton.Text = If(keyboardCapture = 0, "Press a key…", KeyLabel(settings.ToggleKey, settings.ToggleModifiers))
        recenterButton.Text = If(keyboardCapture = 1, "Press a key…", KeyLabel(settings.RecenterKey, settings.RecenterModifiers))
        Dim devices = input.Snapshot()
        For action = 0 To 1
            Dim selectedAction = action
            Dim list = bindingLists(action)
            Dim rows = settings.Bindings.Where(Function(b) b.Action = selectedAction).Select(Function(binding)
                Dim connected = devices.Any(Function(s) s.Source = binding.Source AndAlso s.Device = binding.Device AndAlso s.Connected)
                Return binding.Label & " — " & String.Join(" + ", binding.Buttons.Select(Function(b) ControllerNames.ButtonName(binding.Source, b))) & If(connected, "", " [disconnected / not detected]")
            End Function).ToArray()
            If list.Items.Cast(Of String)().SequenceEqual(rows) Then Continue For
            Dim selected = list.SelectedIndex
            list.BeginUpdate() : list.Items.Clear() : list.Items.AddRange(rows) : list.EndUpdate()
            If selected >= 0 AndAlso selected < list.Items.Count Then list.SelectedIndex = selected
        Next
    End Sub
    Private Shared Function KeyLabel(key As Integer, modifiers As Integer) As String
        Return If((modifiers And 1) <> 0, "Ctrl+", "") & If((modifiers And 2) <> 0, "Alt+", "") & If((modifiers And 4) <> 0, "Shift+", "") & CType(key, Keys).ToString()
    End Function
    Private Sub BeginKeyCapture(action As Integer)
        If busy Then Return
        keyboardCapture = action : controllerCapture = -1
        inputLabel.Text = "Press a key with optional Ctrl/Alt/Shift. Escape cancels."
        RefreshBindings()
    End Sub
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Escape Then
            keyboardCapture = -1 : controllerCapture = -1 : capturedDevice = Nothing
            inputLabel.Text = "Binding cancelled." : RefreshBindings() : e.SuppressKeyPress = True : Return
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
        RefreshBindings()
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
            tabs.Enabled = Not busy
            Dim currentStatus = If(status Is Nothing, "", status.State & status.UpdatedUtc.ToString("O"))
            If currentStatus <> lastStatus Then
                lastStatus = currentStatus : RefreshDisplayRate()
            End If
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
