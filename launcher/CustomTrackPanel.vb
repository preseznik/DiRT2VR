Imports DiRT2VR.CustomTracks
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class CustomTrackPanel
    Inherits VerticalStack
    Private ReadOnly context As InstallContext
    Private ReadOnly verification As CustomTrackVerificationCache
    Private ReadOnly preferences As CustomTrackPreferences
    Private ReadOnly toggle As New CheckBox With {.Text = "CUSTOM tracks (Experimental)", .Name = "CustomTracks", .AutoSize = True}
    Private ReadOnly browser As New CustomTrackBrowser
    Private ReadOnly detail As VerticalStack
    Private ReadOnly title As New Label With {.AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold)}
    Private ReadOnly description As New Label With {.AutoSize = False}
    Private ReadOnly status As New TrackStatusLabel With {.Name = "CustomTrackStatus", .AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold), .Padding = New Padding(8)}
    Private ReadOnly workActions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
    Private ReadOnly setup As New VerticalStack With {.Name = "CustomTrackSetup"}
    Private ReadOnly body As New VerticalStack
    Private ReadOnly installCard As New VerticalStack
    Private ReadOnly installHint As New Label With {.AutoSize = False}
    Private ReadOnly bestTime As New Label With {.Name = "CustomBestTime", .AutoSize = True}
    Private ReadOnly unavailable As New Label With {.Name = "CustomPackUnavailable", .AutoSize = False}
    Private ReadOnly layouts As New ComboBox With {.Name = "CustomTrackLayout", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly condition As New Label With {.Name = "CustomTrackCondition", .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft}
    Private ReadOnly launchMode As New ComboBox With {.Name = "CustomLaunchMode", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cars As New ComboBox With {.Name = "CustomCar", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponentCars As New ComboBox With {.Name = "CustomOpponentCars", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly raceDifficulty As New ComboBox With {.Name = "CustomRaceDifficulty", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponents As New ValueSlider("CustomOpponents", 1, 7, 3)
    Private ReadOnly laps As New ValueSlider("CustomLaps", 1, 20, 1)
    Private ReadOnly lightingTest As New ComboBox With {.Name = "ButtermilkLightingTest", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly lightingTestBox As New VerticalStack
    Private ReadOnly modeHint As New Label With {.Name = "CustomModeHint", .AutoSize = True}
    Private ReadOnly opponentHint As New Label With {.AutoSize = False}
    Private ReadOnly installButton As New Button With {.Text = "Build and install…", .Name = "BuildCustomTrack", .AutoSize = True}
    Private ReadOnly manage As New Button With {.Text = "Manage…", .Name = "ManageCustomTrack", .AutoSize = True}
    Private ReadOnly cancelButton As New Button With {.Text = "Stop build", .Name = "CancelCustomTrack", .AutoSize = True}
    Private ReadOnly errorDetails As New Button With {.Text = "Details…", .Name = "CustomTrackErrorDetails", .AutoSize = True}
    Private ReadOnly progressBar As New ProgressBar With {.Name = "CustomTrackProgress", .Minimum = 0, .Maximum = 100, .Height = 18, .Width = 220}
    Private ReadOnly actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
    Private ReadOnly menu As New ContextMenuStrip
    Private ReadOnly packStatuses As New Dictionary(Of String, String)
    Private currentPack As CustomTrackPack
    Private cancellation As CancellationTokenSource
    Private installationValid As Boolean
    Private installedLayouts As New HashSet(Of String)(StringComparer.Ordinal)
    Private hasReceipt As Boolean
    Private installedReceipt As PackReceipt
    Private fullGridOpponents As Integer = 3
    Private nordRaceLaps As Integer = 1
    Private raceReady As Boolean
    Private working As Boolean
    Private loading As Boolean
    Private initialized As Boolean
    Private errorText As String = ""
    Public Event AvailabilityChanged As EventHandler
    Private Class LayoutItem
        Public ReadOnly Value As Layout
        Private ReadOnly conditionName As Boolean
        Public Sub New(value As Layout, Optional showCondition As Boolean = False)
            Me.Value = value : conditionName = showCondition
        End Sub
        Public Overrides Function ToString() As String
            Return If(conditionName, Value.Condition, Value.Name)
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
        verification = New CustomTrackVerificationCache(context.GameRoot)
        Name = "CustomTrackPanel" : detail = browser.Detail
        For Each choice In {layouts, launchMode, cars, opponentCars, raceDifficulty, browser.Picker, lightingTest}
            StyleChoice(choice, Me)
        Next
        Controls.Add(toggle)
        detail.Controls.Add(title) : detail.Controls.Add(description) : detail.Controls.Add(bestTime) : detail.Controls.Add(status)
        detail.Controls.Add(workActions) : detail.Controls.Add(body)
        Dim layoutColumns As New ResponsiveColumns With {.WideAt = 560}
        Field(layoutColumns.First, "Layout", layouts) : Field(layoutColumns.Second, "Conditions", condition)
        setup.Controls.Add(layoutColumns)
        Dim columns As New ResponsiveColumns With {.WideAt = 560}
        setup.Controls.Add(columns)
        launchMode.Items.AddRange({"Direct practice", "Race"})
        cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
        opponentCars.Items.AddRange({"Same as driver", "Mixed", "Same class"})
        raceDifficulty.Items.AddRange(DirectRaceDifficulty.Labels)
        Field(columns.First, "Launch mode", launchMode) : Field(columns.First, "Car", cars)
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
        For Each choice In {launchMode, cars, opponentCars, raceDifficulty}
            AddHandler choice.SelectedIndexChanged, Sub() RefreshRaceOptions()
        Next
        AddHandler laps.ValueChanged, Sub()
                                          If Not loading AndAlso currentPack?.Id = "nordschleife" AndAlso launchMode.SelectedIndex = 1 Then nordRaceLaps = laps.Value
                                      End Sub
        AddHandler layouts.SelectedIndexChanged, Sub()
                                                    lightingTest.SelectedIndex = 2
                                                    Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
                                                    condition.Text = If(chosen Is Nothing, "Choose a layout", chosen.Discipline & " · " & chosen.Condition)
                                                    If currentPack?.Id = "nordschleife" Then condition.Text = "Standard circuit"
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
        AddHandler menu.Items.Add("Rebuild from source…").Click, Async Sub() Await InstallPack()
        AddHandler menu.Items.Add("Uninstall pack…").Click, Async Sub()
                                                               If Not currentPack.Available OrElse working Then Return
                                                               If MessageBox.Show(Me, "Remove " & currentPack.Name & " and its " & currentPack.Layouts.Length & " layouts? Original tracks, saves and other packs will be kept.", "Uninstall " & currentPack.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
                                                               Await Operation(Async Function(token, progress)
                                                                                   verification.Invalidate(currentPack.Id)
                                                                                   progress.Report(New TrackProgress(0, "Removing installed pack — please wait for safe completion.", False))
                                                                                   Await CustomTrackService.UninstallAsync(context, currentPack.Id)
                                                                                   installationValid = False : hasReceipt = False
                                                                                   SetStatus("Not installed", currentPack.Name & " removed. Your original tracks are available with CUSTOM tracks off.")
                                                                               End Function)
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
            Dim nord = currentPack.Id = "nordschleife"
            layouts.AccessibleName = If(nord, "Conditions", "Layout")
            setup.Controls.Find("CustomTrackLayoutLabel", True).Single().Text = If(nord, "Conditions", "Layout")
            setup.Controls.Find("CustomTrackConditionLabel", True).Single().Text = If(nord, "Layout", "Conditions")
            installHint.Text = "Build this pack from your own " & TrackPacks.Get(currentPack.Id).SourceName & " files. Select a detected installation or browse to its folder. Installed tracks work offline."
            installationValid = False : raceReady = False : installedReceipt = Nothing : hasReceipt = False : errorText = "" : installedLayouts.Clear()
            Dim selected = preferences.ForPack(currentPack.Id)
            nordRaceLaps = Math.Clamp(selected.Laps, 1, 20)
            layouts.Items.Clear()
            For Each trackLayout In currentPack.Layouts
                layouts.Items.Add(New LayoutItem(trackLayout, nord))
            Next
            layouts.SelectedItem = layouts.Items.Cast(Of LayoutItem).FirstOrDefault(Function(item) item.Value.Id = selected.LayoutId)
            launchMode.Items.Clear()
            launchMode.Items.Add("Direct practice")
            If currentPack.Available AndAlso TrackPacks.Get(currentPack.Id).Modes.Contains("desktop-race") Then launchMode.Items.Add("Race")
            cars.Items.Clear()
            cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
            launchMode.SelectedIndex = If(selected.LaunchMode = "race" AndAlso launchMode.Items.Count = 1, -1, Array.IndexOf({"practice", "race"}, selected.LaunchMode))
            cars.SelectedItem = cars.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) c.Code = selected.CarCode)
            raceDifficulty.SelectedIndex = Math.Clamp(selected.RaceDifficulty, -1, 5) + 1
            opponentCars.SelectedIndex = Array.IndexOf({"same", "mixed", "class"}, selected.OpponentCars)
            opponents.Maximum = 7 : fullGridOpponents = Math.Clamp(selected.Opponents, 1, 7)
            opponents.Value = fullGridOpponents : laps.Value = If(nord AndAlso launchMode.SelectedIndex <> 1, 1, nordRaceLaps)
            status.Text = If(currentPack.Available, "Choose Build and install to prepare this pack.", If(currentPack.Id = "smelter", "In development · Gameplay validation pending", "Unavailable in this launcher"))
            unavailable.Text = If(currentPack.Id = "smelter", "Smelter is unavailable in this launcher.", "Choose an available pack from the list. Your saved selection has been kept.")
        Finally
            loading = False
        End Try
        LoadLayoutSession() : RefreshRaceOptions() : RefreshBestTime() : RenderState()
    End Sub
    Private Sub LoadLayoutSession()
        If currentPack Is Nothing OrElse Not currentPack.Available OrElse layouts.SelectedItem Is Nothing Then Return
        Dim pack = TrackPacks.Get(currentPack.Id), id = DirectCast(layouts.SelectedItem, LayoutItem).Value.Id
        If opponents.Maximum > 1 Then fullGridOpponents = opponents.Value
        opponents.Maximum = pack.MaximumOpponents(id)
        opponents.Value = Math.Min(fullGridOpponents, opponents.Maximum)
    End Sub
    Private Sub CaptureSelection()
        If currentPack Is Nothing OrElse Not currentPack.Available Then Return
        Dim selected = preferences.ForPack(currentPack.Id)
        If layouts.SelectedItem IsNot Nothing Then selected.LayoutId = DirectCast(layouts.SelectedItem, LayoutItem).Value.Id
        If launchMode.SelectedIndex >= 0 Then selected.LaunchMode = {"practice", "race"}(launchMode.SelectedIndex)
        If cars.SelectedItem IsNot Nothing Then selected.CarCode = DirectCast(cars.SelectedItem, PracticeCar).Code
        If opponentCars.SelectedIndex >= 0 Then selected.OpponentCars = {"same", "mixed", "class"}(opponentCars.SelectedIndex)
        selected.RaceDifficulty = raceDifficulty.SelectedIndex - 1
        selected.Opponents = If(opponents.Maximum = 1, fullGridOpponents, opponents.Value) : selected.Laps = If(currentPack.Id = "nordschleife", nordRaceLaps, laps.Value)
        selected.PostProcessTest = If(ButtermilkPostProcess.Supports(selected.LayoutId), {"normal", "bloom-off", "lower-exposure"}(Math.Max(0, lightingTest.SelectedIndex)), "normal")
    End Sub
    Public Sub Save()
        CaptureSelection() : preferences.Save(context)
    End Sub
    Public Sub RefreshBestTime()
        bestTime.Visible = currentPack?.Id = "nordschleife"
        detail.Controls.SetChildIndex(bestTime, 2)
        If currentPack?.Id <> "nordschleife" Then Return
        Dim layoutId = If(TryCast(layouts.SelectedItem, LayoutItem)?.Value.Id, TrackPacks.Nordschleife.Layouts(0).Id)
        Try
            Dim lap = BestLapStore.Best(context, layoutId, TrackPacks.Nordschleife.Version)
            bestTime.Text = If(lap Is Nothing, "Best lap: no completed lap yet", "Best lap: " & BestLapStore.Display(lap))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Text.Json.JsonException
            bestTime.Text = "Best lap history unavailable; saved file kept"
        End Try
    End Sub
    Private Sub RefreshRaceOptions()
        Dim chosen = TryCast(layouts.SelectedItem, LayoutItem)?.Value
        raceReady = installedReceipt IsNot Nothing AndAlso currentPack IsNot Nothing AndAlso TrackPacks.Get(currentPack.Id).SupportsRace(installedReceipt, chosen?.Id)
        If ButtermilkPostProcess.Supports(chosen?.Id) Then
            If Not setup.Controls.Contains(lightingTestBox) Then
                setup.Controls.Add(lightingTestBox) : setup.Controls.SetChildIndex(lightingTestBox, 1)
            End If
        Else
            setup.Controls.Remove(lightingTestBox)
        End If
        Dim race = launchMode.SelectedIndex = 1
        Dim nord = currentPack?.Id = "nordschleife"
        Dim pointToPoint = chosen IsNot Nothing AndAlso Not chosen.Circuit
        laps.Enabled = Not pointToPoint AndAlso (Not nord OrElse race)
        If pointToPoint Then laps.Value = 1
        If nord AndAlso Not loading Then laps.Value = If(race, nordRaceLaps, 1)
        modeHint.Text = If(nord, "Desktop and VR · " & If(race, "1–20 laps", "One lap") & " · VR and AI races are experimental", "Desktop and VR · AI races are experimental · LAN unavailable")
        If currentPack?.Id = "nordschleife" AndAlso launchMode.SelectedIndex = 1 Then modeHint.Text &= ". Best laps are saved in Direct practice."
        If pointToPoint Then modeHint.Text = "Desktop Direct practice · One 10.4 km run · Race, VR and LAN unavailable"
        If chosen?.Discipline = "Head-to-head" Then modeHint.Text = "Two-car grid: one AI opponent. Race timing on these separate-lane courses needs testing; knockout Head-to-head rules are unavailable."
        If installationValid AndAlso chosen IsNot Nothing AndAlso Not installedLayouts.Contains(chosen.Id) Then modeHint.Text = "This layout is not installed. Choose Manage → Rebuild from source."
        raceDifficulty.Enabled = race
        opponents.Enabled = race AndAlso opponents.Maximum > 1 : opponentCars.Enabled = race
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
        If working AndAlso Not workActions.Controls.Contains(cancelButton) Then workActions.Controls.Add(cancelButton)
        If Not working Then workActions.Controls.Remove(cancelButton)
        If errorText <> "" Then actions.Controls.Add(errorDetails)
        If working AndAlso Not workActions.Controls.Contains(progressBar) Then workActions.Controls.Add(progressBar)
        If Not working Then workActions.Controls.Remove(progressBar)
        toggle.Enabled = Not working : browser.PackList.Enabled = Not working : browser.Picker.Enabled = Not working
        setup.Enabled = Not working : installButton.Enabled = Not working : manage.Enabled = Not working
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
        If Not raceReady AndAlso currentPack.Id = AspenPack.Id Then status.Text &= ". Rebuild from source to enable AI races."
        If layouts.SelectedItem Is Nothing Then status.Text &= ". Your saved layout is unavailable; choose a layout."
        If cars.SelectedItem Is Nothing Then status.Text &= ". Your saved car is unavailable; choose an installed car."
        RefreshRaceOptions()
    End Sub
    Private Async Function InstallPack() As Task
        If working OrElse Not currentPack.Available Then Return
        CaptureSelection()
        Await Operation(Async Function(token, progress)
                            context.RequireClosed()
                            Dim offer = CustomTrackService.Profile(currentPack.Id), selected = preferences.ForPack(currentPack.Id)
                            Using picker As New AspenSourceForm(selected.SourceFolder, CustomTrackService.SourceFolders(currentPack.Id), offer, selected.BuildLayoutIds, installedReceipt)
                                If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                                selected.SourceFolder = picker.SourceFolder : selected.BuildLayoutIds = picker.SelectedLayoutIds : preferences.Save(context)
                            End Using
                            SetStatus("Building…", "Building " & selected.BuildLayoutIds.Length.ToString() & " selected " & currentPack.Name & " layouts…")
                            verification.Invalidate(currentPack.Id)
                            Await CustomTrackService.InstallAsync(context, offer, selected.SourceFolder, progress, token, selected.BuildLayoutIds)
                            ' InstallAsync has committed successfully; a late stop must not misreport it as cancelled.
                            installedReceipt = Await Task.Run(Function() verification.Read(TrackPacks.Get(currentPack.Id), CancellationToken.None))
                            installationValid = True : hasReceipt = True
                            installedLayouts = installedReceipt.Sessions.Select(Function(s) s.LayoutId).ToHashSet(StringComparer.Ordinal) : RefreshRaceOptions()
                            SetStatus("Installed · Ready offline", selected.BuildLayoutIds.Length.ToString() & " layouts built · " & installedLayouts.Count.ToString() & " of " & offer.Layouts.Length.ToString() & " installed. Choose a layout, then Launch.")
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
        TextRenderer.DrawText(e.Graphics, If(pack.Id = "nordschleife", "Standard circuit · 20.7 km", If(pack.Id = "mizu-mountain", "Point-to-point · 10.4 km · Desktop", If(pack.Available, pack.Layouts.Length & " layouts · Race and VR", "Saved selection"))), Font, bounds, foreground, TextFormatFlags.Top Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
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
    Private ReadOnly choices As New List(Of CheckBox)
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
    Public Sub New(previous As String, detected As String(), offer As ConversionProfile, Optional selectedIds As String() = Nothing, Optional installed As PackReceipt = Nothing)
        Dim nord = offer.Id = "nordschleife"
        Dim sourceGame = TrackPacks.Get(offer.Id).SourceName
        source.Name = If(nord, "AssettoCorsaSourceFolder", If(offer.Id = "mizu-mountain", "Grid2SourceFolder", "Dirt3SourceFolder"))
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
        content.Controls.Add(New Label With {.Text = If(nord, "All three lighting presets are built together.", "Only checked layouts are built. Unchecked layouts already installed will be kept."), .AutoSize = True})
        Dim selectionButtons As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
        Dim all As New Button With {.Name = "SelectAllLayouts", .Text = "Select all", .AutoSize = True}
        Dim none As New Button With {.Name = "SelectNoLayouts", .Text = "Select none", .AutoSize = True}
        AddHandler all.Click, Sub()
                                  For Each choice In choices : choice.Checked = True : Next
                              End Sub
        AddHandler none.Click, Sub()
                                   For Each choice In choices : choice.Checked = False : Next
                               End Sub
        selectionButtons.Controls.AddRange({all, none}) : selectionButtons.Visible = Not nord : content.Controls.Add(selectionButtons)
        For Each trackLayout In offer.Layouts
            Dim session = installed?.Sessions.FirstOrDefault(Function(s) s.LayoutId = trackLayout.Id)
            Dim status = If(session Is Nothing, "Not installed", "Installed")
            Dim choice As New CheckBox With {.Name = "BuildLayout_" & trackLayout.Id, .Tag = trackLayout.Id, .AutoSize = True,
                .Text = If(nord, trackLayout.Condition, trackLayout.Name) & " · " & status,
                .Checked = nord OrElse selectedIds Is Nothing OrElse selectedIds.Contains(trackLayout.Id, StringComparer.Ordinal), .Enabled = Not nord, .AccessibleName = If(nord, trackLayout.Condition, trackLayout.Name)}
            choices.Add(choice) : content.Controls.Add(choice)
            AddHandler choice.CheckedChanged, Sub() UpdateSelection()
        Next
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
