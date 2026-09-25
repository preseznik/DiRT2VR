Imports System.Windows.Forms
Imports System.Drawing
Imports System.Threading.Tasks

Public Class MainForm
    Inherits Form
    Private ReadOnly context As InstallContext
    Private settings As VrSettings
    Private ReadOnly runtimeBox As New TextBox With {.Dock = DockStyle.Fill}
    Private ReadOnly logging As New CheckBox With {.Text = "Enable diagnostic logging", .Name = "LoggingEnabled", .AutoSize = True}
    Private ReadOnly skipIntroduction As New CheckBox With {.Text = "Skip introduction for LAN multiplayer", .Name = "SkipIntroduction", .AutoSize = True}
    Private ReadOnly skipStartupMovies As New CheckBox With {.Text = "Skip startup logo movies (single-player launches)", .Name = "SkipStartupMovies", .AutoSize = True}
    Private ReadOnly serverList As New ListView With {.Name = "LanServers", .View = View.Details, .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .Dock = DockStyle.Top, .Height = 300}
    Private ReadOnly hostButton As New Button With {.Name = "HostLAN", .Text = "HOST", .AutoSize = True}
    Private ReadOnly joinButton As New Button With {.Name = "JoinLAN", .Text = "JOIN", .AutoSize = True, .Enabled = False}
    Private ReadOnly refreshServers As New Button With {.Name = "RefreshLAN", .Text = "Refresh", .AutoSize = True}
    Private ReadOnly browserStatus As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0), .Text = "Open Multiplayer to find LAN hosts."}
    Private scanning As Boolean
    Private nextScan As DateTime
    Private ReadOnly stateLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly inputLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(740, 0)}
    Private ReadOnly bindingLists As ListBox() = {New ListBox(), New ListBox()}
    Private ReadOnly tabs As New TabControl With {.Dock = DockStyle.Fill, .Name = "LauncherTabs"}
    Private ReadOnly launchMode As ComboBox = Choice("LaunchMode")
    Private ReadOnly eventChoice As ComboBox = Choice("PracticeEvent")
    Private ReadOnly trackChoice As ComboBox = Choice("PracticeTrack")
    Private ReadOnly carChoice As ComboBox = Choice("PracticeCar")
    Private ReadOnly opponents As New ValueSlider("Opponents", 1, 7, 7) With {.AccessibleName = "AI opponents"}
    Private ReadOnly laps As New ValueSlider("Laps", 1, 20, 1) With {.AccessibleName = "Laps"}
    Private ReadOnly borderless As New CheckBox With {.Text = "Borderless fullscreen (desktop only)", .Name = "BorderlessDesktop", .AutoSize = True}
    Private ReadOnly renderScale As New ValueSlider("RenderScale", 50, 150, 100, "%")
    Private ReadOnly headsetScale As New ValueSlider("HeadsetScale", 25, 100, 50, "%")
    Private ReadOnly fieldOfView As New ValueSlider("FieldOfView", 70, 100, 100, "%")
    Private ReadOnly opponentCars As ComboBox = Choice("OpponentCars")
    Private ReadOnly opponentHint As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0)}
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
    Private ReadOnly graphicsSummary As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0)}
    Private ReadOnly refreshLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(710, 0)}
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
    Private ReadOnly updateNotice As New Button With {.Text = "New version available", .Name = "UpdateAvailable", .AutoSize = True, .Visible = False, .Anchor = AnchorStyles.Right}
    Private ReadOnly updateCancellation As New CancellationTokenSource()
    Private ReadOnly checkForUpdate As Func(Of CancellationToken, Task(Of ReleaseUpdate))
    Private availableUpdate As ReleaseUpdate
    Public Sub New(value As InstallContext, Optional releaseCheck As Func(Of CancellationToken, Task(Of ReleaseUpdate)) = Nothing)
        context = value
        input = New ControllerInput(context)
        checkForUpdate = If(releaseCheck, AddressOf CheckReleaseAsync)
        settings = VrSettings.Load(context)
        Text = "DiRT2VR — Experimental launcher"
        Using stream = GetType(MainForm).Assembly.GetManifestResourceStream("DiRT2VR.ico"), appIcon As New Icon(stream)
            Icon = DirectCast(appIcon.Clone(), Icon)
        End Using
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        MinimumSize = New Size(820, 690)
        ClientSize = New Size(900, 1040)
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
        BuildGraphicsTab()
        BuildControlsTab()
        BuildSettingsTab()
        AddHandler tabs.SelectedIndexChanged, Sub()
                                                  keyboardCapture = -1 : controllerCapture = -1 : capturedDevice = Nothing
                                                  inputLabel.Text = "Select a binding to change it."
                                                  RefreshBindings()
                                                  desktopButton.Visible = tabs.SelectedIndex <> 1
                                                  launchButton.Visible = tabs.SelectedIndex <> 1
                                              End Sub
        Dim commands As New TableLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0, 16, 0, 0)}
        commands.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        commands.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        commands.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim launches As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0)}
        Dim utilities As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0), .Anchor = AnchorStyles.Right}
        Dim logs As New Button With {.Text = "Open logs", .AutoSize = True, .Name = "OpenLogs"}
        launches.Controls.AddRange({desktopButton, launchButton})
        utilities.Controls.AddRange({saveButton, recoverButton, logs})
        commands.Controls.Add(launches, 0, 0) : commands.Controls.Add(utilities, 2, 0)
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
        inputTimer.Start() : timer.Start() : RefreshStatus()
        AddHandler Shown, Sub() FitInitialWindow()
        AddHandler Shown, Async Sub() Await CheckStartupUpdate()
    End Sub
    Private Shared Async Function CheckReleaseAsync(token As CancellationToken) As Task(Of ReleaseUpdate)
        Using client = UpdateService.CreateClient()
            Return Await New UpdateService(client).CheckAsync(BuildInfo.Version, BuildInfo.Version.Contains("-"), token)
        End Using
    End Function
    Private Async Function CheckStartupUpdate() As Task
        Dim token = updateCancellation.Token
        Try
            Dim result = Await checkForUpdate(token)
            If IsDisposed OrElse token.IsCancellationRequested Then Return
            availableUpdate = result
            updateNotice.Visible = result IsNot Nothing
            If result IsNot Nothing Then updateNotice.AccessibleDescription = "Version " & result.Version.Text & " is available. Open Help / About to review and install."
        Catch ex As Exception
            ' Offline/rate-limited startup checks are quiet; About offers a manual retry.
        End Try
    End Function
    Private Shared Function Choice(name As String) As ComboBox
        Return New ComboBox With {.Name = name, .AccessibleName = name, .DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill, .DropDownWidth = 600}
    End Function
    Private Function TabLayout(title As String) As TableLayoutPanel
        ' Native themed TabPages still paint a light background in Windows dark mode.
        ' Inherit the form's resolved system palette instead of the visual-style brush.
        Dim page As New TabPage(title) With {.Padding = New Padding(16), .UseVisualStyleBackColor = False, .BackColor = BackColor, .AutoScroll = True}
        Dim content As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 1}
        content.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        page.Controls.Add(content) : tabs.TabPages.Add(page)
        Return content
    End Function
    Private Shared Function Note(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(710, 0), .Margin = New Padding(0, 8, 0, 12)}
    End Function
    Private Sub FitInitialWindow()
        ' Size from the laid-out Settings content at the actual display DPI, then cap to the monitor.
        Dim page = tabs.TabPages.Cast(Of TabPage).Single(Function(p) p.Text = "Settings")
        Dim content = page.Controls(0)
        Dim desiredHeight = Height + Math.Max(0, content.PreferredSize.Height + page.Padding.Vertical + 12 - page.ClientSize.Height)
        Dim work = Screen.FromControl(Me).WorkingArea
        MinimumSize = New Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height))
        Size = New Size(Math.Min(Width, work.Width), Math.Min(desiredHeight, work.Height))
        Location = New Point(work.Left + (work.Width - Width) \ 2, work.Top + (work.Height - Height) \ 2)
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
        content.Controls.Add(Note("HOST opens DiRT 2 and lists this PC while the game is running. Create a lobby in the game's Multiplayer / LAN menu. 'HOST game running' does not confirm a lobby or player count."))
        content.Controls.Add(Note("JOIN opens LAN play and adds the selected PC to the game's network peers. Finish joining through the game's Multiplayer / LAN menu. Automatic lobby entry is not available yet."))
        content.Controls.Add(Note("HOST and JOIN ask you to choose Desktop or VR each time. Both use your normal career. Multiplayer VR is untested, and the race-loading disconnect remains under investigation. Both PCs need the same local network; allow DiRT 2 on your Windows Private network if prompted."))
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
        Dim grid As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Dock = DockStyle.Top}
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140))
        grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Dim labels = {"Launch mode", "Event", "Track", "Car", "Opponent cars"}
        Dim choices = {launchMode, eventChoice, trackChoice, carChoice, opponentCars}
        For i = 0 To choices.Length - 1
            grid.Controls.Add(New Label With {.Text = labels(i), .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, i)
            choices(i).Margin = New Padding(3, 8, 3, 10)
            choices(i).FlatStyle = FlatStyle.Flat
            choices(i).BackColor = BackColor : choices(i).ForeColor = ForeColor
            ' Some native combo text areas retain a light brush even in app dark mode.
            choices(i).DrawMode = DrawMode.OwnerDrawFixed
            AddHandler choices(i).DrawItem, AddressOf DrawChoice
            grid.Controls.Add(choices(i), 1, i)
        Next
        content.Controls.Add(grid)
        grid.Controls.Add(New Label With {.Text = "AI opponents", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 5)
        opponents.Value = settings.Opponents
        grid.Controls.Add(opponents, 1, 5)
        grid.Controls.Add(New Label With {.Text = "Laps (circuits)", .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, 6)
        laps.Value = settings.Laps
        grid.Controls.Add(laps, 1, 6)
        AddHandler trackChoice.SelectedIndexChanged, Sub() RefreshLaps()
        launchMode.Items.AddRange({"Normal Launch", "Direct practice (experimental)", "Race (experimental)"})
        opponentCars.Items.AddRange({"Same as driver", "Mixed", "Same class"})
        opponentCars.SelectedIndex = Array.IndexOf({"same", "mixed", "class"}, settings.OpponentCars)
        AddHandler opponentCars.SelectedIndexChanged, Sub() RefreshOpponentHint()
        AddHandler carChoice.SelectedIndexChanged, Sub() RefreshOpponentHint()
        eventChoice.Items.AddRange(RaceCatalog.Current.Tracks.Where(Function(t) Directory.Exists(t.Folder(context))).Select(Function(t) t.Event).Distinct().Order().Cast(Of Object).ToArray())
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
                                                        opponents.Enabled = launchMode.SelectedIndex = 2
                                                        opponentCars.Enabled = launchMode.SelectedIndex = 2
                                                        RefreshOpponentHint()
                                                        RefreshLaps()
                                                    End Sub
        launchMode.SelectedIndex = Math.Max(0, Array.IndexOf({"menus", "practice", "race"}, settings.LaunchMode))
        content.Controls.Add(opponentHint)
        content.Controls.Add(Note("Launch plays on your monitor; Launch VR uses SteamVR. Practice is solo; Race adds AI opponents. Start with Landrush or Rallycross; other event grids and VR cockpits remain experimental."))
        content.Controls.Add(Note("Laps apply to circuits in both Practice and Race; point-to-point stages are one run. Finish with Restart or Return to menus. Pause also offers both choices. Return to menus closes the session and reopens Normal Launch in the same Desktop/VR mode; Alt+F4 quits without reopening. Custom races do not award career progress."))
    End Sub
    Private Sub RefreshOpponentHint()
        launchButton.Enabled = Not busy
        Dim vehicle = TryCast(carChoice.SelectedItem, PracticeCar)
        opponentHint.Text = If(launchMode.SelectedIndex <> 2, "Opponent car selection applies only to Race.", If(opponentCars.SelectedIndex = 2, "Opponent class: " & If(vehicle?.ClassName, "Select a car"), If(opponentCars.SelectedIndex = 1, "Mixed draws from all installed classes; vehicle performance can differ substantially.", "All opponents use the same car as the driver.")))
    End Sub
    Private Sub ShowAbout()
        Using dialog As New AboutForm(context, availableUpdate)
            dialog.ShowDialog(Me)
        End Using
    End Sub
    Private Sub RefreshLaps()
        Dim track = TryCast(trackChoice.SelectedItem, PracticeTrack)
        laps.Enabled = (launchMode.SelectedIndex = 1 OrElse launchMode.SelectedIndex = 2) AndAlso track IsNot Nothing AndAlso track.Circuit
    End Sub
    Private Sub DrawChoice(sender As Object, e As DrawItemEventArgs)
        Dim box = DirectCast(sender, ComboBox)
        Dim highlighted = (e.State And DrawItemState.Selected) <> 0 AndAlso (e.State And DrawItemState.ComboBoxEdit) = 0
        Dim background = If(highlighted, SystemColors.Highlight, BackColor)
        Dim foreground = If(Not box.Enabled, SystemColors.GrayText, If(highlighted, SystemColors.HighlightText, ForeColor))
        Using brush As New SolidBrush(background)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        If e.Index >= 0 Then
            TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items(e.Index)), box.Font, e.Bounds, foreground, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End If
        e.DrawFocusRectangle()
    End Sub
    Private Sub BuildSettingsTab()
        Dim content = TabLayout("Settings")
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
        skipStartupMovies.Checked = settings.SkipStartupMovies : content.Controls.Add(skipStartupMovies)
        content.Controls.Add(Note("Skip the Codemasters, Intel, AMD and EGO startup movies in single-player launches. LAN keeps these movies because changing their definitions fails the game's race-loading checks. Other videos and the legal screen remain. Original definitions are restored after play."))
        content.Controls.Add(Note("LAN uses the same career and graphics settings as normal play. No import or separate save is needed."))
        skipIntroduction.Checked = settings.SkipIntroduction : content.Controls.Add(skipIntroduction)
        content.Controls.Add(Note("LAN only: skip the opening movie and forced tutorial while keeping profile creation. Applies on the next LAN launch; turning it off does not undo saved progress. Normal Launch, Practice and Race are unaffected."))
        logging.Checked = settings.LoggingEnabled : content.Controls.Add(logging)
        content.Controls.Add(Note("Logging is off by default. Enable it only when troubleshooting, then save before launching. Recovery records are always kept; existing logs are not deleted."))
        content.Controls.Add(Note("For Launch VR, start SteamVR and connect your headset first. Use the Subaru STI cockpit for the tested setup. Regular Launch does not require a headset."))
        content.Controls.Add(Note("VR launches enter races in cockpit VR automatically. Toggle VR switches to the flat screen; Recenter resets your seated position. Menus and pause screens appear on the flat screen."))
        content.Controls.Add(Note("Quit the game normally to restore temporary files. You can close this settings window while playing; the background session manager stays running."))
    End Sub
    Private Sub BuildGraphicsTab()
        Dim content = TabLayout("Graphics")
        content.Controls.Add(New Label With {.Text = "Desktop display", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        borderless.Checked = settings.BorderlessDesktop : content.Controls.Add(borderless)
        content.Controls.Add(Note("Fill the primary monitor at desktop resolution, with window borders and in-game VSync off. Applies to desktop Launch and HOST/JOIN; original display settings are restored after play."))
        content.Controls.Add(New Label With {.Text = "VR graphics", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
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
        treeDetail.Value = settings.TreeDetail : objectDetail.Value = settings.ObjectDetail
        AddGraphicsRow(grid, "Tree detail", treeDetail, "VR only. Higher detail keeps detailed vegetation farther away. Game preserves your game setting.")
        AddGraphicsRow(grid, "Object detail", objectDetail, "VR only. Try Ultra for trackside pop-in; costs performance. Track-specific distance limits still apply.")
        hudDistance.Value = CInt(settings.HudDistance * 2D)
        AddGraphicsRow(grid, "HUD distance", hudDistance, "1–20 metres, in 0.5 m steps. Keeps the HUD's apparent size. Default: 1 m.")
        content.Controls.Add(grid)
        hudFollow.Checked = settings.HudFollowView : content.Controls.Add(hudFollow)
        content.Controls.Add(New Label With {.Text = "Show HUD areas", .AutoSize = True})
        hudGauges.Checked = settings.HudGauges : hudLapTime.Checked = settings.HudLapTime : hudPosition.Checked = settings.HudPosition
        hudMap.Checked = settings.HudMap : hudProgress.Checked = settings.HudProgress
        Dim hudElements As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top, .WrapContents = True, .Margin = New Padding(12, 0, 0, 0)}
        hudElements.Controls.AddRange({hudGauges, hudLapTime, hudPosition, hudMap, hudProgress})
        content.Controls.Add(hudElements)
        content.Controls.Add(Note("Uncheck to hide that area of the cockpit HUD. Other overlays in the same area are hidden too. The centre, menus, virtual screen and desktop HUD stay unchanged. Uses the standard race HUD layout."))
        content.Controls.Add(Note("The cockpit HUD is fixed relative to the car at your chosen distance. Enable HUD follows view to keep it in front of your head. Recenter resets its position. Save and relaunch VR to apply."))
        content.Controls.Add(Note("Render resolution controls scene detail. Raising headset texture scale alone cannot add missing detail. Cropping reduces peripheral vision; performance gains depend on the scene."))
        content.Controls.Add(New Label With {.Text = "Headset refresh rate", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        content.Controls.Add(refreshLabel)
        content.Controls.Add(Note("Set refresh rate in SteamVR or your headset connection software before launching. DiRT2VR follows the runtime; the game's desktop refresh setting does not select headset Hz."))
        Dim defaults As New Button With {.Text = "Restore graphics defaults", .AutoSize = True}
        AddHandler defaults.Click, Sub()
                                       borderless.Checked = False
                                       renderScale.Value = 100 : headsetScale.Value = 50 : fieldOfView.Value = 100 : mirrors.SelectedIndex = 0
                                       hudFollow.Checked = False
                                       hudDistance.Value = 2
                                       treeDetail.Value = 0 : objectDetail.Value = 0
                                       hudGauges.Checked = False
                                       For Each element In {hudLapTime, hudPosition, hudMap, hudProgress}
                                           element.Checked = True
                                       Next
                                   End Sub
        content.Controls.Add(defaults)
        content.Controls.Add(Note("Save settings to apply on the next launch. VR crowds, particles, shadows and motion blur retain the current reduced-effects setup."))
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
            Dim summaryPath = IO.Path.Combine(context.UserRoot, "headset.json")
            If File.Exists(summaryPath) Then
                Dim summary = Files.ReadJson(Of HeadsetStatus)(summaryPath)
                refreshLabel.Text = If(summary.RefreshHz <> "", $"Last launch reported {summary.RefreshHz} Hz ({summary.UpdatedUtc.ToLocalTime():g}). This is not a live reading.", "The last preflight did not report headset Hz. Check SteamVR or your headset connection software.")
                Return
            End If
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
        content.Controls.Add(New Label With {.Text = "Driving controls", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold), .Margin = New Padding(0, 20, 0, 8)})
        content.Controls.Add(Note("Direct practice and Race load your existing profile's controls before the event. Use the optional editor below to assign steering, pedals, clutch, handbrake and gears across launcher modes."))
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
        settings.LoggingEnabled = logging.Checked
        settings.SkipIntroduction = skipIntroduction.Checked
        settings.SkipStartupMovies = skipStartupMovies.Checked
        settings.BorderlessDesktop = borderless.Checked
        settings.RenderScale = CInt(renderScale.Value) : settings.HeadsetScale = CInt(headsetScale.Value)
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
        If settings.DirectMode Then
            Dim track = TryCast(trackChoice.SelectedItem, PracticeTrack)
            Dim car = TryCast(carChoice.SelectedItem, PracticeCar)
            If track Is Nothing OrElse car Is Nothing Then Throw New IOException("Select an installed track and car, or use Normal Launch.")
            RaceCatalog.Current.ValidateInstalled(context, track.Id, car.Code)
            settings.TrackId = track.Id : settings.CarCode = car.Code
        End If
        settings.Validate()
        Files.SaveJson(context.PreferencesPath, settings)
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
        input.Poll()
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
            desktopButton.Enabled = Not busy : launchButton.Enabled = Not busy : recoverButton.Enabled = Not busy : saveButton.Enabled = Not busy
            hostButton.Enabled = Not busy : refreshServers.Enabled = Not busy AndAlso Not scanning
            joinButton.Enabled = Not busy AndAlso If(SelectedHost()?.Joinable, False)
            tabs.Enabled = Not busy
            Dim currentStatus = If(status Is Nothing, "", status.State & status.UpdatedUtc.ToString("O"))
            If currentStatus <> lastStatus Then
                lastStatus = currentStatus : RefreshDisplayRate()
            End If
            If busy Then
                stateLabel.Text = status.State & If(status.Message <> "", ": " & status.Message, "")
            ElseIf New AssetTransaction(context).Pending OrElse New GraphicsTransaction(context).Pending OrElse New LanTransaction(context).Pending OrElse New StartupMovies(context).Pending OrElse New DirectMenus(context).Pending Then
                stateLabel.Text = "Recovery pending. Close the game and choose Restore original files."
            ElseIf status IsNot Nothing AndAlso status.State = "Failed" Then
                stateLabel.Text = "Failed: " & status.Message
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
