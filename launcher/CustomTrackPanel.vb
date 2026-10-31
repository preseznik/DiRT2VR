Imports DiRT2VR.CustomTracks
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class CustomTrackPanel
    Inherits VerticalStack
    Public Event VehicleSetupRequested As EventHandler
    Private ReadOnly context As InstallContext
    Private ReadOnly verification As CustomTrackVerificationCache
    Private ReadOnly preferences As CustomTrackPreferences
    Private ReadOnly toggle As New CheckBox With {.Text = "CUSTOM tracks (Experimental)", .Name = "CustomTracks", .AutoSize = True}
    Private ReadOnly browser As New CustomTrackBrowser
    Private ReadOnly detail As VerticalStack
    Private ReadOnly title As New Label With {.AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold)}
    Private ReadOnly description As New Label With {.AutoSize = True}
    Private ReadOnly status As New TrackStatusLabel With {.Name = "CustomTrackStatus", .AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold), .Padding = New Padding(8)}
    Private ReadOnly workActions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
    Private ReadOnly setup As New VerticalStack With {.Name = "CustomTrackSetup"}
    Private ReadOnly body As New VerticalStack
    Private ReadOnly installCard As New TrackBuildStack
    Private ReadOnly installHint As New Label With {.AutoSize = True}
    Private ReadOnly bestTime As New Label With {.Name = "CustomBestTime", .AutoSize = True}
    Private ReadOnly unavailable As New Label With {.Name = "CustomPackUnavailable", .AutoSize = False}
    Private ReadOnly layouts As New ComboBox With {.Name = "CustomTrackLayout", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly condition As New Label With {.Name = "CustomTrackCondition", .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly launchMode As New ComboBox With {.Name = "CustomLaunchMode", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private customCars As CustomCarSelection
    Private ReadOnly cars As New ComboBox With {.Name = "CustomCar", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponentCars As New ComboBox With {.Name = "CustomOpponentCars", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly raceDifficulty As New ComboBox With {.Name = "CustomRaceDifficulty", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponents As New ValueSlider("CustomOpponents", 1, 7, 3)
    Private ReadOnly laps As New ValueSlider("CustomLaps", 1, 20, 1)
    Private ReadOnly lightingTest As New ComboBox With {.Name = "ButtermilkLightingTest", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly lightingTestBox As New VerticalStack
    Private ReadOnly modeHint As New Label With {.Name = "CustomModeHint", .AutoSize = True}
    Private ReadOnly opponentHint As New Label With {.AutoSize = False}
    Private ReadOnly rebuildNotice As New TrackStatusLabel With {.Name = "CustomTrackRebuildRequired", .AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold), .Padding = New Padding(8), .UseMnemonic = False}
    Private ReadOnly rebuildCard As New TrackBuildStack With {.Margin = New Padding(0)}
    Private ReadOnly installButton As New Button With {.Text = "Build and install…", .Name = "BuildCustomTrack", .AutoSize = True}
    Private ReadOnly manage As New Button With {.Text = "Manage…", .Name = "ManageCustomTrack", .AutoSize = True}
    Private ReadOnly cancelButton As New Button With {.Text = "Stop build", .Name = "CancelCustomTrack", .AutoSize = True}
    Private ReadOnly errorDetails As New Button With {.Text = "Details…", .Name = "CustomTrackErrorDetails", .AutoSize = True}
    Private ReadOnly progressBar As New ProgressBar With {.Name = "CustomTrackProgress", .Minimum = 0, .Maximum = 100, .Height = 18, .Width = 220}
    Private ReadOnly actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
    Private ReadOnly menu As New ContextMenuStrip
    Private ReadOnly packStatuses As New Dictionary(Of String, String)
    Private ReadOnly catalogue As New List(Of CustomTrackPack)(CustomTrackCatalog.Packs)
    Private ReadOnly catalogueInstalled As New Dictionary(Of String, HashSet(Of String))(StringComparer.Ordinal)
    Private currentPack As CustomTrackPack
    Private cancellation As CancellationTokenSource
    Private installationValid As Boolean
    Private installedLayouts As New HashSet(Of String)(StringComparer.Ordinal)
    Private hasReceipt As Boolean
    Private installedReceipt As PackReceipt
    Private fullGridOpponents As Integer = 3
    Private circuitRaceLaps As Integer = 1
    Private raceReady As Boolean
    Private working As Boolean
    Private loading As Boolean
    Private initialized As Boolean
    Private errorText As String = ""
    Public Event AvailabilityChanged As EventHandler
    Private Class LayoutItem
        Public ReadOnly Value As Layout
        Public Property AssetQuality As String = ""
        Private ReadOnly conditionName As Boolean
        Public Sub New(value As Layout, Optional showCondition As Boolean = False)
            Me.Value = value : conditionName = showCondition
        End Sub
        Public Overrides Function ToString() As String
            Return If(conditionName, Value.Condition, Value.Name) & AssetQuality
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
    Public ReadOnly Property SelectedLayoutId As String
        Get
            Return TryCast(layouts.SelectedItem, LayoutItem)?.Value.Id
        End Get
    End Property
    Public ReadOnly Property CanLaunch As Boolean
        Get
            Return LaunchBlockReason() = ""
        End Get
    End Property
    Public ReadOnly Property CanLaunchVr As Boolean
        Get
            Return LaunchBlockReason(True) = ""
        End Get
    End Property
    Public Function LaunchBlockReason(Optional vr As Boolean = False) As String
        If Not browser.SelectionVisible Then Return "Choose a visible custom track, or change the track filters."
        If currentPack Is Nothing Then Return "Choose a custom track."
        If Not currentPack.Available Then Return If(currentPack.External IsNot Nothing, currentPack.External.Problem, "This custom track is unavailable in this launcher.")
        If working Then Return "Wait for the custom track build to finish, or cancel it."
        If Not installationValid Then Return "Build/install or repair the selected custom track before launching."
        Dim layout = TryCast(layouts.SelectedItem, LayoutItem)?.Value
        If layout Is Nothing Then Return "Choose an installed track layout."
        If Not installedLayouts.Contains(layout.Id) Then Return "This layout is not installed. Choose Manage → Rebuild from source."
        If launchMode.SelectedIndex < 0 Then Return "Choose an available launch mode for this track."
        If cars.SelectedItem Is Nothing Then Return "Choose an installed car."
        If opponentCars.SelectedIndex < 0 Then Return "Choose which cars the opponents should use."
        Dim race = launchMode.SelectedIndex > 0, mode = If(race, "race", "practice"), count = If(race, opponents.Value, 0)
        If race AndAlso Not raceReady Then Return "This layout needs updated AI driving paths. Choose Manage → Rebuild from source before racing."
        If customCars IsNot Nothing Then
            Dim reason = customCars.LaunchBlockReason(layout.Id, mode, vr, count)
            If reason <> "" Then Return reason
        End If
        Try
            TrackPacks.Get(currentPack.Id).RequireMode(layout.Id, vr, mode, DirectCast(cars.SelectedItem, PracticeCar).Code, count, laps.Value, SelectedRaceStart)
            ReplayContent.RequireTrackSupport(context, layout.Id)
            Return ""
        Catch ex As IOException
            Return ex.Message
        End Try
    End Function
    Public Sub New(value As InstallContext)
        context = value : preferences = CustomTrackPreferences.Load(context)
        TrackPacks.DiscoverExternal(context.GameRoot, BuildInfo.Version)
        catalogue.Clear() : catalogue.AddRange(CustomTrackCatalog.Packs)
        verification = New CustomTrackVerificationCache(context.GameRoot)
        Name = "CustomTrackPanel" : detail = browser.Detail
        rebuildCard.Controls.Add(rebuildNotice)
        AddHandler Disposed, Sub() rebuildCard.Dispose()
        For Each choice In {layouts, launchMode, cars, opponentCars, raceDifficulty, browser.Picker, lightingTest}
            StyleChoice(choice, Me)
        Next
        Controls.Add(toggle)
        Dim folders As New ContextMenuStrip
        AddHandler folders.Items.Add("Open external tracks folder").Click, Sub() OpenExternalFolder("track")
        AddHandler folders.Items.Add("Open external cars folder").Click, Sub() OpenExternalFolder("car")
        AddHandler browser.Filters.Folders.Click, Sub() folders.Show(browser.Filters.Folders, New Point(0, browser.Filters.Folders.Height))
        AddHandler Disposed, Sub() folders.Dispose()
        AddHandler browser.Filters.RefreshList.Click, Async Sub()
            If working Then Return
            Try
            CaptureSelection()
            TrackPacks.DiscoverExternal(context.GameRoot, BuildInfo.Version)
            For Each pack In catalogue : verification.Invalidate(pack.Id) : Next
            catalogue.Clear() : catalogue.AddRange(CustomTrackCatalog.Packs)
            currentPack = catalogue.FirstOrDefault(Function(p) p.Id = preferences.SelectedPackId)
            If currentPack Is Nothing Then
                currentPack = New CustomTrackPack(preferences.SelectedPackId, "Unavailable pack", "Your saved pack was removed. Choose another pack or restore its folder.", False, Array.Empty(Of Layout)())
                catalogue.Add(currentPack)
            End If
            For Each pack In catalogue : packStatuses(pack.Id) = If(pack.External IsNot Nothing, If(pack.Available, "External · Not verified", "Needs attention"), "Not verified") : Next
            LoadSelection() : RefreshFilterInventory()
            CustomCarService.Notify(context)
            If CustomEnabled AndAlso currentPack.Available Then Await RefreshInstallation(True)
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Refresh custom tracks", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub
        detail.Controls.Add(title) : detail.Controls.Add(description) : detail.Controls.Add(bestTime) : detail.Controls.Add(status)
        detail.Controls.Add(workActions) : detail.Controls.Add(body)
        Dim layoutColumns As New ResponsiveColumns With {.WideAt = 560}
        Field(layoutColumns.First, "Layout", layouts) : Field(layoutColumns.Second, "Conditions", condition)
        setup.Controls.Add(layoutColumns)
        Dim columns As New ResponsiveColumns With {.WideAt = 560}
        setup.Controls.Add(columns)
        launchMode.Items.AddRange({"Direct practice", "Race"})
        customCars = New CustomCarSelection(context, cars, "custom/" & preferences.SelectedPackId, preferences.ForPack(preferences.SelectedPackId).CarCode)
        AddHandler customCars.Changed, Sub()
                                          If Not loading Then
                                              RefreshRaceOptions() : RefreshBestTime()
                                          End If
                                          RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
                                      End Sub
        opponentCars.Items.AddRange({"Same as driver", "Mixed", "Same class"})
        raceDifficulty.Items.AddRange(DirectRaceDifficulty.Labels)
        Field(columns.First, "Launch mode", launchMode) : Field(columns.First, "Car", customCars)
        Dim vehicleSetup As New Button With {.Text = "Vehicle setup…", .Name = "CustomVehicleSetup", .AutoSize = True}
        columns.First.Controls.Add(vehicleSetup)
        AddHandler vehicleSetup.Click, Sub() RaiseEvent VehicleSetupRequested(Me, EventArgs.Empty)
        Field(columns.Second, "Race difficulty", raceDifficulty)
        Field(columns.Second, "AI opponents", opponents) : Field(columns.Second, "Opponent cars", opponentCars)
        Field(setup, "Laps", laps) : setup.Controls.Add(opponentHint)
        setup.Controls.Add(modeHint)
        lightingTest.Items.AddRange({"Original exposure (reference)", "Bloom off (diagnostic)", "Lower exposure (default)"})
        lightingTest.SelectedIndex = 2
        Field(lightingTestBox, "Snow lighting", lightingTest)
        lightingTestBox.Controls.Add(New Label With {.Name = "ButtermilkLightingHint", .AutoSize = True, .Text = "Lower exposure preserves snow detail; the abrupt brightness border remains. No track rebuild needed. Original effects return after exit. Comparison choices reset to lower exposure when changing layout or reopening the launcher."})
        installCard.Controls.Add(installHint)
        installCard.Controls.Add(installButton)
        detail.Controls.Add(actions)
        detail.Controls.Add(New Label With {.Text = "Turn CUSTOM tracks off to restore your original track, car and race selections.", .AutoSize = False})
        For Each pack In catalogue
            browser.PackList.Items.Add(pack) : browser.Picker.Items.Add(pack)
            packStatuses(pack.Id) = If(pack.External IsNot Nothing, If(pack.Available, "External · Not verified", "Needs attention"), If(pack.Available, If(File.Exists(SafeFiles.Inside(context.GameRoot, TrackPacks.Get(pack.Id).Receipt)), "Installed", "Not installed"), "In development"))
        Next
        currentPack = catalogue.FirstOrDefault(Function(p) p.Id = preferences.SelectedPackId)
        If currentPack Is Nothing Then
            currentPack = New CustomTrackPack(preferences.SelectedPackId, "Unavailable pack", "Your saved pack is not supported by this launcher.", False, Array.Empty(Of Layout)())
            catalogue.Add(currentPack)
            browser.PackList.Items.Add(currentPack) : browser.Picker.Items.Add(currentPack)
            packStatuses(currentPack.Id) = "Unavailable"
        End If
        AddHandler browser.PackList.DrawItem, AddressOf DrawPack
        AddHandler browser.PackList.SelectedIndexChanged, Async Sub() Await SelectPack(TryCast(browser.PackList.SelectedItem, CustomTrackPack))
        AddHandler browser.Picker.SelectedIndexChanged, Async Sub() Await SelectPack(TryCast(browser.Picker.SelectedItem, CustomTrackPack))
        For Each choice In {launchMode, cars, opponentCars, raceDifficulty}
            AddHandler choice.SelectedIndexChanged, Sub() RefreshRaceOptions()
        Next
        AddHandler laps.ValueChanged, Sub()
                                          If Not loading AndAlso OneLapPractice AndAlso launchMode.SelectedIndex = 1 Then circuitRaceLaps = laps.Value
                                      End Sub
        AddHandler layouts.SelectedIndexChanged, Sub()
                                                    lightingTest.SelectedIndex = 2
                                                    Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
                                                    condition.Text = If(chosen Is Nothing, "Choose a layout", chosen.Discipline & " · " & chosen.Condition)
                                                    If currentPack?.Id = "nordschleife" Then condition.Text = "Standard circuit"
                                                    If currentPack?.Id = "misty-loch" Then condition.Text = "Full circuit · 6.45 km"
                                                    If currentPack?.Id = "spa-francorchamps" Then condition.Text = "Circuit · Daylight"
                                                    RefreshBestTime()
                                                    If Not loading Then LoadLayoutSession()
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
        AddHandler menu.Items.Add("Verify installed files").Click, Async Sub() Await RefreshInstallation(True)
        Dim rebuild = menu.Items.Add("Rebuild from source…")
        AddHandler rebuild.Click, Async Sub() Await InstallPack()
        AddHandler menu.Items.Add("Uninstall pack…").Click, Async Sub()
                                                               If Not currentPack.Available OrElse working OrElse currentPack.External IsNot Nothing Then Return
                                                               If MessageBox.Show(Me, "Remove " & currentPack.Name & " and its " & currentPack.Layouts.Length & " layouts? Original tracks, saves and other packs will be kept.", "Uninstall " & currentPack.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
                                                               Await Operation(Async Function(token, progress)
                                                                                   verification.Invalidate(currentPack.Id)
                                                                                   progress.Report(New TrackProgress(0, "Removing installed pack — please wait for safe completion.", False))
                                                                                   Await CustomTrackService.UninstallAsync(context, currentPack.Id)
                                                                                   installationValid = False : hasReceipt = False
                                                                                   SetStatus("Not installed", currentPack.Name & " removed. Your original tracks are available with CUSTOM tracks off.")
                                                                               End Function)
                                                           End Sub
        Dim export = menu.Items.Add("Export pack ZIP…")
        export.Name = "ExportCustomTrack"
        AddHandler export.Click, Sub() PackExportDialog.Export(Me, context, "track", currentPack.Id, currentPack.Name)
        AddHandler menu.Opening, Sub()
                                    rebuild.Visible = currentPack.External Is Nothing
                                    menu.Items(2).Visible = currentPack.External Is Nothing
                                    export.Enabled = Not working AndAlso currentPack.Available AndAlso hasReceipt
                                End Sub
        AddHandler errorDetails.Click, Sub() MessageBox.Show(Me, errorText, currentPack.Name & " — details", MessageBoxButtons.OK, MessageBoxIcon.Information)
        AddHandler Disposed, Sub()
                                 cancellation?.Cancel()
                                 verification.Dispose()
                                 menu.Dispose()
                                 For Each control As Control In New Control() {browser, setup, installCard, unavailable, manage, cancelButton, errorDetails, progressBar, lightingTestBox}
                                     control.Dispose()
                                 Next
                             End Sub
        LoadSelection()
        browser.Filters.Restore(preferences.FilterSourceGames, preferences.InstalledTracksOnly)
        RefreshFilterInventory()
        AddHandler browser.Filters.Changed, Sub()
                                                CaptureSelection()
                                                preferences.FilterSourceGames = browser.Filters.SourceIds
                                                preferences.InstalledTracksOnly = browser.Filters.InstalledOnly.Checked
                                                ApplyFilters()
                                            End Sub
        toggle.Checked = preferences.Enabled
        initialized = True
        AddHandler HandleCreated, Sub()
                                      BeginInvoke(New Action(Async Sub()
                                                                 If IsDisposed Then Return
                                                                 If CustomEnabled Then Await RefreshInstallation()
                                                             End Sub))
                                  End Sub
    End Sub
    Private Sub OpenExternalFolder(kind As String)
        Dim path = SafeFiles.Inside(context.GameRoot, ExternalPacks.RelativeFolder(kind))
        Directory.CreateDirectory(path)
        Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
    End Sub
    Private Async Function SelectPack(pack As CustomTrackPack) As Task
        If loading OrElse working OrElse pack Is Nothing OrElse pack Is currentPack Then Return
        CaptureSelection() : currentPack = pack : preferences.SelectedPackId = pack.Id
        LoadSelection()
        ApplyFilters()
        If initialized AndAlso CustomEnabled Then Await RefreshInstallation()
    End Function
    Private Sub LoadSelection()
        loading = True
        Try
            browser.PackList.SelectedItem = currentPack : browser.Picker.SelectedItem = currentPack
            title.Text = currentPack.Name : description.Text = currentPack.Description
            Dim conditions = currentPack.Id = "nordschleife" OrElse currentPack.Id = "misty-loch"
            layouts.AccessibleName = If(conditions, "Conditions", "Layout")
            setup.Controls.Find("CustomTrackLayoutLabel", True).Single().Text = If(conditions, "Conditions", "Layout")
            setup.Controls.Find("CustomTrackConditionLabel", True).Single().Text = If(conditions, "Layout", "Conditions")
            installHint.Text = If(currentPack.Available, "Build this pack from your own " & TrackPacks.Get(currentPack.Id).SourceName & " files. Select a detected installation or browse to its folder. Installed tracks work offline.", "This saved pack is unavailable.")
            installationValid = False : raceReady = False : installedReceipt = Nothing : hasReceipt = False : errorText = "" : installedLayouts.Clear()
            Dim selected = preferences.ForPack(currentPack.Id)
            circuitRaceLaps = Math.Clamp(selected.Laps, 1, 20)
            layouts.Items.Clear()
            For Each trackLayout In currentPack.Layouts
                layouts.Items.Add(New LayoutItem(trackLayout, conditions))
            Next
            layouts.SelectedItem = layouts.Items.Cast(Of LayoutItem).FirstOrDefault(Function(item) item.Value.Id = selected.LayoutId)
            launchMode.Items.Clear()
            launchMode.Items.Add("Direct practice")
            If currentPack.Available AndAlso TrackPacks.Get(currentPack.Id).Modes.Contains("desktop-race") Then launchMode.Items.Add("Grid race")
            Dim dualStart = currentPack.Available AndAlso layouts.SelectedItem IsNot Nothing AndAlso
                TrackPacks.Get(currentPack.Id).SupportsStaggered(selected.LayoutId) AndAlso TrackPacks.Get(currentPack.Id).SupportsGridRace(selected.LayoutId)
            If dualStart Then launchMode.Items.Add("Grid race")
            launchMode.SelectedIndex = If(selected.LaunchMode = "race", If(launchMode.Items.Count = 1, -1, If(dualStart AndAlso selected.RaceStart = "grid", 2, 1)), 0)
            customCars.SetStockSelection("custom/" & currentPack.Id, selected.CarCode)
            customCars.Grades.Value = selected.CarGrade
            raceDifficulty.SelectedIndex = Math.Clamp(selected.RaceDifficulty, -1, 5) + 1
            opponentCars.SelectedIndex = Array.IndexOf({"same", "mixed", "class"}, selected.OpponentCars)
            opponents.Maximum = 7 : fullGridOpponents = Math.Clamp(selected.Opponents, 1, 7)
            opponents.Value = fullGridOpponents : laps.Value = If(OneLapPractice AndAlso launchMode.SelectedIndex <> 1, 1, circuitRaceLaps)
            status.Text = If(currentPack.Available, "Choose Build and install to prepare this pack.", If(currentPack.Id = "smelter", "In development · Gameplay validation pending", "Unavailable in this launcher"))
            unavailable.Text = If(currentPack.External IsNot Nothing, currentPack.External.Problem, If(currentPack.Id = "smelter", "Smelter is unavailable in this launcher.", "Choose an available pack from the list. Your saved selection has been kept."))
        Finally
            loading = False
        End Try
        LoadLayoutSession() : RefreshRaceOptions() : RefreshBestTime() : RenderState()
    End Sub
    Private Sub LoadLayoutSession()
        If currentPack Is Nothing OrElse Not currentPack.Available OrElse layouts.SelectedItem Is Nothing Then Return
        Dim pack = TrackPacks.Get(currentPack.Id), id = DirectCast(layouts.SelectedItem, LayoutItem).Value.Id
        If opponents.Maximum > 1 Then fullGridOpponents = opponents.Value
        opponents.Maximum = Math.Max(1, pack.MaximumOpponents(id, SelectedRaceStart))
        opponents.Value = Math.Min(fullGridOpponents, opponents.Maximum)
    End Sub
    Private Sub CaptureSelection()
        If currentPack Is Nothing OrElse Not currentPack.Available Then Return
        Dim selected = preferences.ForPack(currentPack.Id)
        If layouts.SelectedItem IsNot Nothing Then selected.LayoutId = DirectCast(layouts.SelectedItem, LayoutItem).Value.Id
        If launchMode.SelectedIndex >= 0 Then selected.LaunchMode = If(launchMode.SelectedIndex = 0, "practice", "race")
        If launchMode.SelectedIndex > 0 Then selected.RaceStart = SelectedRaceStart
        If cars.SelectedItem IsNot Nothing Then selected.CarCode = If(customCars.CustomEnabled, customCars.StockSelection, DirectCast(cars.SelectedItem, PracticeCar).Code)
        selected.CarGrade = customCars.Grades.Value
        If opponentCars.SelectedIndex >= 0 Then selected.OpponentCars = {"same", "mixed", "class"}(opponentCars.SelectedIndex)
        selected.RaceDifficulty = raceDifficulty.SelectedIndex - 1
        selected.Opponents = If(opponents.Maximum = 1, fullGridOpponents, opponents.Value) : selected.Laps = If(OneLapPractice, circuitRaceLaps, laps.Value)
        selected.PostProcessTest = If(ButtermilkPostProcess.Supports(selected.LayoutId), {"normal", "bloom-off", "lower-exposure"}(Math.Max(0, lightingTest.SelectedIndex)), "normal")
    End Sub
    Public Sub Save()
        CaptureSelection() : preferences.Save(context)
    End Sub
    Private Sub RefreshFilterInventory()
        catalogueInstalled.Clear()
        For Each pack In catalogue.Where(Function(p) p.External IsNot Nothing AndAlso Not p.Available)
            catalogueInstalled(pack.Id) = Nothing
        Next
        For Each pack In catalogue.Where(Function(p) p.Available)
            Dim definition = TrackPacks.Get(pack.Id)
            If Not File.Exists(SafeFiles.Inside(context.GameRoot, definition.Receipt)) Then Continue For
            Try
                ' Read the receipt only. Filtering must not rehash gigabytes of track files.
                catalogueInstalled(pack.Id) = definition.Read(context.GameRoot, False).Sessions.Select(Function(s) s.LayoutId).ToHashSet(StringComparer.Ordinal)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Text.Json.JsonException
                ' A damaged receipt is still an installation that the user can repair.
                catalogueInstalled(pack.Id) = Nothing
                packStatuses(pack.Id) = "Needs attention"
            End Try
        Next
        browser.Filters.SetInstalledSources(catalogue.Where(Function(p) catalogueInstalled.ContainsKey(p.Id)).Select(Function(p) p.SourceGame))
        ApplyFilters()
    End Sub
    Private Sub ApplyFilters()
        Dim visible = catalogue.Where(Function(p) browser.Filters.Includes(p, catalogueInstalled.ContainsKey(p.Id))).ToArray()
        Dim selectedLayout = If(TryCast(layouts.SelectedItem, LayoutItem)?.Value.Id, preferences.ForPack(currentPack.Id).LayoutId)
        loading = True
        browser.SuspendLayout()
        Try
            If Not browser.PackList.Items.Cast(Of CustomTrackPack).SequenceEqual(visible) Then
                browser.PackList.BeginUpdate() : browser.PackList.Items.Clear() : browser.PackList.Items.AddRange(visible)
                browser.PackList.EndUpdate()
                browser.Picker.BeginUpdate() : browser.Picker.Items.Clear() : browser.Picker.Items.AddRange(visible)
                browser.Picker.EndUpdate()
            End If
            browser.PackList.SelectedItem = currentPack : browser.Picker.SelectedItem = currentPack
            browser.SelectionVisible = visible.Contains(currentPack)
            browser.EmptyMessage.Text = If(visible.Length = 0, "No tracks match these filters.", "Your selected track is hidden. Choose a track or show all tracks.")
            Dim installed As HashSet(Of String) = Nothing
            catalogueInstalled.TryGetValue(currentPack.Id, installed)
            Dim visibleLayouts = currentPack.Layouts.Where(Function(l) Not browser.Filters.InstalledOnly.Checked OrElse installed Is Nothing OrElse installed.Contains(l.Id)).ToArray()
            If Not layouts.Items.Cast(Of LayoutItem).Select(Function(item) item.Value).SequenceEqual(visibleLayouts) Then
                Dim lighting = lightingTest.SelectedIndex
                layouts.BeginUpdate() : layouts.Items.Clear()
                For Each trackLayout In visibleLayouts
                    layouts.Items.Add(New LayoutItem(trackLayout, currentPack.Id = "nordschleife" OrElse currentPack.Id = "misty-loch"))
                Next
                layouts.SelectedItem = layouts.Items.Cast(Of LayoutItem).FirstOrDefault(Function(item) item.Value.Id = selectedLayout)
                lightingTest.SelectedIndex = lighting : layouts.EndUpdate()
            End If
            For i = 0 To layouts.Items.Count - 1
                Dim item = DirectCast(layouts.Items(i), LayoutItem)
                Dim session = installedReceipt?.Sessions.FirstOrDefault(Function(s) s.LayoutId = item.Value.Id)
                Dim quality = If(currentPack.Id = "pikes-peak" AndAlso session IsNot Nothing, " · " & If(session?.PerformanceAssets, "Performance", "Standard"), "")
                If item.AssetQuality <> quality Then
                    item.AssetQuality = quality : layouts.Items(i) = item
                End If
            Next
        Finally
            loading = False : browser.ResumeLayout(True)
        End Try
        RefreshRaceOptions() : RenderState()
    End Sub
    Public Sub RefreshBestTime()
        detail.Controls.SetChildIndex(bestTime, 2)
        Dim layoutId = TryCast(layouts.SelectedItem, LayoutItem)?.Value.Id, carCode = TryCast(cars.SelectedItem, PracticeCar)?.Code
        Dim text = ""
        If CustomEnabled AndAlso currentPack IsNot Nothing AndAlso currentPack.Available AndAlso layoutId IsNot Nothing Then
            Try
                If customCars IsNot Nothing AndAlso customCars.CustomEnabled Then carCode = Global.DiRT2VR.CustomCarService.SelectedCode(context)
                If carCode IsNot Nothing Then text = BestLapStore.Summary(context, layoutId, carCode, installedReceipt)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                text = ""
            End Try
        End If
        If bestTime.Text <> text Then bestTime.Text = text
        bestTime.Visible = text <> ""
    End Sub
    Private ReadOnly Property OneLapPractice As Boolean
        Get
            Return currentPack?.Id = "nordschleife" OrElse currentPack?.Id = "misty-loch"
        End Get
    End Property
    Private ReadOnly Property SelectedRaceStart As String
        Get
            If launchMode.SelectedIndex = 2 Then Return "grid"
            Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
            If chosen IsNot Nothing AndAlso currentPack IsNot Nothing AndAlso currentPack.Available AndAlso TrackPacks.Get(currentPack.Id).SupportsStaggered(chosen.Id) Then Return "staggered"
            Return "grid"
        End Get
    End Property
    Private Sub RefreshRaceOptions()
        If loading Then Return
        Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
        Dim staggered = chosen IsNot Nothing AndAlso currentPack IsNot Nothing AndAlso currentPack.Available AndAlso TrackPacks.Get(currentPack.Id).SupportsStaggered(chosen.Id)
        Dim dualStart = staggered AndAlso TrackPacks.Get(currentPack.Id).SupportsGridRace(chosen.Id)
        loading = True
        Try
            If dualStart AndAlso launchMode.Items.Count = 2 Then launchMode.Items.Add("Grid race")
            If Not dualStart AndAlso launchMode.Items.Count = 3 Then
                If launchMode.SelectedIndex = 2 Then launchMode.SelectedIndex = 1
                launchMode.Items.RemoveAt(2)
            End If
        Finally
            loading = False
        End Try
        If launchMode.Items.Count > 1 Then
            Dim label = If(staggered, "Rally (staggered)", "Grid race")
            If CStr(launchMode.Items(1)) <> label Then launchMode.Items(1) = label
        End If
        raceReady = installedReceipt IsNot Nothing AndAlso currentPack IsNot Nothing AndAlso TrackPacks.Get(currentPack.Id).SupportsRace(installedReceipt, chosen?.Id)
        If ButtermilkPostProcess.Supports(chosen?.Id) Then
            If Not setup.Controls.Contains(lightingTestBox) Then
                setup.Controls.Add(lightingTestBox) : setup.Controls.SetChildIndex(lightingTestBox, 1)
            End If
        Else
            setup.Controls.Remove(lightingTestBox)
        End If
        Dim soloOnly = currentPack IsNot Nothing AndAlso currentPack.Available AndAlso Not TrackPacks.Get(currentPack.Id).Modes.Contains("desktop-race")
        If soloOnly AndAlso launchMode.SelectedIndex <> 0 Then launchMode.SelectedIndex = 0
        launchMode.Enabled = Not soloOnly
        LoadLayoutSession()
        Dim race = launchMode.SelectedIndex > 0
        Dim pointToPoint = chosen IsNot Nothing AndAlso Not chosen.Circuit
        laps.Enabled = Not pointToPoint AndAlso (Not OneLapPractice OrElse race)
        If pointToPoint Then laps.Value = 1
        If OneLapPractice AndAlso Not loading Then laps.Value = If(race, circuitRaceLaps, 1)
        modeHint.Text = If(OneLapPractice, "Desktop and VR · " & If(race, "1–20 laps", "One lap") & " · VR and AI races are experimental", "Desktop and VR · AI races are experimental · LAN unavailable")
        If pointToPoint Then modeHint.Text = "One complete run · AI races and VR depend on the selected pack · LAN unavailable"
        If currentPack.External IsNot Nothing AndAlso currentPack.Available Then modeHint.Text = "External · " & String.Join(", ", TrackPacks.Get(currentPack.Id).Modes.Select(Function(mode) mode.Replace("desktop-", "Desktop ").Replace("vr-", "VR ").Replace("solo", "Practice").Replace("race", "Race"))) & If(currentPack.External.Manifest.Replay, " · Full Replay supported by author", " · Turn full-race recording off")
        If race AndAlso SelectedRaceStart = "staggered" Then modeHint.Text &= " · Staggered starts with individual stage times"
        If race AndAlso SelectedRaceStart = "grid" Then modeHint.Text &= " · Simultaneous grid start"
        If race Then modeHint.Text &= " · Best times are saved in Direct practice"
        If chosen?.Discipline = "Head-to-head" Then modeHint.Text = "Two-car grid: one AI opponent. Race timing on these separate-lane courses needs testing; knockout Head-to-head rules are unavailable."
        If customCars IsNot Nothing AndAlso customCars.CustomEnabled AndAlso CustomCarPreferences.Load(context).SelectedId = Global.DiRT2VR.CustomCars.CarPack.Code Then modeHint.Text = "Ferrari F2004: Direct practice and Race on compatible tracks · Desktop and VR · AI handling is experimental."
        If currentPack?.Id = "spa-francorchamps" Then modeHint.Text = "Desktop and VR · 1–20 laps · VR and AI races are experimental"
        raceDifficulty.Enabled = race
        opponents.Enabled = race AndAlso opponents.Maximum > 1 : opponentCars.Enabled = race
        Dim vehicle = TryCast(cars.SelectedItem, PracticeCar)
        opponentHint.Text = If(Not race, "Solo practice. Your race settings are kept for later.", If(opponentCars.SelectedIndex = 2,
            "Opponent class: " & If(vehicle?.ClassName, "Select a car"), If(opponentCars.SelectedIndex = 1, "Mixed: all installed classes.", "All opponents use the same car as the driver.")))
        RefreshRebuildNotice()
        If Not loading Then RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
    End Sub
    Private Sub RefreshRebuildNotice()
        Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
        Dim missingLayout = chosen IsNot Nothing AndAlso Not installedLayouts.Contains(chosen.Id)
        Dim required = installationValid AndAlso Not working AndAlso chosen IsNot Nothing AndAlso
            (missingLayout OrElse (launchMode.SelectedIndex > 0 AndAlso Not raceReady))
        rebuildNotice.Pulse = required
        If required Then
            rebuildNotice.Text = "Rebuild required — " & If(missingLayout, "layout not installed", "race unavailable") & vbCrLf &
                "Choose Manage → Rebuild from source" & If(missingLayout, " to install this layout.", " to update AI driving paths.")
            If Not detail.Controls.Contains(rebuildCard) Then
                detail.Controls.Add(rebuildCard)
                detail.Controls.SetChildIndex(rebuildCard, detail.Controls.GetChildIndex(body))
            End If
        Else
            detail.Controls.Remove(rebuildCard)
        End If
    End Sub
    Private Sub RenderState()
        RefreshRebuildNotice()
        Dim content As Control = If(Not currentPack.Available, CType(unavailable, Control), If(installationValid, CType(setup, Control), installCard))
        If Not body.Controls.Contains(content) Then
            body.Controls.Clear() : body.Controls.Add(content)
        End If
        actions.Controls.Clear()
        If hasReceipt Then actions.Controls.Add(manage)
        If working AndAlso Not workActions.Controls.Contains(cancelButton) Then workActions.Controls.Add(cancelButton)
        If Not working Then workActions.Controls.Remove(cancelButton)
        If errorText <> "" Then actions.Controls.Add(errorDetails)
        If working AndAlso Not workActions.Controls.Contains(progressBar) Then workActions.Controls.Add(progressBar)
        If Not working Then workActions.Controls.Remove(progressBar)
        toggle.Enabled = Not working : browser.PackList.Enabled = Not working : browser.Picker.Enabled = Not working
        browser.Filters.Enabled = Not working
        setup.Enabled = Not working : installButton.Enabled = Not working : manage.Enabled = Not working
        installButton.Visible = currentPack.External Is Nothing
        browser.PackList.Invalidate() : PerformLayout()
        RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
    End Sub
    Private Sub SetStatus(packStatus As String, message As String)
        packStatuses(currentPack.Id) = packStatus : status.Text = message : browser.PackList.Invalidate()
    End Sub
    Public Sub CancelOperation()
        If Not working OrElse Not cancelButton.Enabled Then Return
        cancelButton.Enabled = False : cancellation?.Cancel()
        status.Text = "Stopping safely… Your installed tracks will be kept."
    End Sub
    Private Async Function RefreshInstallation(Optional force As Boolean = False) As Task
        If working OrElse Not currentPack.Available Then Return
        If force OrElse CustomTrackService.RecoveryPending(context) Then verification.Invalidate(currentPack.Id)
        Dim cached = verification.TryGet(currentPack.Id)
        If cached IsNot Nothing Then
            ApplyVerifiedReceipt(cached) : RenderState() : Return
        End If
        Await Operation(Async Function(token, progress)
                            installationValid = False : raceReady = False : installedReceipt = Nothing
                            hasReceipt = File.Exists(SafeFiles.Inside(context.GameRoot, TrackPacks.Get(currentPack.Id).Receipt))
                            If Not hasReceipt Then
                                SetStatus("Not installed", "Not installed · Build once from your own " & TrackPacks.Get(currentPack.Id).SourceName & " files.")
                                Return
                            End If
                            SetStatus("Checking…", "Checking installed " & currentPack.Name & " files…")
                            Dim receipt = Await Task.Run(Function() verification.Read(TrackPacks.Get(currentPack.Id), token), token)
                            ApplyVerifiedReceipt(receipt)
                        End Function)
    End Function
    Private Sub ApplyVerifiedReceipt(receipt As PackReceipt)
        errorText = ""
        hasReceipt = True : installationValid = True : installedReceipt = receipt : raceReady = TrackPacks.Get(currentPack.Id).SupportsRace(receipt, preferences.ForPack(currentPack.Id).LayoutId)
        installedLayouts = receipt.Sessions.Select(Function(s) s.LayoutId).ToHashSet(StringComparer.Ordinal)
        SetStatus("Installed · Ready offline", receipt.Sessions.Length.ToString() & " of " & currentPack.Layouts.Length.ToString() & " layouts installed · Ready offline")
        If currentPack.Id = "misty-loch" OrElse currentPack.Id = "nordschleife" Then status.Text &= " · " & If(receipt.HighQualityAssets, "High quality assets", "Standard assets")
        If currentPack.External IsNot Nothing Then status.Text &= " · External pack; author-declared modes"
        If Not raceReady AndAlso currentPack.Id = AspenPack.Id Then status.Text &= ". Rebuild from source to enable AI races."
        If layouts.SelectedItem Is Nothing Then status.Text &= ". Your saved layout is unavailable; choose a layout."
        If cars.SelectedItem Is Nothing Then status.Text &= ". Your saved car is unavailable; choose an installed car."
        RefreshRaceOptions()
    End Sub
    Private Async Function InstallPack() As Task
        If working OrElse Not currentPack.Available OrElse currentPack.External IsNot Nothing Then Return
        CaptureSelection()
        Await Operation(Async Function(token, progress)
                            context.RequireClosed()
                            Dim offer = CustomTrackService.Profile(currentPack.Id), selected = preferences.ForPack(currentPack.Id)
                            Using picker As New AspenSourceForm(selected.SourceFolder, CustomTrackService.SourceFolders(currentPack.Id), offer, selected.BuildLayoutIds, installedReceipt, selected.HighQualityAssets, selected.PerformanceAssets)
                                If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                                selected.SourceFolder = picker.SourceFolder : selected.BuildLayoutIds = picker.SelectedLayoutIds : selected.HighQualityAssets = picker.HighQualityAssets : selected.PerformanceAssets = picker.PerformanceAssets : preferences.Save(context)
                            End Using
                            SetStatus("Building…", "Building " & selected.BuildLayoutIds.Length.ToString() & " selected " & currentPack.Name & " layouts…")
                            verification.Invalidate(currentPack.Id)
                            Await CustomTrackService.InstallAsync(context, offer, selected.SourceFolder, progress, token, selected.BuildLayoutIds, selected.HighQualityAssets, selected.PerformanceAssets)
                            ' InstallAsync has committed successfully; a late stop must not misreport it as cancelled.
                            installedReceipt = Await Task.Run(Function() verification.Read(TrackPacks.Get(currentPack.Id), CancellationToken.None))
                            installationValid = True : hasReceipt = True
                            installedLayouts = installedReceipt.Sessions.Select(Function(s) s.LayoutId).ToHashSet(StringComparer.Ordinal) : RefreshRaceOptions()
                            SetStatus("Installed · Ready offline", selected.BuildLayoutIds.Length.ToString() & " layouts built · " & installedLayouts.Count.ToString() & " of " & offer.Layouts.Length.ToString() & " installed. Choose a layout, then Launch.")
                            If currentPack.Id = "misty-loch" OrElse currentPack.Id = "nordschleife" Then status.Text &= " · " & If(installedReceipt.HighQualityAssets, "High quality assets", "Standard assets")
                        End Function)
    End Function
    Private Async Function Operation(action As Func(Of CancellationToken, IProgress(Of TrackProgress), Task)) As Task
        If working Then Return
        ' A saved CUSTOM selection can be restored before Application.Run starts.
        ' Keep verification/progress continuations on the owning UI thread.
        If Not TypeOf SynchronizationContext.Current Is WindowsFormsSynchronizationContext Then SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
        working = True : errorText = "" : progressBar.Value = 0
        cancellation = New CancellationTokenSource() : cancelButton.Enabled = True : RenderState()
        Dim operationCancellation = cancellation
        Try
            Dim progress As New Progress(Of TrackProgress)(Sub(value)
                                                              If IsDisposed OrElse Not working OrElse cancellation IsNot operationCancellation Then Return
                                                              cancelButton.Enabled = value.CanCancel AndAlso Not operationCancellation.IsCancellationRequested
                                                              If operationCancellation.IsCancellationRequested AndAlso value.CanCancel Then Return
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
            If Not IsDisposed Then RefreshFilterInventory()
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
        Dim subtitle = If(pack.External IsNot Nothing, "External · " & If(pack.Available, pack.Layouts.Length & " layouts", "Needs attention"), If(pack.Id = "nordschleife", "Standard circuit · 20.7 km", If(pack.Id = "mizu-mountain", "Point-to-point · 10.4 km · Race and VR", If(pack.Id = "misty-loch", "Full circuit · 6.45 km · Race and VR", If(pack.Available, pack.Layouts.Length & " layouts · Race and VR", "Saved selection")))))
        If pack.Id = "spa-francorchamps" Then subtitle = "GP circuit · 7 km · Race and VR"
        TextRenderer.DrawText(e.Graphics, subtitle, Font, bounds, foreground, TextFormatFlags.Top Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        bounds.Y += Px(Me, 24)
        TextRenderer.DrawText(e.Graphics, packStatuses(pack.Id), Font, bounds, foreground, TextFormatFlags.Top Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        e.DrawFocusRectangle()
    End Sub
End Class

Public Class CustomTrackBrowser
    Inherits Panel
    Public ReadOnly Filters As New CustomTrackFilters
    Public ReadOnly EmptyMessage As New Label With {.AutoSize = True, .Name = "CustomTrackFilterMessage"}
    Public SelectionVisible As Boolean = True
    Public ReadOnly PackList As New ListBox With {.Name = "CustomPackList", .DrawMode = DrawMode.OwnerDrawFixed, .IntegralHeight = False, .AccessibleName = "Track packs"}
    Public ReadOnly Picker As New ComboBox With {.Name = "CustomPack", .DropDownStyle = ComboBoxStyle.DropDownList, .AccessibleName = "Track pack"}
    Public ReadOnly Detail As New TrackBuildStack
    Private ReadOnly compact As SettingRow
    Private ReadOnly empty As New TrackBuildStack
    Private arranging As Boolean
    Private viewport As ScrollableControl
    Public Sub New()
        Name = "CustomTrackBrowser" : Margin = New Padding(0, 12, 0, 0)
        compact = New SettingRow("Track pack", Picker)
        Dim reset As New Button With {.Text = "Show all tracks", .Name = "ResetCustomTrackFilters", .AutoSize = True}
        empty.Controls.AddRange({EmptyMessage, reset})
        AddHandler reset.Click, Sub() Filters.ShowAll()
        Controls.AddRange({Filters, PackList, compact, Detail, empty})
        AddHandler Detail.SizeChanged, Sub() PerformLayout()
        AddHandler Filters.SizeChanged, Sub() PerformLayout()
    End Sub
    Private Sub ViewportResized(sender As Object, e As EventArgs)
        PerformLayout()
    End Sub
    Private Function AvailableListHeight(listTop As Integer, contentHeight As Integer) As Integer
        Dim ancestor = Parent
        While ancestor IsNot Nothing AndAlso Not (TypeOf ancestor Is ScrollableControl AndAlso DirectCast(ancestor, ScrollableControl).AutoScroll)
            ancestor = ancestor.Parent
        End While
        Dim current = TryCast(ancestor, ScrollableControl)
        If current IsNot viewport Then
            If viewport IsNot Nothing Then RemoveHandler viewport.ClientSizeChanged, AddressOf ViewportResized
            viewport = current
            If viewport IsNot Nothing Then AddHandler viewport.ClientSizeChanged, AddressOf ViewportResized
        End If
        If viewport Is Nothing Then Return contentHeight
        Dim offset = listTop, child As Control = Me
        While child IsNot viewport
            offset += child.Top : child = child.Parent
        End While
        ' Use the unscrolled position so scrolling the page cannot grow the list.
        offset -= viewport.AutoScrollPosition.Y
        Return Math.Min(contentHeight, Math.Max(PackList.ItemHeight + Px(Me, 6), viewport.ClientSize.Height - viewport.Padding.Bottom - offset))
    End Function
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso viewport IsNot Nothing Then RemoveHandler viewport.ClientSizeChanged, AddressOf ViewportResized
        MyBase.Dispose(disposing)
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse Detail Is Nothing OrElse compact Is Nothing OrElse Width <= 0 Then Return
        arranging = True
        Try
            Dim narrow = Width < Px(Me, 800), gap = Px(Me, 22), listWidth = Px(Me, 215)
            Filters.SetBounds(0, 0, Width, Filters.GetPreferredSize(New Size(Width, 0)).Height) : Filters.PerformLayout()
            Dim listTop = Filters.Bottom + Px(Me, 12)
            PackList.Visible = Not narrow : compact.Visible = narrow
            PackList.ItemHeight = Px(Me, 88)
            Dim listHeight = AvailableListHeight(listTop, PackList.Items.Count * PackList.ItemHeight + Px(Me, 6))
            PackList.SetBounds(0, listTop, listWidth, listHeight)
            compact.SetBounds(0, listTop, Width, compact.Height) : compact.PerformLayout()
            Dim left = If(narrow, 0, listWidth + gap), top = If(narrow, compact.Bottom + gap, listTop)
            Detail.Visible = SelectionVisible : empty.Visible = Not SelectionVisible
            Detail.SetBounds(left, top, Math.Max(1, Width - left), Detail.Height) : Detail.PerformLayout()
            empty.SetBounds(left, top, Math.Max(1, Width - left), empty.Height) : empty.PerformLayout()
            Height = Math.Max(If(SelectionVisible, Detail.Bottom, empty.Bottom), If(narrow, compact.Bottom, PackList.Bottom))
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
    Private ReadOnly choices As New List(Of CheckBox)
    Private ReadOnly quality As New CheckBox With {.Name = "BuildHighQualityAssets", .Text = "Build with high quality assets", .AutoSize = True}
    Private ReadOnly performance As New CheckBox With {.Name = "BuildPerformanceAssets", .Text = "Build performance version", .AutoSize = True}
    Public ReadOnly Property PerformanceAssets As Boolean
        Get
            Return performance.Checked
        End Get
    End Property
    Public ReadOnly Property HighQualityAssets As Boolean
        Get
            Return quality.Checked
        End Get
    End Property
    Private ReadOnly selectionStatus As New Label With {.Name = "BuildLayoutCount", .AutoSize = True}
    Private ReadOnly install As New Button With {.Name = "ConfirmTrackBuild", .Text = "Build and install", .AutoSize = True}
    Public ReadOnly Property SelectedLayoutIds As String()
        Get
            Return choices.Where(Function(c) c.Checked).Select(Function(c) CStr(c.Tag)).ToArray()
        End Get
    End Property
    Private Sub UpdateSelection()
        Dim count = SelectedLayoutIds.Length
        selectionStatus.Text = count.ToString() & " of " & choices.Count.ToString() & " layouts selected"
        install.Enabled = count > 0
    End Sub
    Public ReadOnly Property SourceFolder As String
        Get
            Return source.Text.Trim().Trim(""""c)
        End Get
    End Property
    Public Sub New(previous As String, detected As String(), offer As ConversionProfile, Optional selectedIds As String() = Nothing, Optional installed As PackReceipt = Nothing, Optional highQualityAssets As Boolean = False, Optional performanceAssets As Boolean = False)
        Dim conditions = offer.Id = "nordschleife" OrElse offer.Id = "misty-loch"
        Dim sourceGame = TrackPacks.Get(offer.Id).SourceName
        source.Name = If(TrackPacks.Get(offer.Id).SourceGame = "assettocorsa", "AssettoCorsaSourceFolder", If(TrackPacks.Get(offer.Id).SourceGame = "grid2", "Grid2SourceFolder", "Dirt3SourceFolder"))
        If TrackPacks.Get(offer.Id).SourceGame = "dirtrally" Then source.Name = "DirtRallySourceFolder"
        Text = "Build " & offer.Name & " layouts — " & sourceGame : Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi : StartPosition = FormStartPosition.CenterParent
        StyleChoice(source, Me)
        ClientSize = New Size(720, 690) : MinimumSize = New Size(480, 440)
        Dim content As New TrackBuildStack With {.Dock = DockStyle.Top, .Padding = New Padding(18)}
        content.Controls.Add(New Label With {.Text = "Choose your " & sourceGame & " folder", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        content.Controls.Add(New Label With {.Text = "Select a detected installation, paste its folder path, or use Browse.", .AutoSize = True})
        source.Items.AddRange(detected.Cast(Of Object).ToArray())
        source.Text = If(previous <> "", previous, detected.FirstOrDefault())
        content.Controls.Add(source)
        Dim browse As New Button With {.Text = "Browse…", .AutoSize = True}
        AddHandler browse.Click, Sub()
                                     Using picker As New FolderBrowserDialog With {.Description = "Select " & sourceGame, .UseDescriptionForTitle = True, .SelectedPath = SourceFolder}
                                         If picker.ShowDialog(Me) = DialogResult.OK Then source.Text = picker.SelectedPath
                                     End Using
                                 End Sub
        content.Controls.Add(browse)
        content.Controls.Add(New Label With {.Text = "Layouts to build", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        content.Controls.Add(New Label With {.Text = If(conditions, "All lighting presets are built together.", "Only checked layouts are built. Unchecked layouts already installed will be kept."), .AutoSize = True})
        Dim selectionButtons As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
        Dim all As New Button With {.Name = "SelectAllLayouts", .Text = "Select all", .AutoSize = True}
        Dim none As New Button With {.Name = "SelectNoLayouts", .Text = "Select none", .AutoSize = True}
        AddHandler all.Click, Sub()
                                  For Each choice In choices : choice.Checked = True : Next
                              End Sub
        AddHandler none.Click, Sub()
                                   For Each choice In choices : choice.Checked = False : Next
                               End Sub
        selectionButtons.Controls.AddRange({all, none}) : selectionButtons.Visible = Not conditions : content.Controls.Add(selectionButtons)
        For Each trackLayout In offer.Layouts
            Dim session = installed?.Sessions.FirstOrDefault(Function(s) s.LayoutId = trackLayout.Id)
            Dim status = If(session Is Nothing, "Not installed", "Installed")
            If offer.Id = "pikes-peak" AndAlso session IsNot Nothing Then status &= " · " & If(session.PerformanceAssets, "Performance", "Standard")
            Dim choice As New CheckBox With {.Name = "BuildLayout_" & trackLayout.Id, .Tag = trackLayout.Id, .AutoSize = True,
                .Text = If(conditions, trackLayout.Condition, trackLayout.Name) & " · " & status,
                .Checked = conditions OrElse selectedIds Is Nothing OrElse selectedIds.Contains(trackLayout.Id, StringComparer.Ordinal), .Enabled = Not conditions, .AccessibleName = If(conditions, trackLayout.Condition, trackLayout.Name)}
            choices.Add(choice) : content.Controls.Add(choice)
            AddHandler choice.CheckedChanged, Sub() UpdateSelection()
        Next
        If offer.Id = "misty-loch" OrElse offer.Id = "nordschleife" Then
            content.Controls.Add(New Label With {.Name = "InstalledAssetQuality", .Text = "Installed assets: " & If(installed Is Nothing, "Not installed", If(installed.HighQualityAssets, "High quality", "Standard")), .AutoSize = True})
            quality.Checked = highQualityAssets
            content.Controls.Add(quality)
            content.Controls.Add(New Label With {.Text = If(offer.Id = "nordschleife", "Source ambient shading, materials and scenery shadows. Uses the local CSP ambient patch when available; higher GPU and memory use. Requires rebuilding.", "More faithful materials and denser grass. Higher GPU and memory use; requires rebuilding."), .AutoSize = True})
        End If
        If offer.Id = "pikes-peak" Then
            performance.Checked = performanceAssets
            content.Controls.Add(performance)
            content.Controls.Add(New Label With {.Text = "Keeps every tree, with simpler distant trees and smaller scenery textures. Applies to checked layouts on desktop and in VR. Changing this option requires rebuilding those layouts.", .AutoSize = True})
        End If
        content.Controls.Add(New Label With {.Text = $"Conversion runs locally. Allow up to {Math.Ceiling(offer.StagingBytes / 1073741824.0)} GB of working space. Your original games stay unchanged.", .AutoSize = True})
        UpdateSelection()
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
        Dim cancel As New Button With {.Text = "Cancel", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        AddHandler install.Click, Sub()
                                      If SelectedLayoutIds.Length = 0 Then Return
                                      If Not Directory.Exists(IO.Path.Combine(SourceFolder, CustomTrackService.SourceTrackFolder(offer.Id))) Then
                                          MessageBox.Show(Me, "Choose the " & sourceGame & " game folder containing the " & offer.Name & " track files.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
                                          Return
                                      End If
                                      DialogResult = DialogResult.OK
                                  End Sub
        buttons.Controls.AddRange({install, cancel})
        Dim footer As New TrackBuildStack With {.Dock = DockStyle.Bottom, .Padding = New Padding(18, 8, 18, 12)}
        footer.Controls.Add(selectionStatus) : footer.Controls.Add(buttons)
        Dim viewport As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        viewport.Controls.Add(content)
        Controls.Add(viewport) : Controls.Add(footer)
        AcceptButton = install : CancelButton = cancel
    End Sub
End Class

' Constrain wrapping to this dialog without changing the shared launcher layout.
Public Class TrackBuildStack
    Inherits VerticalStack
    Private arranging As Boolean
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        If arranging Then Return
        arranging = True
        Try
            For Each child As Control In Controls
                If TypeOf child Is Label OrElse TypeOf child Is CheckBox Then
                    Dim width = Math.Max(1, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal)
                    child.MaximumSize = New Size(width, 0)
                    child.Height = child.GetPreferredSize(New Size(width, 0)).Height
                End If
            Next
            MyBase.OnLayout(e)
        Finally
            arranging = False
        End Try
    End Sub
End Class
