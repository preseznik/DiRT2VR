Imports System.Windows.Forms
Imports System.Drawing
Imports System.Threading.Tasks

Public Class MainForm
    Inherits LauncherForm
    Private ReadOnly settingsTips As New ToolTip With {.InitialDelay = 450, .ReshowDelay = 150, .AutoPopDelay = 15000, .ShowAlways = True}
    Private ReadOnly context As InstallContext
    Private settings As VrSettings
    Private windowReady As Boolean
    Private lastWindowState As FormWindowState = FormWindowState.Normal
    Private ReadOnly modernInterface As New AppearanceSwitch()
    Private ReadOnly bindingCells(SeatActions.Names.Length - 1) As ControllerBindingCell
    Private replacementBinding As ControllerBinding
    Private ReadOnly runtimeBox As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly flashback As New CheckBox With {.Text = "On (Experimental)", .Name = "ExperimentalFlashback", .AccessibleName = "Frame-rate-independent rewind (Experimental)", .AutoSize = True}
    Private ReadOnly logging As New CheckBox With {.Text = "Enable diagnostic logging", .Name = "LoggingEnabled", .AutoSize = True}
    Private ReadOnly chaseFreeLook As New CheckBox With {.Text = "On (Experimental)", .Name = "ChaseFreeLook", .AutoSize = True}
    Private ReadOnly chaseInvert As New CheckBox With {.Text = "On", .Name = "ChaseInvertVertical", .AutoSize = True}
    Private ReadOnly chaseMouse As New ValueSlider("ChaseMouseSensitivity", 25, 300, 100, "%")
    Private ReadOnly chaseMouseMode As New MouseLookPicker()
    Private ReadOnly chaseStick As New ValueSlider("ChaseStickSensitivity", 25, 300, 100, "%")
    Private ReadOnly skipIntroduction As New CheckBox With {.Text = "Skip introduction for LAN multiplayer", .Name = "SkipIntroduction", .AutoSize = True}
    Private ReadOnly skipStartupMovies As New CheckBox With {.Text = "Skip startup logo movies (single-player launches)", .Name = "SkipStartupMovies", .AutoSize = True}
    Private ReadOnly serverList As New LanServerList With {.Name = "LanServers", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .Dock = DockStyle.Top, .Height = 300, .ShowItemToolTips = True}
    Private ReadOnly hostButton As New Button With {.Name = "HostLAN", .Text = "HOST", .AutoSize = True}
    Private ReadOnly joinButton As New Button With {.Name = "JoinLAN", .Text = "JOIN", .AutoSize = True, .Enabled = False}
    Private ReadOnly refreshServers As New Button With {.Name = "RefreshLAN", .Text = "Refresh", .AutoSize = True}
    Private ReadOnly browserStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0), .Text = "Open Multiplayer to find LAN hosts."}
    Private scanning As Boolean
    Private nextScan As DateTime
    Private ReadOnly stateLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly inputLabel As New Label With {.AutoSize = False, .AutoEllipsis = True, .Name = "InputStatus"}
    Private ReadOnly bindingLists As ListBox() = Enumerable.Range(0, SeatActions.Names.Length).Select(Function(i) New ListBox()).ToArray()
    Private ReadOnly tabs As New ModernTabs With {.Dock = DockStyle.Fill, .Name = "LauncherTabs"}
    Private ReadOnly launchMode As ComboBox = Choice("LaunchMode")
    Private ReadOnly eventChoice As ComboBox = Choice("PracticeEvent")
    Private ReadOnly trackChoice As ComboBox = Choice("PracticeTrack")
    Private ReadOnly carChoice As ComboBox = Choice("PracticeCar")
    Private ReadOnly opponents As New ValueSlider("Opponents", 1, 7, 7) With {.AccessibleName = "AI opponents"}
    Private ReadOnly laps As New ValueSlider("Laps", 1, 20, 1) With {.AccessibleName = "Laps"}
    Private ReadOnly borderless As New CheckBox With {.Text = "Borderless fullscreen (desktop only)", .Name = "BorderlessDesktop", .AutoSize = True}
    Private ReadOnly desktopVSync As New CheckBox With {.Text = "On", .Name = "DesktopVSync", .AutoSize = True}
    Private ReadOnly desktopBloom As New CheckBox With {.Text = "On", .Name = "DesktopBloom", .AutoSize = True}
    Private ReadOnly vrBloom As New CheckBox With {.Text = "On", .Name = "VrBloom", .AutoSize = True}
    Private ReadOnly renderScale As New ValueSlider("RenderScale", 50, 300, 100, "%", recommendedValue:=150)
    Private ReadOnly headsetScale As New ValueSlider("HeadsetScale", 25, 100, 100, "%")
    Private ReadOnly msaa As New ValueSlider("VrMsaa", 0, 3, 1, valueLabels:={"Off", "2×", "4×", "8×"})
    Private ReadOnly extendedViews As New CheckBox With {.Text = "On (Experimental)", .Name = "VrExtendedViews", .AccessibleName = "3D beyond the cockpit (Experimental)", .AutoSize = True}
    Private ReadOnly vrShadows As New CheckBox With {.Text = "On", .Name = "VrShadows", .AutoSize = True}
    Private ReadOnly steeringObserve As New CheckBox With {.Text = "Observe only — no correction", .Name = "SteeringObserveOnly", .AutoSize = True}
    Private ReadOnly steeringAnimation As New CheckBox With {.Text = "On (Experimental)", .Name = "VrSteeringAnimation", .AccessibleName = "Remove artificial steering corrections (Experimental)", .AutoSize = True}
    Private ReadOnly msaaWarning As New Label With {.Name = "MsaaWarning", .AutoSize = False}
    Private ReadOnly fieldOfView As New ValueSlider("FieldOfView", 70, 100, 100, "%")
    Private ReadOnly opponentCars As ComboBox = Choice("OpponentCars")
    Private ReadOnly raceDifficulty As ComboBox = Choice("RaceDifficulty")
    Private ReadOnly opponentHint As New Label With {.AutoSize = False, .MinimumSize = New Size(0, 44), .MaximumSize = New Size(710, 0)}
    Private ReadOnly mirrors As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Top, .DropDownWidth = 230, .Name = "Mirrors"}
    Private ReadOnly hudFollow As New CheckBox With {.Text = "HUD follows view", .Name = "HudFollowView", .AutoSize = True}
    Private ReadOnly hudDistance As New ValueSlider("HudDistance", 2, 40, 2, " m", 2D)
    Private ReadOnly treeDetail As New ValueSlider("TreeDetail", 0, 5, 0, valueLabels:={"Game", "Ultra low", "Low", "Medium", "High", "Ultra"})
    Private ReadOnly objectDetail As New ValueSlider("ObjectDetail", 0, 5, 0, valueLabels:={"Game", "Ultra low", "Low", "Medium", "High", "Ultra"})
    Private ReadOnly hudGauges As New CheckBox With {.Text = "Speedometer / gear / revs", .Name = "HudGauges", .AutoSize = True}
    Private ReadOnly hudLapTime As New CheckBox With {.Text = "Lap / time", .Name = "HudLapTime", .AutoSize = True}
    Private ReadOnly hudPosition As New CheckBox With {.Text = "Race position", .Name = "HudPosition", .AutoSize = True}
    Private ReadOnly hudMap As New CheckBox With {.Text = "Route map", .Name = "HudMap", .AutoSize = True}
    Private ReadOnly hudProgress As New CheckBox With {.Text = "Stage progress bar", .Name = "HudProgress", .AutoSize = True}
    Private ReadOnly refreshLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0)}
    ' SettingRow owns these readout bounds; AutoSize would reflow every sibling on text changes.
    Private ReadOnly requestedResolution As New Label With {.Name = "RequestedResolution", .AutoSize = False}
    Private ReadOnly actualResolution As New Label With {.Name = "ActualResolution", .AutoSize = True}
    Private nextResolutionRefresh As DateTime
    Private lastStatus As String = ""
    Private ReadOnly toggleButton As New Button With {.AutoSize = True}
    Private ReadOnly recenterButton As New Button With {.AutoSize = True}
    Private ReadOnly desktopButton As New Button With {.Text = "Launch", .AutoSize = True, .Name = "LaunchDesktop"}
    Private ReadOnly launchButton As New Button With {.Text = "Launch VR", .AutoSize = True, .Name = "LaunchVR"}
    Private ReadOnly saveButton As New Button With {.Text = "Save settings", .AutoSize = True, .Name = "SaveSettings"}
    Private ReadOnly recoverButton As New Button With {.Text = "Restore original files", .AutoSize = True}
    Private ReadOnly input As ControllerInput
    Private ReadOnly timer As New System.Windows.Forms.Timer With {.Interval = 100}
    Private ReadOnly inputTimer As New System.Windows.Forms.Timer With {.Interval = 20}
    Private keyboardCapture As Integer = -1
    Private controllerCapture As Integer = -1
    Private capturedDevice As ControllerSample
    Private buttonCapture As ControllerCapture
    Private busy As Boolean
    Private customTracks As CustomTrackPanel
    Private filters As FilterPanel
    Private ReadOnly updateNotice As New Button With {.Text = "New version available", .Name = "UpdateAvailable", .AutoSize = True, .Visible = False, .Anchor = AnchorStyles.Right}
    Private ReadOnly updateCancellation As New CancellationTokenSource()
    Private ReadOnly checkForUpdate As Func(Of CancellationToken, Task(Of ReleaseUpdate))
    Private availableUpdate As ReleaseUpdate
    Private updateGeneration As Integer
    Public Sub New(value As InstallContext, Optional releaseCheck As Func(Of CancellationToken, Task(Of ReleaseUpdate)) = Nothing)
        context = value
        input = New ControllerInput(context)
        checkForUpdate = If(releaseCheck, AddressOf CheckReleaseAsync)
        settings = VrSettings.Load(context)
        LauncherAppearance.Modern = settings.ModernInterface
        Text = "DiRT2VR" & If(BuildInfo.Channel = "Experimental", " — Experimental", "")
        Using stream = GetType(MainForm).Assembly.GetManifestResourceStream("DiRT2VR.ico"), appIcon As New Icon(stream)
            Icon = DirectCast(appIcon.Clone(), Icon)
        End Using
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        MinimumSize = New Size(560, 540)
        ClientSize = New Size(900, 960)
        StartPosition = FormStartPosition.CenterScreen
        KeyPreview = True
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(20), .ColumnCount = 1, .RowCount = 4}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim header As New TableLayoutPanel With {.ColumnCount = 3, .Dock = DockStyle.Fill, .AutoSize = True}
        header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100)) : header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize)) : header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        header.Controls.Add(New Label With {.Text = "DiRT 2 VR", .Font = New Font(Font.FontFamily, 20, FontStyle.Bold), .AutoSize = True}, 0, 0)
        Dim help As New Button With {.Text = "?", .Name = "HelpAbout", .AccessibleName = "Help / About", .Size = New Size(36, 36), .Anchor = AnchorStyles.Right}
        AddHandler help.Click, Sub() ShowAbout()
        AddHandler updateNotice.Click, Sub() ShowAbout()
        header.Controls.Add(updateNotice, 1, 0) : header.Controls.Add(help, 2, 0) : layout.Controls.Add(header)
        AddHandler HelpRequested, Sub(sender, e)
                                     e.Handled = True : ShowAbout()
                                 End Sub
        stateLabel.Margin = New Padding(0, 8, 0, 16)
        layout.Controls.Add(stateLabel)
        layout.Controls.Add(tabs)
        BuildLaunchTab()
        BuildMultiplayerTab()
        Dim profiles As New ProfilePanel(context)
        Dim profileContent = TabLayout("Profile")
        profileContent.Controls.Add(profiles)
        AddHandler profileContent.Parent.Enter, Sub() profiles.Reload()
        BuildGraphicsTab()
        BuildControlsTab()
        BuildSettingsTab()
        BuildAdvancedTab()
        AddHandler tabs.SelectedIndexChanged, Sub()
                                                  keyboardCapture = -1 : controllerCapture = -1 : capturedDevice = Nothing
                                                  inputLabel.Text = "Select a binding to change it."
                                                  RefreshBindings()
                                                  desktopButton.Visible = tabs.SelectedIndex <> 1
                                                  launchButton.Visible = tabs.SelectedIndex <> 1
                                              End Sub
        Dim logs As New Button With {.Text = "Open logs", .AutoSize = True, .Name = "OpenLogs"}
        Dim commands As New LauncherFooter(desktopButton, launchButton, saveButton, recoverButton, logs)
        AddHandler desktopButton.Click, Sub() SafeAction(Sub()
                                                            SaveSettings()
                                                            Spawn("--launch", "--desktop", "--no-ui")
                                                        End Sub)
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
        ConfigureTooltips()
        AddHandler Disposed, Sub() settingsTips.Dispose()
        AddHandler SizeChanged, Sub() stateLabel.MaximumSize = New Size(Math.Max(1, ClientSize.Width - layout.Padding.Horizontal), 0)
        AddHandler Resize, Sub()
                               If windowReady AndAlso WindowState <> FormWindowState.Minimized Then lastWindowState = WindowState
                           End Sub
        RefreshBindings() : RefreshDisplayRate()
        AddHandler input.StateChanged, AddressOf OnController
        AddHandler inputTimer.Tick, Sub() input.Poll()
        AddHandler timer.Tick, Sub()
                                  RefreshStatus()
                                  If tabs.SelectedIndex = 1 AndAlso Not busy AndAlso Not scanning AndAlso DateTime.UtcNow >= nextScan Then RefreshServerList()
                              End Sub
        AddHandler FormClosed, Sub()
                                  updateCancellation.Cancel() : updateCancellation.Dispose()
                                  timer.Stop() : timer.Dispose() : inputTimer.Stop() : inputTimer.Dispose() : input.Dispose()
                                  Icon.Dispose()
                              End Sub
        AddHandler FormClosing, Sub(sender, e)
                                    If customTracks.IsWorking Then
                                        customTracks.CancelOperation()
                                        e.Cancel = True
                                    End If
                                    If Not e.Cancel AndAlso windowReady Then
                                        Dim normal = If(WindowState = FormWindowState.Normal, Size, RestoreBounds.Size)
                                        WindowPreferences.Capture(normal, DeviceDpi, lastWindowState = FormWindowState.Maximized).Save(context)
                                    End If
                                End Sub
        inputTimer.Start() : timer.Start() : RefreshStatus()
        AddHandler Shown, Sub() FitInitialWindow()
        AddHandler Shown, Async Sub() Await CheckStartupUpdate()
    End Sub
    Private Sub ConfigureTooltips()
        Tip(flashback, "Keeps more rewind history when playing at high FPS, without slowing the game." & vbCrLf & "Single-player desktop and VR only. Experimental; off by default.")
        Tip(renderScale, "How much detail the game draws for each eye. The green notch marks 150% recommended. Higher can be sharper," & vbCrLf &
            "but needs more GPU power and memory. A low Headset texture scale" & vbCrLf & "can still make the final picture look soft.")
        Tip(headsetScale, "Size of the finished picture sent to your headset." & vbCrLf &
            "Lower uses less memory but can blur the view. 100% keeps the" & vbCrLf &
            "size SteamVR recommends. This is not car or road texture quality.")
        Tip(msaa, "Smooths jagged edges in VR. Higher settings use more memory" & vbCrLf & "and GPU power; 4× and 8× can cause crashes. 2× is the default.")
        Tip(desktopBloom, "Soft glow around bright areas. On keeps the game's effects;" & vbCrLf & "Off removes the glow. Save and relaunch desktop play to apply.")
        Tip(vrBloom, "Soft glow around bright areas. On keeps the game's effects;" & vbCrLf & "Off removes the glow in VR, including its virtual screen." & vbCrLf & "Save and relaunch VR to apply. This is separate from shadows.")
        Tip(fieldOfView, "Lower values trim the edges of your VR view to reduce rendering" & vbCrLf & "work. 100% keeps the full view; objects keep their normal scale.")
        Tip(mirrors, "Turn the car's rear-view mirrors on or off in VR." & vbCrLf & "Off can improve performance. Game setting keeps your usual choice.")
        Tip(extendedViews, "Use headset 3D and head tracking in supported cameras outside the cockpit." & vbCrLf & "First stage: bonnet, bumper and chase views. Menus and replays stay on the virtual screen." & vbCrLf & "Off by default. Save and relaunch VR to apply. Toggle VR always returns to the flat screen.")
        Tip(vrShadows, "Experimental: enable shadows in VR using the game's shadow quality." & vbCrLf & "Off keeps the current faster rendering. Shadows may still disagree between eyes." & vbCrLf & "Applies on next VR launch; desktop play is unchanged.")
        Tip(treeDetail, "Higher keeps detailed vegetation visible farther away, but" & vbCrLf & "costs performance. Game keeps your usual setting.")
        Tip(objectDetail, "Higher keeps detailed buildings and trackside objects farther" & vbCrLf & "away, but costs performance. Game keeps your usual setting.")
        Tip(borderless, "Fill the main monitor without window borders during desktop" & vbCrLf & "play. Alt+Tab still works. This does not affect VR or its mirror.")
        Tip(desktopVSync, "Stops horizontal tearing by matching desktop frames to your" & vbCrLf & "monitor. Off allows uncapped FPS. This does not affect VR timing.")
        Tip(hudFollow, "On: the HUD follows where you look. Off: it stays in front" & vbCrLf & "of the car while you turn your head.")
        Tip(hudDistance, "How far away the floating HUD appears in VR." & vbCrLf & "The text stays the same apparent size so it remains readable.")
        Tip(hudGauges, "Show speed, gear and revs on the floating VR HUD.")
        Tip(hudLapTime, "Show lap and timing information on the VR HUD.")
        Tip(hudPosition, "Show your race position on the VR HUD.")
        Tip(hudMap, "Show the route map on the VR HUD.")
        Tip(hudProgress, "Show how far through the stage you are on the VR HUD.")
        Tip(requestedResolution, "The scene size your current settings will request next launch." & vbCrLf & "The last-launch report shows what was actually used.")
        Tip(actualResolution, "Measured sizes from your last VR launch, not a live preview" & vbCrLf & "of unsaved changes. Game is the scene; headset is the sent image.")
        Tip(refreshLabel, "How often the headset updates its picture. Change this in" & vbCrLf & "SteamVR or your headset software. This is the last reported rate.")
        Tip(launchMode, "Normal Launch opens the game menus. Direct practice starts" & vbCrLf & "a solo event. Race adds computer-controlled opponents.")
        Tip(eventChoice, "Choose a driving discipline to filter the track list." & vbCrLf & "Direct launches do not start a career event.")
        Tip(trackChoice, "The course used by Direct practice or Race.")
        Tip(carChoice, "Your car for Direct practice or Race.")
        Tip(opponentCars, "Same as driver: matching cars. Mixed: any installed class." & vbCrLf & "Same class: cars from your chosen car's class. Race mode only.")
        Tip(opponents, "How many computer-controlled cars race against you." & vbCrLf & "Used in Race mode; Direct practice is solo.")
        Tip(laps, "Number of laps for circuit tracks in Direct practice or Race." & vbCrLf & "Point-to-point stages always run once.")
        Tip(runtimeBox.Parent, "SteamVR's connection to the headset. Normally detected for you;" & vbCrLf & "browse only if your SteamVR installation is elsewhere.")
        Tip(skipStartupMovies, "Skip startup logos in single-player launches." & vbCrLf & "LAN keeps them to avoid multiplayer disconnects.")
        Tip(skipIntroduction, "Skip the opening movie and forced first race when launching" & vbCrLf & "LAN. Profile creation still works; normal play is unchanged.")
        Tip(logging, "Save diagnostic files to help troubleshoot a problem." & vbCrLf & "Leave off for normal play to avoid extra disk usage.")
        Tip(toggleButton, "Choose a keyboard shortcut to switch between cockpit VR" & vbCrLf & "and the flat virtual screen.")
        Tip(recenterButton, "Choose a keyboard shortcut to reset your seated VR position." & vbCrLf & "Sit comfortably and face forward before using it in the game.")
        Tip(saveButton, "Keep these choices for your next launch, including quick launch." & vbCrLf & "This does not change a race that is already running.")
        Tip(recoverButton, "Restore original game files after an interrupted session." & vbCrLf & "Close DiRT 2 first. Your career is not reset.")
        Tip(Controls.Find("GraphicsDefaults", True).Single(), "Reset the Graphics tab to its defaults, then save." & vbCrLf & "Your driving bindings and career are unchanged.")
        Tip(Controls.Find("DrivingControls", True).Single(), "Set steering, pedals, gears and other driving controls," & vbCrLf & "or use the guided binding wizard.")
        Tip(hostButton, "Start LAN play, then create a lobby in the game's LAN menu." & vbCrLf & "You can choose desktop or VR before launching.")
        Tip(joinButton, "Start LAN play aimed at the selected host, then join through" & vbCrLf & "the game's LAN menu. You can choose desktop or VR.")
        Tip(refreshServers, "Look again for hosts on your local network.")
    End Sub
    Private Sub Tip(control As Control, description As String)
        ' Native sliders are child controls. Cover the caption, track and value,
        ' so users do not have to hunt for a small hover target.
        Dim target = If(TypeOf control.Parent Is SettingRow, control.Parent, control)
        AttachTip(target, description)
    End Sub
    Private Sub AttachTip(control As Control, description As String)
        settingsTips.SetToolTip(control, description)
        For Each child As Control In control.Controls
            AttachTip(child, description)
        Next
    End Sub
    Private Async Function CheckReleaseAsync(token As CancellationToken) As Task(Of ReleaseUpdate)
        Using client = UpdateService.CreateClient()
            Return Await New UpdateService(client).CheckAsync(BuildInfo.Version, UpdatePreferences.Load(context).IncludeExperimentalReleases, token)
        End Using
    End Function
    Private Async Function CheckStartupUpdate() As Task
        Dim token = updateCancellation.Token
        updateGeneration += 1
        Dim generation = updateGeneration
        Try
            Dim result = Await checkForUpdate(token)
            If IsDisposed OrElse token.IsCancellationRequested OrElse generation <> updateGeneration Then Return
            If result IsNot Nothing AndAlso result.IsExperimental AndAlso Not UpdatePreferences.Load(context).IncludeExperimentalReleases Then Return
            availableUpdate = result
            updateNotice.Text = If(result?.IsExperimental, "Experimental update available", "New version available")
            updateNotice.Visible = result IsNot Nothing
            If result IsNot Nothing Then updateNotice.AccessibleDescription = "Version " & result.Version.Text & " is available. Open Help / About to review and install."
        Catch ex As Exception
            ' Offline/rate-limited startup checks are quiet; About offers a manual retry.
        End Try
    End Function
    Private Shared Function Choice(name As String) As ComboBox
        Return New ComboBox With {.Name = name, .AccessibleName = name, .DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill, .DropDownWidth = 600}
    End Function
    Private Function TabLayout(title As String) As VerticalStack
        Dim page As New TabPage(title) With {.Padding = New Padding(16), .UseVisualStyleBackColor = False, .BackColor = BackColor, .AutoScroll = True}
        Dim content = Stack()
        page.Controls.Add(content) : tabs.TabPages.Add(page)
        AddHandler page.SizeChanged, Sub()
                                         Dim inset = Math.Max(Px(Me, 16), (page.ClientSize.Width - Px(Me, 1360)) \ 2)
                                         page.Padding = New Padding(inset, Px(Me, 12), inset, Px(Me, 12))
                                     End Sub
        AddHandler content.SizeChanged, Sub() FitLabels(content)
        Return content
    End Function
    Private Shared Sub FitLabels(parent As Control)
        For Each child As Control In parent.Controls
            If TypeOf child Is Label AndAlso child.AutoSize Then child.MaximumSize = New Size(Math.Max(1, parent.ClientSize.Width - child.Margin.Horizontal), 0)
            If Not TypeOf child Is SettingRow Then FitLabels(child)
        Next
    End Sub
    Private Sub FitInitialWindow()
        Dim work = Screen.FromControl(Me).WorkingArea
        Dim saved = WindowPreferences.Load(context)
        MinimumSize = New Size(Math.Min(Px(Me, 560), work.Width), Math.Min(Px(Me, 540), work.Height))
        Size = saved.Fit(work.Size, DeviceDpi, Size)
        Location = New Point(work.Left + (work.Width - Width) \ 2, work.Top + (work.Height - Height) \ 2)
        windowReady = True
        If saved.Maximized Then WindowState = FormWindowState.Maximized
    End Sub
    Private Sub BuildMultiplayerTab()
        Dim content = TabLayout("Multiplayer")
        content.Controls.Add(New Label With {.Text = "LAN servers", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        serverList.Columns.Add("Host", 240) : serverList.Columns.Add("Address", 170)
        serverList.Columns.Add("Players", 85) : serverList.Columns.Add("Status", 180)
        serverList.BackColor = BackColor : serverList.ForeColor = ForeColor
        content.Controls.Add(serverList)
        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True}
        buttons.Controls.AddRange({hostButton, joinButton, refreshServers}) : content.Controls.Add(buttons)
        content.Controls.Add(browserStatus)
        content.Controls.Add(HelpLink(Sub() ShowAbout("Multiplayer")))
        Dim page = DirectCast(content.Parent, TabPage)
        AddHandler page.SizeChanged, Sub() serverList.Height = Math.Max(Px(Me, 160), page.ClientSize.Height - Px(Me, 180))
        AddHandler refreshServers.Click, Sub() RefreshServerList()
        AddHandler serverList.SelectedIndexChanged, Sub() joinButton.Enabled = Not busy AndAlso If(SelectedHost()?.Joinable, False)
        AddHandler hostButton.Click, Sub() SafeAction(Sub()
                                                         LaunchMultiplayer()
                                                     End Sub)
        AddHandler joinButton.Click, Sub() SafeAction(Sub()
                                                         Dim host = SelectedHost()
                                                         If host Is Nothing OrElse Not host.Joinable Then Throw New IOException("Refresh and select an available LAN host.")
                                                         LaunchMultiplayer(host.Endpoint.ToString())
                                                     End Sub)
    End Sub
    Private Sub LaunchMultiplayer(Optional joinTarget As String = Nothing)
        Using choice As New LanLaunchForm(joinTarget IsNot Nothing)
            Dim result = choice.ShowDialog(Me)
            If result <> DialogResult.Yes AndAlso result <> DialogResult.No Then Return
            SaveSettings()
            Spawn(LanSession.LaunchArguments(result = DialogResult.Yes, joinTarget))
        End Using
    End Sub
    Private Function SelectedHost() As LanHost
        Return If(serverList.SelectedItems.Count = 1, TryCast(serverList.SelectedItems(0).Tag, LanHost), Nothing)
    End Function
    Private Async Sub RefreshServerList()
        If scanning OrElse busy OrElse IsDisposed Then Return
        scanning = True : refreshServers.Enabled = False : browserStatus.Text = "Searching LAN…"
        Dim previous = SelectedHost()?.Endpoint.ToString()
        Try
            Dim hosts = Await LanBrowser.ScanAsync(updateCancellation.Token)
            If IsDisposed Then Return
            serverList.BeginUpdate() : serverList.Items.Clear()
            For Each host In hosts
                Dim players = If(host.HostGame, "—", host.Players & "/" & host.Capacity)
                Dim status = If(host.HostGame, "HOST game running", If(host.Racing, "Racing", If(Not host.Joinable, "Full", If(host.Advertised, "Hosting", "Lobby"))))
                Dim row As New ListViewItem({host.Name, host.Endpoint.ToString(), players, status}) With {.Tag = host}
                row.ToolTipText = host.Name & Environment.NewLine & host.Endpoint.ToString() & Environment.NewLine & players & " players — " & status
                serverList.Items.Add(row)
                If host.Endpoint.ToString() = previous Then row.Selected = True
            Next
            serverList.EndUpdate()
            browserStatus.Text = If(hosts.Count = 0, "No LAN hosts found. Launch with HOST on the other PC, then refresh.", hosts.Count & " LAN host(s). Refreshes automatically while this tab is open.")
        Catch ex As OperationCanceledException
        Catch ex As Exception
            If Not IsDisposed Then
                serverList.Items.Clear() : browserStatus.Text = "Discovery failed: " & ex.Message
            End If
        Finally
            scanning = False : nextScan = DateTime.UtcNow.AddSeconds(4)
            If Not IsDisposed Then refreshServers.Enabled = Not busy
        End Try
    End Sub
    Private Sub BuildLaunchTab()
        Dim content = TabLayout("Launcher")
        customTracks = New CustomTrackPanel(context)
        content.Controls.Add(customTracks)
        Dim columns As New ResponsiveColumns()
        content.Controls.Add(columns)
        AddHandler customTracks.AvailabilityChanged, Sub()
                                                          If customTracks.CustomEnabled Then
                                                              content.Controls.Remove(columns)
                                                          ElseIf Not content.Controls.Contains(columns) Then
                                                              content.Controls.Add(columns) : content.Controls.SetChildIndex(columns, 1)
                                                          End If
                                                          RefreshOpponentHint()
                                                      End Sub
        If customTracks.CustomEnabled Then content.Controls.Remove(columns)
        Dim selection = Section(columns.First, "Event selection")
        Dim race = Section(columns.Second, "Race options")
        Dim labels = {"Launch mode", "Event", "Track", "Car", "Opponent cars"}
        Dim choices = {launchMode, eventChoice, trackChoice, carChoice, opponentCars}
        For i = 0 To choices.Length - 1
            StyleChoice(choices(i), Me)
            Field(If(i = 4, race, selection), labels(i), choices(i))
        Next
        StyleChoice(raceDifficulty, Me)
        raceDifficulty.Items.AddRange(DirectRaceDifficulty.Labels)
        raceDifficulty.SelectedIndex = settings.RaceDifficulty + 1
        Field(race, "Race difficulty", raceDifficulty)
        Tip(raceDifficulty, "How hard the computer drivers race. Uses DiRT 2's difficulty levels." & vbCrLf & "Use game setting keeps your saved choice. Race mode only.")
        opponents.Value = settings.Opponents : Field(race, "AI opponents", opponents)
        laps.Value = settings.Laps : Field(race, "Laps (circuits)", laps)
        AddHandler trackChoice.SelectedIndexChanged, Sub() RefreshLaps()
        launchMode.Items.AddRange({"Normal Launch", "Direct practice (experimental)", "Race (experimental)"})
        opponentCars.Items.AddRange({"Same as driver", "Mixed", "Same class"})
        opponentCars.SelectedIndex = Array.IndexOf({"same", "mixed", "class"}, settings.OpponentCars)
        AddHandler opponentCars.SelectedIndexChanged, Sub() RefreshOpponentHint()
        AddHandler carChoice.SelectedIndexChanged, Sub() RefreshOpponentHint()
        eventChoice.Items.AddRange(RaceCatalog.Current.Tracks.Where(Function(t) PrototypeTrack.Installed(t, context)).Select(Function(t) t.Event).Distinct().Order().Cast(Of Object).ToArray())
        carChoice.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
        AddHandler eventChoice.SelectedIndexChanged, Sub()
                                                        Dim previous = TryCast(trackChoice.SelectedItem, PracticeTrack)?.Id
                                                        trackChoice.Items.Clear()
                                                        trackChoice.Items.AddRange(RaceCatalog.Current.Tracks.Where(Function(t) t.Event = CStr(eventChoice.SelectedItem) AndAlso PrototypeTrack.Installed(t, context)).OrderBy(Function(t) t.Label).Cast(Of Object).ToArray())
                                                        trackChoice.SelectedItem = trackChoice.Items.Cast(Of PracticeTrack).FirstOrDefault(Function(t) t.Id = previous)
                                                        If trackChoice.SelectedIndex < 0 AndAlso trackChoice.Items.Count > 0 Then trackChoice.SelectedIndex = 0
                                                    End Sub
        eventChoice.SelectedItem = RaceCatalog.Current.Track(settings.TrackId).Event
        If eventChoice.SelectedIndex < 0 AndAlso eventChoice.Items.Count > 0 Then eventChoice.SelectedIndex = 0
        trackChoice.SelectedItem = trackChoice.Items.Cast(Of PracticeTrack).FirstOrDefault(Function(t) t.Id = settings.TrackId)
        If trackChoice.SelectedIndex < 0 AndAlso trackChoice.Items.Count > 0 Then trackChoice.SelectedIndex = 0
        carChoice.SelectedItem = carChoice.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) c.Code = settings.CarCode)
        If carChoice.SelectedIndex < 0 AndAlso carChoice.Items.Count > 0 Then carChoice.SelectedIndex = 0
        AddHandler launchMode.SelectedIndexChanged, Sub()
                                                        For Each control In {eventChoice, trackChoice, carChoice}
                                                            control.Enabled = launchMode.SelectedIndex = 1 OrElse launchMode.SelectedIndex = 2
                                                        Next
                                                        raceDifficulty.Enabled = launchMode.SelectedIndex = 2
                                                        opponents.Enabled = launchMode.SelectedIndex = 2
                                                        opponentCars.Enabled = launchMode.SelectedIndex = 2
                                                        RefreshOpponentHint()
                                                        RefreshLaps()
                                                    End Sub
        launchMode.SelectedIndex = Math.Max(0, Array.IndexOf({"menus", "practice", "race"}, settings.LaunchMode))
        content.Controls.Add(opponentHint)
        content.Controls.Add(HelpLink(Sub() ShowAbout(If(customTracks.CustomEnabled, "Custom tracks", "Getting started"))))
    End Sub
    Private Sub RefreshOpponentHint()
        RefreshLaunchAvailability()
        If customTracks IsNot Nothing AndAlso customTracks.CustomEnabled Then
            opponentHint.Text = ""
            Return
        End If
        Dim vehicle = TryCast(carChoice.SelectedItem, PracticeCar)
        opponentHint.Text = If(launchMode.SelectedIndex <> 2, "", If(opponentCars.SelectedIndex = 2, "Opponent class: " & If(vehicle?.ClassName, "Select a car"), If(opponentCars.SelectedIndex = 1, "Mixed: all installed classes.", "All opponents use the same car as the driver.")))
    End Sub
    Private Sub RefreshLaunchAvailability()
        Dim working = busy OrElse (customTracks IsNot Nothing AndAlso customTracks.IsWorking)
        desktopButton.Enabled = Not working AndAlso (customTracks Is Nothing OrElse Not customTracks.CustomEnabled OrElse customTracks.CanLaunch)
        launchButton.Enabled = desktopButton.Enabled AndAlso (customTracks Is Nothing OrElse Not customTracks.CustomEnabled OrElse customTracks.CanLaunchVr)
        saveButton.Enabled = Not working : recoverButton.Enabled = Not working
        hostButton.Enabled = Not working AndAlso (customTracks Is Nothing OrElse Not customTracks.CustomEnabled)
        joinButton.Enabled = hostButton.Enabled AndAlso If(SelectedHost()?.Joinable, False)
    End Sub
    Private Async Sub ShowAbout(Optional topic As String = Nothing)
        Dim changed As Boolean
        Using dialog As New AboutForm(context, availableUpdate)
            AddHandler dialog.UpdatePreferenceChanged, Sub()
                                                          changed = True
                                                          InvalidateUpdateOffer()
                                                      End Sub
            If topic IsNot Nothing Then dialog.ShowInstructions(topic)
            dialog.ShowDialog(Me)
        End Using
        If changed AndAlso Not IsDisposed Then Await CheckStartupUpdate()
    End Sub
    Private Sub InvalidateUpdateOffer()
        updateGeneration += 1
        availableUpdate = Nothing
        updateNotice.Visible = False
    End Sub
    Private Sub RefreshLaps()
        Dim track = TryCast(trackChoice.SelectedItem, PracticeTrack)
        laps.Enabled = (launchMode.SelectedIndex = 1 OrElse launchMode.SelectedIndex = 2) AndAlso track IsNot Nothing AndAlso track.Circuit
    End Sub
    Private Sub BuildSettingsTab()
        Dim content = TabLayout("Settings")
        Dim columns As New ResponsiveColumns() : content.Controls.Add(columns)
        Dim appearance = Section(columns.First, "Appearance")
        modernInterface.Checked = settings.ModernInterface
        Field(appearance, "Modern interface", modernInterface)
        Tip(modernInterface, "Choose the compact modern look or the original classic layout. Save settings to keep your choice.")
        AddHandler modernInterface.CheckedChanged, Sub()
                                                        CancelBindingCapture()
                                                        LauncherAppearance.Modern = modernInterface.Checked
                                                        LauncherAppearance.Apply(Me)
                                                        RefreshBindings()
                                                    End Sub
        Dim paths = Section(columns.First, "Locations")
        Field(paths, "Game folder", New TextBox With {.Text = context.GameRoot, .ReadOnly = True, .Name = "GameFolder"})
        Dim runtimeRow As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True}
        runtimeRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100)) : runtimeRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        runtimeBox.Text = settings.Runtime : runtimeBox.Name = "RuntimePath"
        runtimeRow.Controls.Add(runtimeBox)
        Dim browse As New Button With {.Text = "Browse…", .AutoSize = True}
        AddHandler browse.Click, Sub()
                                     Using dialog As New OpenFileDialog With {.Filter = "SteamVR x86 runtime|steamxr_win32.json", .FileName = "steamxr_win32.json"}
                                         If dialog.ShowDialog(Me) = DialogResult.OK Then runtimeBox.Text = dialog.FileName
                                     End Using
                                 End Sub
        runtimeRow.Controls.Add(browse) : Field(paths, "SteamVR runtime", runtimeRow)
        Dim options = Section(columns.Second, "Startup & diagnostics")
        skipStartupMovies.Text = "On" : skipStartupMovies.Checked = settings.SkipStartupMovies : Field(options, "Skip logos (single player)", skipStartupMovies)
        skipIntroduction.Text = "On" : skipIntroduction.Checked = settings.SkipIntroduction : Field(options, "Skip introduction (LAN)", skipIntroduction)
        logging.Text = "On" : logging.Checked = settings.LoggingEnabled : Field(options, "Diagnostic logging", logging)
        content.Controls.Add(HelpLink(Sub() ShowAbout("Getting started")))
    End Sub
    Private Sub BuildAdvancedTab()
        Dim content = TabLayout("Advanced")
        Dim gameplay = Section(content, "Gameplay")
        flashback.Checked = settings.ExperimentalFlashback
        Field(gameplay, "Frame-rate-independent rewind", flashback)
        Dim graphics = Section(content, "Graphics")
        extendedViews.Checked = settings.VrExtendedViews
        Field(graphics, "3D beyond the cockpit", extendedViews)
        content.Controls.Add(HelpLink(Sub() ShowAbout("Advanced")))
    End Sub
    Private Sub BuildGraphicsTab()
        Dim content = TabLayout("Graphics")
        Dim page = DirectCast(content.Parent, TabPage)
        page.Controls.Remove(content) : page.AutoScroll = False
        Dim graphicsTabs As New ModernTabs With {.Name = "GraphicsTabs", .Dock = DockStyle.Fill}
        Dim mainPage As New TabPage("Main") With {.AutoScroll = True, .UseVisualStyleBackColor = False}
        Dim filtersPage As New TabPage("Filters") With {.UseVisualStyleBackColor = False}
        mainPage.Controls.Add(content)
        filters = New FilterPanel(context, settings) : filtersPage.Controls.Add(filters)
        graphicsTabs.TabPages.AddRange({mainPage, filtersPage}) : page.Controls.Add(graphicsTabs)
        Dim columns As New ResponsiveColumns() : content.Controls.Add(columns)
        Dim desktop = Section(columns.First, "Desktop")
        borderless.Text = "On" : borderless.Checked = settings.BorderlessDesktop : Field(desktop, "Borderless fullscreen", borderless)
        desktopVSync.Checked = settings.DesktopVSync : Field(desktop, "VSync", desktopVSync)
        desktopBloom.Checked = settings.DesktopBloom : Field(desktop, "Bloom", desktopBloom)
        Dim render = Section(columns.First, "VR rendering")
        renderScale.Value = settings.RenderScale : headsetScale.Value = settings.HeadsetScale : fieldOfView.Value = settings.FieldOfView
        mirrors.Items.AddRange({"Game setting", "On", "Off"})
        mirrors.SelectedIndex = Array.IndexOf({"game", "on", "off"}, settings.Mirrors)
        StyleChoice(mirrors, Me)
        Field(render, "Render resolution", renderScale) : Field(render, "Headset texture scale", headsetScale)
        msaa.Value = Array.IndexOf({0, 2, 4, 8}, settings.VrMsaa)
        Field(render, "Anti-aliasing (MSAA)", msaa)
        AddHandler msaa.ValueChanged, Sub() RefreshMsaaWarning()
        RefreshMsaaWarning()
        Field(render, "Field of view", fieldOfView) : Field(render, "Car mirrors", mirrors)
        vrShadows.Checked = settings.VrShadows : Field(render, "Shadows (experimental)", vrShadows)
        vrBloom.Checked = settings.VrBloom : Field(render, "Bloom", vrBloom)
        treeDetail.Value = settings.TreeDetail : objectDetail.Value = settings.ObjectDetail
        Field(render, "Tree detail", treeDetail) : Field(render, "Object detail", objectDetail)
        Field(render, "Headset refresh rate", refreshLabel)
        Field(render, "Requested scene", requestedResolution)
        ' Keep the warning outside the adjustable rows so changing MSAA does not
        ' shift neighbouring sliders. No empty warning row between settings.
        msaaWarning.Height = Px(Me, 32) : msaaWarning.Margin = New Padding(0)
        render.Controls.Add(msaaWarning)
        Dim hud = Section(columns.Second, "VR HUD")
        hudDistance.Value = CInt(settings.HudDistance * 2D) : Field(hud, "Distance", hudDistance)
        hudFollow.Text = "On" : hudFollow.Checked = settings.HudFollowView : Field(hud, "Follow view", hudFollow)
        hudGauges.Text = "Gauges" : hudPosition.Text = "Position" : hudProgress.Text = "Progress"
        hudGauges.Checked = settings.HudGauges : hudLapTime.Checked = settings.HudLapTime : hudPosition.Checked = settings.HudPosition
        hudMap.Checked = settings.HudMap : hudProgress.Checked = settings.HudProgress
        Dim hudElements As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
        For Each element In {hudGauges, hudLapTime, hudPosition, hudMap, hudProgress}
            element.Margin = New Padding(0, 4, 16, 4) : hudElements.Controls.Add(element)
        Next
        Field(hud, "Show", hudElements)
        Dim defaults As New Button With {.Text = "Restore defaults", .Name = "GraphicsDefaults", .AutoSize = True}
        AddHandler defaults.Click, Sub()
                                       borderless.Checked = False : desktopVSync.Checked = True
                                       desktopBloom.Checked = True : vrBloom.Checked = True
                                       filters.ResetSelections()
                                       msaa.Value = 1 : vrShadows.Checked = False
                                       renderScale.Value = 100 : headsetScale.Value = 100 : fieldOfView.Value = 100 : mirrors.SelectedIndex = 0
                                       hudFollow.Checked = False : hudDistance.Value = 2
                                       treeDetail.Value = 0 : objectDetail.Value = 0 : hudGauges.Checked = False
                                       For Each element In {hudLapTime, hudPosition, hudMap, hudProgress}
                                           element.Checked = True
                                       Next
                                   End Sub
        BuildSeatSettings(columns.Second)
        columns.Second.Controls.Add(defaults)
        columns.Second.Controls.Add(HelpLink(Sub() ShowAbout("VR rendering")))
        columns.Second.Controls.Add(actualResolution)
        AddHandler renderScale.ValueChanged, Sub() RefreshGraphicsSummary()
        AddHandler headsetScale.ValueChanged, Sub() RefreshGraphicsSummary()
        AddHandler fieldOfView.ValueChanged, Sub() RefreshGraphicsSummary()
        RefreshGraphicsSummary()
        RefreshResolutionReport()
    End Sub
    Private Sub RefreshGraphicsSummary()
        Dim preview As New VrSettings With {.RenderScale = CInt(renderScale.Value), .FieldOfView = CInt(fieldOfView.Value)}
        Dim pixels = preview.RenderWidth * CDbl(preview.RenderHeight) / (1600 * 1200)
        requestedResolution.Text = $"{preview.RenderWidth} × {preview.RenderHeight} per eye ({pixels:P0} of default pixels)"
        renderScale.AccessibleDescription = "Requested scene: " & requestedResolution.Text & $". Headset texture: {headsetScale.Value}% of recommended width and height."
    End Sub
    Private Sub RefreshResolutionReport()
        Dim description = "Actual resolution: not reported yet. Start a VR session to measure it."
        Try
            Dim path = IO.Path.Combine(context.UserRoot, "resolution.json")
            If File.Exists(path) Then description = If(Files.ReadJson(Of ResolutionStatus)(path)?.Description(), "Resolution report unavailable.")
        Catch ex As IOException
            description = "Resolution report unavailable."
        Catch ex As UnauthorizedAccessException
            description = "Resolution report unavailable."
        Catch ex As System.Text.Json.JsonException
            description = "Resolution report unavailable."
        End Try
        ' Avoid laying out both the placeholder and the report on every timer tick.
        If actualResolution.Text <> description Then actualResolution.Text = description
    End Sub
    Private Sub RefreshMsaaWarning()
        msaaWarning.Text = If(msaa.Value >= 2, If(msaa.Value = 3, "8×: very high memory cost; may cause VR crashes.", "4×: higher memory cost; reduce if VR is unstable."), "")
    End Sub
    Private Sub RefreshDisplayRate()
        refreshLabel.Text = "SteamVR controlled"
        Try
            Dim summaryPath = IO.Path.Combine(context.UserRoot, "headset.json")
            If File.Exists(summaryPath) Then
                Dim summary = Files.ReadJson(Of HeadsetStatus)(summaryPath)
                refreshLabel.Text = If(summary.RefreshHz <> "", $"{summary.RefreshHz} Hz (last launch)", "SteamVR controlled")
                Return
            End If
            Dim logs = IO.Path.Combine(context.UserRoot, "logs")
            If Not Directory.Exists(logs) Then Return
            Dim latest = Directory.GetDirectories(logs).OrderByDescending(Function(path) IO.Path.GetFileName(path)).FirstOrDefault()
            If latest Is Nothing Then Return
            Dim report = IO.Path.Combine(latest, "preflight.txt")
            If Not File.Exists(report) Then Return
            Dim match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(report), "(?m)^display_refresh_hz=([0-9.]+)")
            refreshLabel.Text = If(match.Success, $"{match.Groups(1).Value} Hz (last launch)", "SteamVR controlled")
        Catch ex As IOException
            refreshLabel.Text = "Headset refresh report is unavailable. Check SteamVR or your headset connection software."
        Catch ex As UnauthorizedAccessException
            refreshLabel.Text = "Cannot read the previous headset report."
        End Try
    End Sub
    Private Sub BuildControlsTab()
        Dim content = TabLayout("Controls")
        Dim page = DirectCast(content.Parent, TabPage)
        page.Controls.Remove(content) : content.Dispose() : page.AutoScroll = False
        Dim controlTabs As New ModernTabs With {.Name = "ControlsTabs", .Dock = DockStyle.Fill}
        page.Controls.Add(controlTabs)
        Dim general = ControlsPage(controlTabs, "General")
        Dim vr = ControlsPage(controlTabs, "VR")
        Dim drivingContent = ControlsPage(controlTabs, "Driving")
        Dim seat = ControlsPage(controlTabs, "Seat")
        AddHandler controlTabs.SelectedIndexChanged, Sub()
                                                        CancelBindingCapture()
                                                        inputLabel.Text = "Select a binding to change it."
                                                        RefreshBindings()
                                                    End Sub
        content = vr
        inputLabel.Height = Px(Me, 36)
        inputLabel.Margin = New Padding(0, 0, 0, 6)
        content.Controls.Add(New Label With {.Text = "VR shortcuts", .Font = New Font(Font, FontStyle.Bold), .AutoSize = True})
        content.Controls.Add(inputLabel)
        content.Controls.Add(New BindingColumns())
        Dim seatStatus As New Label With {.Text = inputLabel.Text, .AutoSize = True}
        AddHandler inputLabel.TextChanged, Sub() seatStatus.Text = inputLabel.Text
        seat.Controls.Add(seatStatus)
        seat.Controls.Add(New BindingColumns())

        Dim individualSeats As New CollapsibleSection("Individual seat bindings (optional)") With {.Name = "IndividualSeatBindings"}
        AddHandler individualSeats.Collapsed, Sub()
                                                 If keyboardCapture >= 3 OrElse controllerCapture >= 3 Then CancelBindingCapture()
                                                 If controllerCapture >= 3 AndAlso controllerCapture <= 11 Then
                                                     controllerCapture = -1 : capturedDevice = Nothing
                                                 End If
                                                 inputLabel.Text = "Select a binding to change it."
                                                 RefreshBindings()
                                             End Sub
        For action = 0 To SeatActions.Names.Length - 1
            content = If(action < 2, vr, seat)
            Dim selectedAction = action

            Dim keyCell As Control
            If action < 9 Then
                Dim button = ShortcutButton(action)
                button.AccessibleName = SeatActions.Names(action) & " keyboard binding"
                AddHandler button.Click, Sub() BeginKeyCapture(selectedAction)
                Dim keys As New BindingKeyCell()
                keys.Controls.Add(button)
                If action >= 2 Then
                    Dim clear As New Button With {.Text = "×", .AutoSize = True, .AccessibleName = "Clear " & SeatActions.Names(action) & " keyboard binding"}
                    AddHandler clear.Click, Sub()
                                                If busy Then Return
                                                CancelBindingCapture()
                                                AssignShortcut(selectedAction, 0, 0) : RefreshBindings()
                                            End Sub
                    keys.Controls.Add(clear)
                End If
                keyCell = keys
            Else
                keyCell = New Label With {.Text = {"Shift (hold) · fixed", "Enter · fixed", "Escape · fixed"}(action - 9), .AccessibleName = SeatActions.Names(action) & " fixed keyboard shortcut; read only", .AutoSize = True}
            End If

            Dim cell As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 1, .Margin = New Padding(3, 3, 0, 14)}
            Dim list = bindingLists(action)
            list.Dock = DockStyle.Fill : list.Height = Px(Me, If(action < 2, 78, 32)) : list.IntegralHeight = False : list.HorizontalScrollbar = True
            list.AccessibleName = SeatActions.Names(action) & " controller bindings"
            cell.Controls.Add(list)
            Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
            Dim bind As New Button With {.Text = "Bind…", .AutoSize = True}
            Dim remove As New Button With {.Text = "Remove selected", .AutoSize = True}
            Tip(bind, If(action < 2, "Bind a button or pair. These buttons still reach the game too.", "Bind a button, pair or wheel POV direction. Seat shortcuts are reserved in cockpit VR; panel navigation buttons are reserved while open."))
            Tip(remove, "Remove the selected VR shortcut. Driving controls are unchanged.")
            AddHandler bind.Click, Sub() BeginControllerCapture(selectedAction)
            AddHandler remove.Click, Sub()
                                         If busy OrElse list.SelectedIndex < 0 Then Return
                                         Dim assignments = settings.Bindings.Where(Function(b) b.Action = selectedAction).ToArray()
                                         CancelBindingCapture()
                                         settings.Bindings.Remove(assignments(list.SelectedIndex)) : RefreshBindings()
                                     End Sub
            buttons.Controls.AddRange({bind, remove}) : cell.Controls.Add(buttons)
            Dim compactCell As New ControllerBindingCell(SeatActions.Names(action), cell)
            bindingCells(action) = compactCell
            AddHandler compactCell.AddBinding, Sub() BeginControllerCapture(selectedAction)
            AddHandler compactCell.EditBinding, Sub(binding)
                                                    BeginControllerCapture(selectedAction)
                                                    If Not busy Then replacementBinding = binding
                                                End Sub
            AddHandler compactCell.RemoveBinding, Sub(binding)
                                                      If busy Then Return
                                                      CancelBindingCapture()
                                                      settings.Bindings.Remove(binding) : RefreshBindings()
                                                  End Sub
            Dim row As New BindingRow(SeatActions.Names(action), keyCell, compactCell, action = 2) With {.Name = "BindingRow" & action}
            If action >= 3 AndAlso action <= 11 Then
                If action = 3 Then content.Controls.Add(individualSeats)
                individualSeats.Content.Controls.Add(row)
            Else
                content.Controls.Add(row)
            End If
            If action = 2 Then
                content.Controls.Add(New Label With {.Text = "Use the seat panel with arrows / D-pad. Separate movement bindings are optional.", .AutoSize = True, .Margin = New Padding(0, 0, 0, 12), .Name = "SeatPanelRecommendation"})
            End If
        Next

        content = drivingContent
        content.Controls.Add(New Label With {.Text = "Driving controls", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold), .Margin = New Padding(0, 0, 0, 8)})
        Dim driving As New Button With {.Text = "Configure driving controls…", .AutoSize = True, .Name = "DrivingControls"}
        AddHandler driving.Click, Sub()
                                     If busy Then Return
                                     SafeAction(Sub()
                                                    Using dialog As New DrivingControlsForm(context)
                                                        dialog.ShowDialog(Me)
                                                    End Using
                                                End Sub)
                                 End Sub
        content.Controls.Add(driving)
        content = general
        content.Controls.Add(New Label With {.Text = "Cockpit animation", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold), .Margin = New Padding(0, 20, 0, 8)})
        steeringAnimation.Checked = settings.VrSteeringAnimation
        Tip(steeringAnimation, "Try removing extra wheel and hand twitch in desktop or VR cockpit view. Desktop requires DX11. Keeps the original rotation range; does not change handling or force feedback. Save and relaunch. Still experimental.")
        Field(content, "Remove artificial steering corrections", steeringAnimation)
        steeringObserve.Checked = settings.SteeringObserveOnly
        steeringObserve.Enabled = steeringAnimation.Checked
        AddHandler steeringAnimation.CheckedChanged, Sub() steeringObserve.Enabled = steeringAnimation.Checked
        Tip(steeringObserve, "For testing: run the animation hooks without applying the correction. Enable Settings → diagnostic logging to record a short comparison. Turn the main option off for the unmodified baseline. Save and relaunch between tests.")
        Field(content, "Diagnostic mode", steeringObserve)
        BuildChaseControls(content)
        content.Controls.Add(HelpLink(Sub() ShowAbout("Controls")))
    End Sub
    Private Function ControlsPage(owner As ModernTabs, title As String) As VerticalStack
        Dim page As New TabPage(title) With {.Name = "Controls" & title, .AutoScroll = True, .UseVisualStyleBackColor = False, .Padding = New Padding(8)}
        Dim content = Stack()
        page.Controls.Add(content) : owner.TabPages.Add(page)
        AddHandler content.SizeChanged, Sub() FitLabels(content)
        Return content
    End Function
    Private Sub BuildChaseControls(content As VerticalStack)
        content.Controls.Add(New Label With {.Text = "Chase camera", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold), .Margin = New Padding(0, 20, 0, 8)})
        chaseFreeLook.Checked = settings.ChaseFreeLook
        chaseMouse.Value = settings.ChaseMouseSensitivity : chaseStick.Value = settings.ChaseStickSensitivity
        chaseMouseMode.SelectedMode = settings.ChaseMouseMode
        chaseInvert.Checked = settings.ChaseInvertVertical
        Tip(chaseFreeLook, "Use the mouse or right stick to look around your car. Choose a mouse button below, or Always on. Hold the right stick pressed in to look behind; release to return to normal chase view. Holds orbit angles while stopped; returns behind you when driving. VR also needs Advanced → Graphics → 3D beyond the cockpit. Save and relaunch.")
        Tip(chaseMouseMode, "Choose which mouse button to hold, or Always on to look around without clicking. Only active in chase view while driving; menus and pause keep normal mouse behavior.")
        For Each choice As RadioButton In chaseMouseMode.Controls
            Tip(choice, choice.AccessibleDescription & " Applies to chase-camera Free look. Save settings and relaunch.")
        Next
        Tip(chaseMouse, "How quickly the chase camera moves when you move the mouse.")
        Tip(chaseStick, "How quickly the chase camera moves with the controller's right stick.")
        Tip(chaseInvert, "Reverse up and down for both mouse and right-stick camera movement.")
        Field(content, "Free look", chaseFreeLook)
        Field(content, "Mouse look", chaseMouseMode)
        content.Controls.Add(New Label With {.Text = "Scroll to zoom in / out — no mouse button needed.", .AutoSize = True, .Margin = New Padding(0, 0, 0, 8)})
        Field(content, "Mouse sensitivity", chaseMouse)
        Field(content, "Right-stick sensitivity", chaseStick)
        Field(content, "Invert vertical", chaseInvert)
        Dim refresh As Action = Sub()
                                    chaseMouse.Enabled = chaseFreeLook.Checked
                                    chaseMouseMode.Enabled = chaseFreeLook.Checked
                                    chaseStick.Enabled = chaseFreeLook.Checked
                                    chaseInvert.Enabled = chaseFreeLook.Checked
                                End Sub
        AddHandler chaseFreeLook.CheckedChanged, Sub() refresh()
        refresh()
    End Sub
    Private Sub SafeAction(action As Action)
        Try
            action()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "DiRT2VR", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
    Private Sub SaveSettings()
        settings.ModernInterface = modernInterface.Checked
        settings.Runtime = runtimeBox.Text.Trim()
        settings.LoggingEnabled = logging.Checked
        settings.ExperimentalFlashback = flashback.Checked
        settings.VrExtendedViews = extendedViews.Checked
        settings.VrSteeringAnimation = steeringAnimation.Checked
        settings.SteeringObserveOnly = steeringObserve.Checked
        settings.ChaseFreeLook = chaseFreeLook.Checked
        settings.ChaseMouseSensitivity = chaseMouse.Value : settings.ChaseStickSensitivity = chaseStick.Value
        settings.ChaseMouseMode = chaseMouseMode.SelectedMode
        settings.ChaseInvertVertical = chaseInvert.Checked
        settings.SkipIntroduction = skipIntroduction.Checked
        settings.SkipStartupMovies = skipStartupMovies.Checked
        settings.BorderlessDesktop = borderless.Checked
        settings.DesktopVSync = desktopVSync.Checked
        settings.DesktopBloom = desktopBloom.Checked : settings.VrBloom = vrBloom.Checked
        settings.RenderScale = CInt(renderScale.Value) : settings.HeadsetScale = CInt(headsetScale.Value)
        settings.VrMsaa = {0, 2, 4, 8}(msaa.Value) : settings.VrShadows = vrShadows.Checked
        settings.FieldOfView = CInt(fieldOfView.Value) : settings.Mirrors = {"game", "on", "off"}(mirrors.SelectedIndex)
        settings.HudFollowView = hudFollow.Checked
        settings.HudDistance = hudDistance.Value / 2D
        settings.TreeDetail = treeDetail.Value : settings.ObjectDetail = objectDetail.Value
        settings.HudGauges = hudGauges.Checked : settings.HudLapTime = hudLapTime.Checked : settings.HudPosition = hudPosition.Checked
        settings.HudMap = hudMap.Checked : settings.HudProgress = hudProgress.Checked
        settings.LaunchMode = {"menus", "practice", "race"}(launchMode.SelectedIndex)
        settings.Opponents = CInt(opponents.Value)
        settings.OpponentCars = {"same", "mixed", "class"}(opponentCars.SelectedIndex)
        settings.Laps = CInt(laps.Value)
        settings.RaceDifficulty = raceDifficulty.SelectedIndex - 1
        If settings.DirectMode AndAlso Not customTracks.CustomEnabled Then
            Dim track = TryCast(trackChoice.SelectedItem, PracticeTrack)
            Dim car = TryCast(carChoice.SelectedItem, PracticeCar)
            If track Is Nothing OrElse car Is Nothing Then Throw New IOException("Select an installed track and car, or use Normal Launch.")
            RaceCatalog.Current.ValidateInstalled(context, track.Id, car.Code)
            settings.TrackId = track.Id : settings.CarCode = car.Code
        End If
        settings.Validate()
        filters.Save(settings)
        SaveSeats()
        Files.SaveJson(context.PreferencesPath, settings)
        customTracks.Save()
        stateLabel.Text = "Settings saved. Changes apply to the next session."
    End Sub
    Private Sub Spawn(ParamArray arguments As String())
        Dim start As New ProcessStartInfo(Environment.ProcessPath) With {.UseShellExecute = False, .CreateNoWindow = True}
        For Each arg In arguments.Concat({"--game", context.GameRoot})
            start.ArgumentList.Add(arg)
        Next
        If arguments.Contains("--launch") AndAlso Not arguments.Contains("--desktop") AndAlso Environment.GetCommandLineArgs().Contains("--diagnostic-capture") Then start.ArgumentList.Add("--diagnostic-capture")
        Using child = Process.Start(start)
            If arguments.Contains("--launch") Then
                StartupFocus.AllowSetForegroundWindow(CUInt(child.Id))
                WindowState = FormWindowState.Minimized
            End If
        End Using
    End Sub
    Private Sub RefreshBindings()
        For action = 0 To 8
            Dim key = Shortcut(action)
            ShortcutButton(action).Text = If(keyboardCapture = action, "Press a key…", If(key.Key = 0, "Unassigned", KeyLabel(key.Key, key.Modifiers)))
        Next
        Dim devices = input.Snapshot()
        For action = 0 To SeatActions.Names.Length - 1
            Dim selectedAction = action
            Dim list = bindingLists(action)
            Dim assignments = settings.Bindings.Where(Function(b) b.Action = selectedAction).ToArray()
            Dim rows = assignments.Select(Function(binding)
                Dim connected = devices.Any(Function(s) s.Source = binding.Source AndAlso s.Device = binding.Device AndAlso s.Connected)
                Return binding.Label & " — " & String.Join(" + ", binding.Buttons.Select(Function(b) ControllerNames.ButtonName(binding.Source, b))) & If(connected, "", " [disconnected / not detected]")
            End Function).ToArray()
            bindingCells(action)?.UpdateBindings(assignments, rows)
            If list.Items.Cast(Of String)().SequenceEqual(rows) Then Continue For
            Dim selected = list.SelectedIndex
            list.BeginUpdate() : list.Items.Clear() : list.Items.AddRange(rows) : list.EndUpdate()
            If selected >= 0 AndAlso selected < list.Items.Count Then list.SelectedIndex = selected
        Next
    End Sub
    Private Shared Function KeyLabel(key As Integer, modifiers As Integer) As String
        Return If((modifiers And 1) <> 0, "Ctrl+", "") & If((modifiers And 2) <> 0, "Alt+", "") & If((modifiers And 4) <> 0, "Shift+", "") & CType(key, Keys).ToString()
    End Function
    Private Sub CancelBindingCapture()
        keyboardCapture = -1 : controllerCapture = -1 : capturedDevice = Nothing
        buttonCapture = Nothing : replacementBinding = Nothing
        inputLabel.Text = "Click a binding to change it."
    End Sub
    Private Sub BeginKeyCapture(action As Integer)
        If busy Then Return
        CancelBindingCapture()
        keyboardCapture = action : controllerCapture = -1
        inputLabel.Text = "Press a key with optional Ctrl/Alt/Shift. Escape cancels."
        RefreshBindings()
    End Sub
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.KeyCode = Keys.Escape Then
            CancelBindingCapture()
            inputLabel.Text = "Binding cancelled." : RefreshBindings() : e.SuppressKeyPress = True : Return
        End If
        If keyboardCapture >= 0 Then
            e.SuppressKeyPress = True
            If Not VrSettings.ValidKey(CInt(e.KeyCode)) Then Return
            Dim modifiers = If(e.Control, 1, 0) Or If(e.Alt, 2, 0) Or If(e.Shift, 4, 0)
            If e.Alt AndAlso (e.KeyCode = Keys.F4 OrElse e.KeyCode = Keys.Tab) Then Return
            Dim previous = Shortcut(keyboardCapture)
            AssignShortcut(keyboardCapture, CInt(e.KeyCode), modifiers)
            Try
                SeatActions.Validate(settings)
            Catch ex As IOException
                AssignShortcut(keyboardCapture, previous.Key, previous.Modifiers)
                inputLabel.Text = ex.Message : Return
            End Try
            keyboardCapture = -1 : inputLabel.Text = "Keyboard binding captured. Save settings to keep it."
            RefreshBindings() : Return
        End If
        MyBase.OnKeyDown(e)
    End Sub
    Private Sub BeginControllerCapture(action As Integer)
        If busy Then Return
        input.Poll()
        CancelBindingCapture()
        keyboardCapture = -1 : controllerCapture = action : capturedDevice = Nothing
        buttonCapture = New ControllerCapture(input.Snapshot())
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
        Dim binding = buttonCapture.Update(sample, controllerCapture)
        If binding Is Nothing Then Return
        controllerCapture = -1 : capturedDevice = Nothing
        Dim previous = replacementBinding
        replacementBinding = Nothing
        If previous IsNot Nothing Then settings.Bindings.Remove(previous)
        settings.Bindings.Add(binding)
        Try
            settings.Validate()
            inputLabel.Text = "Controller binding captured. Save settings to keep it."
        Catch ex As Exception
            settings.Bindings.Remove(binding)
            If previous IsNot Nothing Then settings.Bindings.Add(previous)
            inputLabel.Text = ex.Message
        End Try
        RefreshBindings()
    End Sub
    Private Sub RefreshStatus()
        Try
            If DateTime.UtcNow >= nextResolutionRefresh Then
                RefreshResolutionReport() : nextResolutionRefresh = DateTime.UtcNow.AddSeconds(1)
                customTracks?.RefreshBestTime()
            End If
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
            RefreshSeatAvailability()
            RefreshLaunchAvailability()
            refreshServers.Enabled = Not busy AndAlso Not scanning
            tabs.Enabled = Not busy
            Dim currentStatus = If(status Is Nothing, "", status.State & status.UpdatedUtc.ToString("O"))
            If currentStatus <> lastStatus Then
                lastStatus = currentStatus : RefreshDisplayRate()
            End If
            If busy Then
                stateLabel.Text = status.State & If(status.Message <> "", ": " & status.Message, "")
            ElseIf New AssetTransaction(context).Pending OrElse New GraphicsTransaction(context).Pending OrElse New LanTransaction(context).Pending OrElse New StartupMovies(context).Pending OrElse New DirectMenus(context).Pending OrElse CustomTrackService.RecoveryPending(context) Then
                stateLabel.Text = "Recovery pending. Close the game and choose Restore original files."
            ElseIf status IsNot Nothing AndAlso status.State = "Failed" Then
                stateLabel.Text = status.FailureDescription(context.GameRunning())
            ElseIf status IsNot Nothing AndAlso status.DisplayWarning <> "" Then
                stateLabel.Text = "Ready — " & status.DisplayWarning
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
