Imports DiRT2VR.CustomTracks
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class CustomTrackPanel
    Inherits VerticalStack
    Private ReadOnly context As InstallContext
    Private ReadOnly preferences As CustomTrackPreferences
    Private ReadOnly toggle As New CheckBox With {.Text = "CUSTOM tracks (Experimental)", .Name = "CustomTracks", .AutoSize = True}
    Private ReadOnly browser As New CustomTrackBrowser
    Private ReadOnly detail As VerticalStack
    Private ReadOnly title As New Label With {.AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold)}
    Private ReadOnly description As New Label With {.AutoSize = False}
    Private ReadOnly status As New Label With {.Name = "CustomTrackStatus", .AutoSize = False}
    Private ReadOnly setup As New VerticalStack With {.Name = "CustomTrackSetup"}
    Private ReadOnly body As New VerticalStack
    Private ReadOnly installCard As New VerticalStack
    Private ReadOnly unavailable As New Label With {.Name = "CustomPackUnavailable", .AutoSize = False}
    Private ReadOnly layouts As New ComboBox With {.Name = "CustomTrackLayout", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly condition As New Label With {.Name = "CustomTrackCondition", .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly launchMode As New ComboBox With {.Name = "CustomLaunchMode", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cars As New ComboBox With {.Name = "CustomCar", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponentCars As New ComboBox With {.Name = "CustomOpponentCars", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponents As New ValueSlider("CustomOpponents", 1, 7, 3)
    Private ReadOnly laps As New ValueSlider("CustomLaps", 1, 20, 1)
    Private ReadOnly modeHint As New Label With {.Name = "CustomModeHint", .AutoSize = False}
    Private ReadOnly opponentHint As New Label With {.AutoSize = False}
    Private ReadOnly installButton As New Button With {.Text = "Build and install…", .Name = "BuildCustomTrack", .AutoSize = True}
    Private ReadOnly manage As New Button With {.Text = "Manage…", .Name = "ManageCustomTrack", .AutoSize = True}
    Private ReadOnly cancelButton As New Button With {.Text = "Cancel", .Name = "CancelCustomTrack", .AutoSize = True}
    Private ReadOnly errorDetails As New Button With {.Text = "Details…", .Name = "CustomTrackErrorDetails", .AutoSize = True}
    Private ReadOnly progressBar As New ProgressBar With {.Name = "CustomTrackProgress", .Minimum = 0, .Maximum = 100, .Height = 18}
    Private ReadOnly actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
    Private ReadOnly menu As New ContextMenuStrip
    Private ReadOnly packStatuses As New Dictionary(Of String, String)
    Private currentPack As CustomTrackPack
    Private cancellation As CancellationTokenSource
    Private installationValid As Boolean
    Private installedLayouts As New HashSet(Of String)(StringComparer.Ordinal)
    Private hasReceipt As Boolean
    Private raceReady As Boolean
    Private working As Boolean
    Private loading As Boolean
    Private initialized As Boolean
    Private errorText As String = ""
    Public Event AvailabilityChanged As EventHandler
    Private Class LayoutItem
        Public ReadOnly Value As Layout
        Public Sub New(value As Layout)
            Me.Value = value
        End Sub
        Public Overrides Function ToString() As String
            Return Value.Name & If(Value.Discipline = "Head-to-head", " (solo practice)", "")
        End Function
    End Class
    Public ReadOnly Property CustomEnabled As Boolean
        Get
            Return toggle.Checked
        End Get
    End Property
    Public ReadOnly Property IsWorking As Boolean
        Get
            Return working
        End Get
    End Property
    Public ReadOnly Property CanLaunch As Boolean
        Get
            Return currentPack IsNot Nothing AndAlso currentPack.Available AndAlso Not working AndAlso installationValid AndAlso layouts.SelectedItem IsNot Nothing AndAlso
                installedLayouts.Contains(DirectCast(layouts.SelectedItem, LayoutItem).Value.Id) AndAlso launchMode.SelectedIndex >= 0 AndAlso (launchMode.SelectedIndex <> 1 OrElse raceReady) AndAlso cars.SelectedItem IsNot Nothing AndAlso opponentCars.SelectedIndex >= 0
        End Get
    End Property
    Public ReadOnly Property CanLaunchVr As Boolean
        Get
            Return CanLaunch AndAlso TrackPacks.Get(currentPack.Id).Modes.Any(Function(m) m.StartsWith("vr-", StringComparison.Ordinal))
        End Get
    End Property
    Public Sub New(value As InstallContext)
        context = value : preferences = CustomTrackPreferences.Load(context)
        Name = "CustomTrackPanel" : detail = browser.Detail
        For Each choice In {layouts, launchMode, cars, opponentCars, browser.Picker}
            StyleChoice(choice, Me)
        Next
        Controls.Add(toggle)
        detail.Controls.Add(title) : detail.Controls.Add(description) : detail.Controls.Add(status) : detail.Controls.Add(body)
        Dim layoutColumns As New ResponsiveColumns With {.WideAt = 560}
        Field(layoutColumns.First, "Layout", layouts) : Field(layoutColumns.Second, "Conditions", condition)
        setup.Controls.Add(layoutColumns)
        Dim columns As New ResponsiveColumns With {.WideAt = 560}
        setup.Controls.Add(columns)
        launchMode.Items.AddRange({"Direct practice", "Race"})
        cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
        opponentCars.Items.AddRange({"Same as driver", "Mixed", "Same class"})
        Field(columns.First, "Launch mode", launchMode) : Field(columns.First, "Car", cars)
        Field(columns.Second, "AI opponents", opponents) : Field(columns.Second, "Opponent cars", opponentCars)
        Field(setup, "Laps", laps) : setup.Controls.Add(opponentHint)
        setup.Controls.Add(modeHint)
        installCard.Controls.Add(New Label With {.Text = "Build this pack from your own DiRT 3 Complete Edition files. You can select a detected installation or browse to its folder. Installed tracks work offline.", .AutoSize = False})
        installCard.Controls.Add(installButton)
        detail.Controls.Add(actions)
        detail.Controls.Add(New Label With {.Text = "Turn CUSTOM tracks off to restore your original track, car and race selections.", .AutoSize = False})
        For Each pack In CustomTrackCatalog.Packs
            browser.PackList.Items.Add(pack) : browser.Picker.Items.Add(pack)
            packStatuses(pack.Id) = If(pack.Available, If(File.Exists(SafeFiles.Inside(context.GameRoot, TrackPacks.Get(pack.Id).Receipt)), "Installed", "Not installed"), "In development")
        Next
        currentPack = CustomTrackCatalog.Find(preferences.SelectedPackId)
        If currentPack Is Nothing Then
            currentPack = New CustomTrackPack(preferences.SelectedPackId, "Unavailable pack", "Your saved pack is not supported by this launcher.", False, Array.Empty(Of Layout)())
            browser.PackList.Items.Add(currentPack) : browser.Picker.Items.Add(currentPack)
            packStatuses(currentPack.Id) = "Unavailable"
        End If
        AddHandler browser.PackList.DrawItem, AddressOf DrawPack
        AddHandler browser.PackList.SelectedIndexChanged, Async Sub() Await SelectPack(TryCast(browser.PackList.SelectedItem, CustomTrackPack))
        AddHandler browser.Picker.SelectedIndexChanged, Async Sub() Await SelectPack(TryCast(browser.Picker.SelectedItem, CustomTrackPack))
        For Each choice In {launchMode, cars, opponentCars}
            AddHandler choice.SelectedIndexChanged, Sub() RefreshRaceOptions()
        Next
        AddHandler layouts.SelectedIndexChanged, Sub()
                                                    Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
                                                    condition.Text = If(chosen Is Nothing, "Choose a layout", chosen.Discipline & " · " & chosen.Condition)
                                                    If Not loading Then LoadSmelterCar()
                                                    RefreshRaceOptions()
                                                    RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
                                                End Sub
        AddHandler toggle.CheckedChanged, Async Sub()
                                             If toggle.Checked Then
                                                 If Not Controls.Contains(browser) Then Controls.Add(browser)
                                             Else
                                                 Controls.Remove(browser)
                                             End If
                                             preferences.Enabled = toggle.Checked
                                             RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
                                             If initialized AndAlso toggle.Checked Then Await RefreshInstallation()
                                         End Sub
        AddHandler installButton.Click, Async Sub() Await InstallPack()
        AddHandler cancelButton.Click, Sub() CancelOperation()
        AddHandler manage.Click, Sub() menu.Show(manage, New Point(0, manage.Height))
        AddHandler menu.Items.Add("Verify installed files").Click, Async Sub() Await RefreshInstallation()
        AddHandler menu.Items.Add("Rebuild from source…").Click, Async Sub() Await InstallPack()
        AddHandler menu.Items.Add("Uninstall pack…").Click, Async Sub()
                                                               If Not currentPack.Available OrElse working Then Return
                                                               If MessageBox.Show(Me, "Remove " & currentPack.Name & " and its " & currentPack.Layouts.Length & " layouts? Original tracks, saves and other packs will be kept.", "Uninstall " & currentPack.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
                                                               Await Operation(Async Function(token, progress)
                                                                                   Await CustomTrackService.UninstallAsync(context, currentPack.Id)
                                                                                   installationValid = False : hasReceipt = False
                                                                                   SetStatus("Not installed", currentPack.Name & " removed. Your original tracks are available with CUSTOM tracks off.")
                                                                               End Function)
                                                           End Sub
        AddHandler errorDetails.Click, Sub() MessageBox.Show(Me, errorText, currentPack.Name & " — details", MessageBoxButtons.OK, MessageBoxIcon.Information)
        AddHandler Disposed, Sub()
                                 cancellation?.Cancel()
                                 menu.Dispose()
                                 For Each control As Control In New Control() {browser, setup, installCard, unavailable, manage, cancelButton, errorDetails, progressBar}
                                     control.Dispose()
                                 Next
                             End Sub
        LoadSelection()
        toggle.Checked = preferences.Enabled
        initialized = True
        AddHandler HandleCreated, Sub()
                                      BeginInvoke(New Action(Async Sub()
                                                                 If IsDisposed Then Return
                                                                 If CustomEnabled Then Await RefreshInstallation()
                                                             End Sub))
                                  End Sub
    End Sub
    Private Async Function SelectPack(pack As CustomTrackPack) As Task
        If loading OrElse working OrElse pack Is Nothing OrElse pack Is currentPack Then Return
        CaptureSelection() : currentPack = pack : preferences.SelectedPackId = pack.Id
        LoadSelection()
        If initialized AndAlso CustomEnabled Then Await RefreshInstallation()
    End Function
    Private Sub LoadSelection()
        loading = True
        Try
            browser.PackList.SelectedItem = currentPack : browser.Picker.SelectedItem = currentPack
            title.Text = currentPack.Name : description.Text = currentPack.Description
            installationValid = False : raceReady = False : hasReceipt = False : errorText = "" : installedLayouts.Clear()
            Dim selected = preferences.ForPack(currentPack.Id)
            layouts.Items.Clear()
            For Each trackLayout In currentPack.Layouts
                layouts.Items.Add(New LayoutItem(trackLayout))
            Next
            layouts.SelectedItem = layouts.Items.Cast(Of LayoutItem).FirstOrDefault(Function(item) item.Value.Id = selected.LayoutId)
            launchMode.Items.Clear()
            launchMode.Items.Add("Direct practice")
            If currentPack.Available AndAlso TrackPacks.Get(currentPack.Id).Modes.Contains("desktop-race") Then launchMode.Items.Add("Race")
            cars.Items.Clear()
            cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) (currentPack.Id <> "smelter" OrElse c.Code = "sti") AndAlso File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
            launchMode.SelectedIndex = If(selected.LaunchMode = "race" AndAlso launchMode.Items.Count = 1, -1, Array.IndexOf({"practice", "race"}, selected.LaunchMode))
            cars.SelectedItem = cars.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) c.Code = selected.CarCode)
            opponentCars.SelectedIndex = Array.IndexOf({"same", "mixed", "class"}, selected.OpponentCars)
            opponents.Value = Math.Clamp(selected.Opponents, 1, 7) : laps.Value = Math.Clamp(selected.Laps, 1, 20)
            status.Text = If(currentPack.Available, "Choose Build and install to prepare this pack.", If(currentPack.Id = "smelter", "In development · Gameplay validation pending", "Unavailable in this launcher"))
            unavailable.Text = If(currentPack.Id = "smelter", "Smelter is unavailable in this launcher.", "Choose an available pack from the list. Your saved selection has been kept.")
        Finally
            loading = False
        End Try
        LoadSmelterCar() : RefreshRaceOptions() : RenderState()
    End Sub
    Private Sub LoadSmelterCar()
        If currentPack?.Id <> "smelter" OrElse layouts.SelectedItem Is Nothing Then Return
        Dim code = TrackPacks.Smelter.PracticeCar(DirectCast(layouts.SelectedItem, LayoutItem).Value.Id)
        Dim wasLoading = loading
        loading = True
        Try
            cars.Items.Clear()
            cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) c.Code = code AndAlso File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).Cast(Of Object).ToArray())
            cars.SelectedIndex = If(cars.Items.Count = 1, 0, -1)
        Finally
            loading = wasLoading
        End Try
    End Sub
    Private Sub CaptureSelection()
        If currentPack Is Nothing OrElse Not currentPack.Available Then Return
        Dim selected = preferences.ForPack(currentPack.Id)
        If layouts.SelectedItem IsNot Nothing Then selected.LayoutId = DirectCast(layouts.SelectedItem, LayoutItem).Value.Id
        If launchMode.SelectedIndex >= 0 Then selected.LaunchMode = {"practice", "race"}(launchMode.SelectedIndex)
        If cars.SelectedItem IsNot Nothing Then selected.CarCode = DirectCast(cars.SelectedItem, PracticeCar).Code
        If opponentCars.SelectedIndex >= 0 Then selected.OpponentCars = {"same", "mixed", "class"}(opponentCars.SelectedIndex)
        selected.Opponents = CInt(opponents.Value) : selected.Laps = CInt(laps.Value)
    End Sub
    Public Sub Save()
        CaptureSelection() : preferences.Save(context)
    End Sub
    Private Sub RefreshRaceOptions()
        modeHint.Text = If(currentPack?.Id = "smelter", "Desktop practice test · Race and VR pending", "Desktop and VR · AI races are experimental · LAN unavailable")
        Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
        If currentPack?.Id = "smelter" AndAlso chosen?.Discipline = "Head-to-head" Then modeHint.Text = "Solo practice on a Head-to-head course; competitive Head-to-head is unavailable."
        If installationValid AndAlso chosen IsNot Nothing AndAlso Not installedLayouts.Contains(chosen.Id) Then modeHint.Text = "This layout is not installed. Choose Manage → Rebuild from source."
        Dim race = launchMode.SelectedIndex = 1
        opponents.Enabled = race : opponentCars.Enabled = race
        Dim vehicle = TryCast(cars.SelectedItem, PracticeCar)
        opponentHint.Text = If(race AndAlso installationValid AndAlso Not raceReady, "Choose Manage → Rebuild from source to update AI driving paths before racing.", If(Not race, "Solo practice. Your race settings are kept for later.", If(opponentCars.SelectedIndex = 2,
            "Opponent class: " & If(vehicle?.ClassName, "Select a car"), If(opponentCars.SelectedIndex = 1, "Mixed: all installed classes.", "All opponents use the same car as the driver."))))
        If Not loading Then RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
    End Sub
    Private Sub RenderState()
        Dim content As Control = If(Not currentPack.Available, CType(unavailable, Control), If(installationValid, CType(setup, Control), installCard))
        If Not body.Controls.Contains(content) Then
            body.Controls.Clear() : body.Controls.Add(content)
        End If
        actions.Controls.Clear()
        If hasReceipt Then actions.Controls.Add(manage)
        If working Then actions.Controls.Add(cancelButton)
        If errorText <> "" Then actions.Controls.Add(errorDetails)
        If working AndAlso Not detail.Controls.Contains(progressBar) Then detail.Controls.Add(progressBar)
        If Not working Then detail.Controls.Remove(progressBar)
        toggle.Enabled = Not working : browser.PackList.Enabled = Not working : browser.Picker.Enabled = Not working
        setup.Enabled = Not working : installButton.Enabled = Not working : manage.Enabled = Not working
        browser.PackList.Invalidate() : PerformLayout()
        RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
    End Sub
    Private Sub SetStatus(packStatus As String, message As String)
        packStatuses(currentPack.Id) = packStatus : status.Text = message : browser.PackList.Invalidate()
    End Sub
    Public Sub CancelOperation()
        cancellation?.Cancel()
        If working Then status.Text = "Cancelling; waiting for the current safe operation to finish…"
    End Sub
    Private Async Function RefreshInstallation() As Task
        If working OrElse Not currentPack.Available Then Return
        Await Operation(Async Function(token, progress)
                            installationValid = False : raceReady = False
                            hasReceipt = File.Exists(SafeFiles.Inside(context.GameRoot, TrackPacks.Get(currentPack.Id).Receipt))
                            If Not hasReceipt Then
                                SetStatus("Not installed", "Not installed · Build once from your own DiRT 3 files.")
                                Return
                            End If
                            SetStatus("Checking…", "Checking installed " & currentPack.Name & " files…")
                            Dim receipt = Await Task.Run(Function() TrackPacks.Get(currentPack.Id).Read(context.GameRoot, True, token), token)
                            CustomTrackService.RequireLauncher(receipt)
                            installationValid = True : raceReady = TrackPacks.Get(currentPack.Id).SupportsRace(receipt)
                            installedLayouts = receipt.Sessions.Select(Function(s) s.LayoutId).ToHashSet(StringComparer.Ordinal)
                            SetStatus("Installed · Ready offline", "Installed · " & currentPack.Name & " " & receipt.Version & " · Ready offline")
                            If Not raceReady AndAlso currentPack.Id = AspenPack.Id Then status.Text &= ". Rebuild from source to enable AI races."
                            If layouts.SelectedItem Is Nothing Then status.Text &= ". Your saved layout is unavailable; choose a layout."
                            If cars.SelectedItem Is Nothing Then status.Text &= ". Your saved car is unavailable; choose an installed car."
                            RefreshRaceOptions()
                        End Function)
    End Function
    Private Async Function InstallPack() As Task
        If working OrElse Not currentPack.Available Then Return
        CaptureSelection()
        Await Operation(Async Function(token, progress)
                            context.RequireClosed()
                            Dim offer = If(currentPack.Id = AspenPack.Id, Aspen.AspenConversion.GetProfile(), Aspen.SmelterConversion.GetProfile()), selected = preferences.ForPack(currentPack.Id)
                            Using picker As New AspenSourceForm(selected.SourceFolder, CustomTrackService.SourceFolders(), offer)
                                If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                                selected.SourceFolder = picker.SourceFolder : preferences.Save(context)
                            End Using
                            SetStatus("Building…", "Building " & currentPack.Name & "…")
                            Await CustomTrackService.InstallAsync(context, offer, selected.SourceFolder, progress, token)
                            installationValid = True : hasReceipt = True : raceReady = TrackPacks.Get(currentPack.Id).Modes.Contains("desktop-race")
                            installedLayouts = offer.Layouts.Select(Function(l) l.Id).ToHashSet(StringComparer.Ordinal) : RefreshRaceOptions()
                            SetStatus("Installed · Ready offline", currentPack.Name & " " & offer.Version & " installed. Choose a layout, then " & If(currentPack.Id = "smelter", "Launch for desktop testing.", "Launch or Launch VR."))
                        End Function)
    End Function
    Private Async Function Operation(action As Func(Of CancellationToken, IProgress(Of TrackProgress), Task)) As Task
        If working Then Return
        ' A saved CUSTOM selection can be restored before Application.Run starts.
        ' Keep verification/progress continuations on the owning UI thread.
        If Not TypeOf SynchronizationContext.Current Is WindowsFormsSynchronizationContext Then SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
        working = True : errorText = "" : progressBar.Value = 0
        cancellation = New CancellationTokenSource() : RenderState()
        Try
            Dim progress As New Progress(Of TrackProgress)(Sub(value)
                                                              If IsDisposed OrElse Not working Then Return
                                                              progressBar.Value = Math.Clamp(value.Percent, 0, 100) : status.Text = value.Message
                                                          End Sub)
            Await action(cancellation.Token, progress)
        Catch ex As OperationCanceledException
            SetStatus(If(installationValid, "Installed · Ready offline", "Not verified"), "Cancelled. Existing tracks are kept. You can verify or build again.")
        Catch ex As Exception
            errorText = ex.Message
            SetStatus(If(installationValid, "Installed · Build failed", "Needs attention"), If(installationValid, "Build failed; your installed pack is still available. ", "This pack could not be verified or built. ") & "Check Details for the affected file and remedy.")
        Finally
            cancellation.Dispose() : cancellation = Nothing : working = False
            If Not IsDisposed Then RenderState()
        End Try
    End Function
    Private Sub DrawPack(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 Then Return
        Dim pack = DirectCast(browser.PackList.Items(e.Index), CustomTrackPack)
        Dim selected = (e.State And DrawItemState.Selected) <> 0
        Dim background = If(selected, SystemColors.Highlight, BackColor)
        Dim foreground = If(selected, If(background.GetBrightness() < 0.5F, Color.White, Color.Black), ForeColor)
        Using brush As New SolidBrush(background)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        Dim bounds = Rectangle.Inflate(e.Bounds, -Px(Me, 10), -Px(Me, 7))
        Using bold As New Font(Font, FontStyle.Bold)
            TextRenderer.DrawText(e.Graphics, pack.Name, bold, bounds, foreground, TextFormatFlags.Top Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End Using
        bounds.Y += Px(Me, 24)
        TextRenderer.DrawText(e.Graphics, If(pack.Id = AspenPack.Id, "4 Rallycross layouts", If(pack.Id = "smelter", "10 layouts · Solo practice", "Saved selection")), Font, bounds, foreground, TextFormatFlags.Top Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        bounds.Y += Px(Me, 24)
        TextRenderer.DrawText(e.Graphics, packStatuses(pack.Id), Font, bounds, foreground, TextFormatFlags.Top Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        e.DrawFocusRectangle()
    End Sub
End Class

Public Class CustomTrackBrowser
    Inherits Panel
    Public ReadOnly PackList As New ListBox With {.Name = "CustomPackList", .DrawMode = DrawMode.OwnerDrawFixed, .IntegralHeight = False, .AccessibleName = "Track packs"}
    Public ReadOnly Picker As New ComboBox With {.Name = "CustomPack", .DropDownStyle = ComboBoxStyle.DropDownList, .AccessibleName = "Track pack"}
    Public ReadOnly Detail As New VerticalStack
    Private ReadOnly compact As SettingRow
    Private arranging As Boolean
    Public Sub New()
        Name = "CustomTrackBrowser" : Margin = New Padding(0, 12, 0, 0)
        compact = New SettingRow("Track pack", Picker)
        Controls.AddRange({PackList, compact, Detail})
        AddHandler Detail.SizeChanged, Sub() PerformLayout()
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse Detail Is Nothing OrElse compact Is Nothing OrElse Width <= 0 Then Return
        arranging = True
        Try
            Dim narrow = Width < Px(Me, 800), gap = Px(Me, 22), listWidth = Px(Me, 215)
            PackList.Visible = Not narrow : compact.Visible = narrow
            PackList.ItemHeight = Px(Me, 88)
            PackList.SetBounds(0, 0, listWidth, Math.Min(PackList.Items.Count, 5) * PackList.ItemHeight + Px(Me, 6))
            compact.SetBounds(0, 0, Width, compact.Height) : compact.PerformLayout()
            Dim left = If(narrow, 0, listWidth + gap), top = If(narrow, compact.Bottom + gap, 0)
            Detail.SetBounds(left, top, Math.Max(1, Width - left), Detail.Height) : Detail.PerformLayout()
            Height = Math.Max(Detail.Bottom, If(narrow, 0, PackList.Bottom))
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, Height)
    End Function
End Class
Public Class AspenSourceForm
    Inherits Form
    Private ReadOnly source As New ComboBox With {.Name = "Dirt3SourceFolder", .DropDownStyle = ComboBoxStyle.DropDown}
    Public ReadOnly Property SourceFolder As String
        Get
            Return source.Text.Trim().Trim(""""c)
        End Get
    End Property
    Public Sub New(previous As String, detected As String(), offer As ConversionProfile)
        Text = "Install " & offer.Name & " — locate DiRT 3" : Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        StyleChoice(source, Me)
        ClientSize = New Size(640, 340) : MinimumSize = New Size(480, 350)
        Dim content As New VerticalStack With {.Dock = DockStyle.Top, .Padding = New Padding(18)}
        content.Controls.Add(New Label With {.Text = "Choose your DiRT 3 Complete Edition folder", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        content.Controls.Add(New Label With {.Text = "Select a detected Steam installation, paste its folder path, or use Browse. The source game stays unchanged.", .AutoSize = False})
        source.Items.AddRange(detected.Cast(Of Object).ToArray())
        source.Text = If(previous <> "", previous, detected.FirstOrDefault())
        content.Controls.Add(source)
        Dim browse As New Button With {.Text = "Browse…", .AutoSize = True}
        AddHandler browse.Click, Sub()
                                     Using picker As New FolderBrowserDialog With {.Description = "Select DiRT 3 Complete Edition", .UseDescriptionForTitle = True, .SelectedPath = SourceFolder}
                                         If picker.ShowDialog(Me) = DialogResult.OK Then source.Text = picker.SelectedPath
                                     End Using
                                 End Sub
        content.Controls.Add(browse)
        content.Controls.Add(New Label With {.Text = $"Conversion tools are included with DiRT2VR. No download is needed. Allow {Math.Ceiling(offer.StagingBytes / 1073741824.0)} GB for building. DiRT 3 is only needed to build or rebuild the tracks.", .AutoSize = False})
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
        Dim install As New Button With {.Text = "Build and install", .AutoSize = True}
        Dim cancel As New Button With {.Text = "Cancel", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        AddHandler install.Click, Sub()
                                      If Not Directory.Exists(IO.Path.Combine(SourceFolder, "tracks/locations/usa/" & If(offer.Id = "smelter", "smelter", "aspen"))) Then
                                          MessageBox.Show(Me, "Choose the DiRT 3 Complete Edition game folder containing the " & offer.Name & " track files.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
                                          Return
                                      End If
                                      DialogResult = DialogResult.OK
                                  End Sub
        buttons.Controls.AddRange({install, cancel}) : content.Controls.Add(buttons)
        Controls.Add(content) : AcceptButton = install : CancelButton = cancel : AutoScroll = True
    End Sub
End Class
