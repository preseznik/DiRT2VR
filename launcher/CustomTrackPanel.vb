Imports DiRT2VR.CustomTracks
Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms

Public Class CustomTrackPanel
    Inherits VerticalStack
    Private ReadOnly context As InstallContext
    Private ReadOnly preferences As CustomTrackPreferences
    Private ReadOnly toggle As New CheckBox With {.Text = "CUSTOM tracks (Experimental)", .Name = "CustomTracks", .AutoSize = True}
    Private ReadOnly detail As New VerticalStack
    Private ReadOnly status As New Label With {.Name = "CustomTrackStatus", .AutoSize = False, .Text = "Not installed"}
    Private ReadOnly layouts As New ComboBox With {.Name = "CustomTrackLayout", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly launchMode As New ComboBox With {.Name = "CustomLaunchMode", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly cars As New ComboBox With {.Name = "CustomCar", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponentCars As New ComboBox With {.Name = "CustomOpponentCars", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly opponents As New ValueSlider("CustomOpponents", 1, 7, 3)
    Private ReadOnly laps As New ValueSlider("CustomLaps", 1, 20, 1)
    Private ReadOnly opponentHint As New Label With {.AutoSize = False}
    Private ReadOnly installButton As New Button With {.Text = "Install Aspen", .Name = "InstallAspen", .AutoSize = True}
    Private ReadOnly manage As New Button With {.Text = "Manage…", .Name = "ManageAspen", .AutoSize = True}
    Private ReadOnly cancelButton As New Button With {.Text = "Cancel", .Name = "CancelAspen", .AutoSize = True, .Enabled = False}
    Private ReadOnly progressBar As New ProgressBar With {.Minimum = 0, .Maximum = 100, .Height = 18}
    Private ReadOnly menu As New ContextMenuStrip
    Private offer As ConversionProfile
    Private cancellation As CancellationTokenSource
    Private installationValid As Boolean
    Private raceReady As Boolean
    Private working As Boolean
    Private initialized As Boolean
    Public Event AvailabilityChanged As EventHandler
    Private Class LayoutItem
        Public ReadOnly Value As Layout
        Public Sub New(value As Layout)
            Me.Value = value
        End Sub
        Public Overrides Function ToString() As String
            Return Value.Name & " — " & Value.Condition
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
            Return Not working AndAlso installationValid AndAlso layouts.SelectedItem IsNot Nothing AndAlso
                launchMode.SelectedIndex >= 0 AndAlso (launchMode.SelectedIndex <> 1 OrElse raceReady) AndAlso cars.SelectedItem IsNot Nothing AndAlso opponentCars.SelectedIndex >= 0
        End Get
    End Property
    Public Sub New(value As InstallContext)
        context = value : preferences = CustomTrackPreferences.Load(context)
        Name = "CustomTrackPanel"
        For Each choice In {layouts, launchMode, cars, opponentCars}
            StyleChoice(choice, Me)
        Next
        Controls.Add(toggle)
        detail.Controls.Add(New Label With {.Text = "Aspen · Four Rallycross layouts", .AutoSize = True, .Font = New Font("Segoe UI", 11, FontStyle.Bold)})
        detail.Controls.Add(New Label With {.Text = "Build once from your own DiRT 3 Complete Edition files, then play offline. Choose Launch VR or Launch for desktop play.", .AutoSize = False})
        detail.Controls.Add(status)
        For Each trackLayout In AspenPack.Layouts
            layouts.Items.Add(New LayoutItem(trackLayout))
        Next
        layouts.SelectedItem = layouts.Items.Cast(Of LayoutItem).FirstOrDefault(Function(item) item.Value.Id = preferences.LayoutId)
        Field(detail, "Layout and lighting", layouts)
        Dim columns As New ResponsiveColumns
        detail.Controls.Add(columns)
        launchMode.Items.AddRange({"Direct practice (experimental)", "Race (experimental)"})
        launchMode.SelectedIndex = Array.IndexOf({"practice", "race"}, preferences.LaunchMode)
        cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
        cars.SelectedItem = cars.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) c.Code = preferences.CarCode)
        opponentCars.Items.AddRange({"Same as driver", "Mixed", "Same class"})
        opponentCars.SelectedIndex = Array.IndexOf({"same", "mixed", "class"}, preferences.OpponentCars)
        opponents.Value = Math.Clamp(preferences.Opponents, 1, 7)
        laps.Value = Math.Clamp(preferences.Laps, 1, 20)
        Field(columns.First, "Launch mode", launchMode)
        Field(columns.First, "Car", cars)
        Field(columns.Second, "AI opponents", opponents)
        Field(columns.Second, "Opponent cars", opponentCars)
        Field(columns.Second, "Laps", laps)
        detail.Controls.Add(opponentHint)
        For Each choice In {launchMode, cars, opponentCars}
            AddHandler choice.SelectedIndexChanged, Sub() RefreshRaceOptions()
        Next
        RefreshRaceOptions()
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
        buttons.Controls.AddRange({installButton, manage, cancelButton})
        detail.Controls.Add(buttons)
        detail.Controls.Add(New Label With {.Text = "VR and AI races are experimental. LAN remains unavailable. Turn CUSTOM tracks off to restore your original track, car and race selections.", .AutoSize = False})
        AddHandler toggle.CheckedChanged, Async Sub()
                                             If toggle.Checked Then
                                                 If Not Controls.Contains(detail) Then Controls.Add(detail)
                                             Else
                                                 Controls.Remove(detail)
                                             End If
                                             preferences.Enabled = toggle.Checked
                                             RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
                                             If initialized AndAlso toggle.Checked Then Await RefreshInstallation()
                                         End Sub
        AddHandler layouts.SelectedIndexChanged, Sub()
                                                    Dim selected = TryCast(layouts.SelectedItem, LayoutItem)
                                                    If selected IsNot Nothing Then preferences.LayoutId = selected.Value.Id
                                                    RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
                                                End Sub
        AddHandler installButton.Click, Async Sub() Await InstallPack()
        AddHandler cancelButton.Click, Sub() CancelOperation()
        AddHandler manage.Click, Sub() menu.Show(manage, New Point(0, manage.Height))
        AddHandler menu.Items.Add("Verify installed files").Click, Async Sub() Await RefreshInstallation()
        AddHandler menu.Items.Add("Rebuild Aspen from source").Click, Async Sub() Await InstallPack()
        AddHandler menu.Items.Add("Uninstall Aspen").Click, Async Sub()
                                                                If MessageBox.Show(Me, "Remove the installed Aspen layouts? Your original tracks and settings will be kept.", "Uninstall Aspen", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) <> DialogResult.OK Then Return
                                                                Await Operation(Async Function(token, progress)
                                                                                    Await CustomTrackService.UninstallAsync(context)
                                                                                    installationValid = False : status.Text = "Aspen removed. Original tracks are available with CUSTOM tracks off."
                                                                                End Function)
                                                            End Sub
        toggle.Checked = preferences.Enabled
        initialized = True
        AddHandler HandleCreated, Async Sub()
                                          If CustomEnabled Then Await RefreshInstallation()
                                      End Sub
        AddHandler Disposed, Sub() menu.Dispose()
    End Sub
    Public Sub Save()
        If CustomEnabled AndAlso (launchMode.SelectedIndex < 0 OrElse cars.SelectedItem Is Nothing OrElse opponentCars.SelectedIndex < 0 OrElse layouts.SelectedItem Is Nothing) Then Throw New IOException("Select an available custom layout, launch mode, car and opponent selection.")
        If launchMode.SelectedIndex >= 0 Then preferences.LaunchMode = {"practice", "race"}(launchMode.SelectedIndex)
        If cars.SelectedItem IsNot Nothing Then preferences.CarCode = DirectCast(cars.SelectedItem, PracticeCar).Code
        If opponentCars.SelectedIndex >= 0 Then preferences.OpponentCars = {"same", "mixed", "class"}(opponentCars.SelectedIndex)
        preferences.Opponents = CInt(opponents.Value) : preferences.Laps = CInt(laps.Value)
        preferences.Save(context)
    End Sub
    Private Sub RefreshRaceOptions()
        Dim race = launchMode.SelectedIndex = 1
        opponents.Enabled = race : opponentCars.Enabled = race
        Dim vehicle = TryCast(cars.SelectedItem, PracticeCar)
        opponentHint.Text = If(race AndAlso installationValid AndAlso Not raceReady, "Choose Rebuild Aspen to update AI driving paths before racing.", If(Not race, "Solo practice. Your race settings are kept for later.", If(opponentCars.SelectedIndex = 2,
            "Opponent class: " & If(vehicle?.ClassName, "Select a car"), If(opponentCars.SelectedIndex = 1,
            "Mixed: all installed classes.", "All opponents use the same car as the driver."))))
        RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
    End Sub
    Public Sub CancelOperation()
        cancellation?.Cancel()
        If working Then status.Text = "Cancelling; waiting for the current safe operation to finish…"
    End Sub
    Private Async Function RefreshInstallation() As Task
        If working Then Return
        Await Operation(Async Function(token, progress)
                            installationValid = False
                            If Not File.Exists(SafeFiles.Inside(context.GameRoot, AspenPack.Receipt)) Then
                                status.Text = "Not installed. Install Aspen to build all four layouts from your DiRT 3 files."
                                Return
                            End If
                            status.Text = "Checking installed Aspen files…"
                            Dim receipt = Await Task.Run(Function() AspenPack.Read(context.GameRoot, True, token), token)
                            CustomTrackService.RequireLauncher(receipt)
                            installationValid = True
                            raceReady = AspenPack.SupportsRace(receipt)
                            status.Text = "Installed · Aspen " & receipt.Version & " · ready offline"
                            If Not raceReady Then status.Text &= ". Rebuild Aspen to enable AI races."
                            RefreshRaceOptions()
                            If layouts.SelectedItem Is Nothing Then status.Text &= ". Your saved layout is unavailable; select a layout."
                        End Function)
    End Function
    Private Async Function InstallPack() As Task
        Await Operation(Async Function(token, progress)
                            context.RequireClosed()
                            status.Text = "Preparing Aspen build…"
                            offer = Aspen.AspenConversion.GetProfile()
                            Using picker As New AspenSourceForm(preferences.SourceFolder, CustomTrackService.SourceFolders(), offer)
                                If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                                preferences.SourceFolder = picker.SourceFolder : preferences.Save(context)
                            End Using
                            Await CustomTrackService.InstallAsync(context, offer, preferences.SourceFolder, progress, token)
                            installationValid = True
                            raceReady = True : RefreshRaceOptions()
                            status.Text = "Aspen " & offer.Version & " installed · ready offline. Select a layout, then choose Launch."
                        End Function)
    End Function
    Private Async Function Operation(action As Func(Of CancellationToken, IProgress(Of TrackProgress), Task)) As Task
        If working Then Return
        working = True : toggle.Enabled = False : installButton.Enabled = False : manage.Enabled = False : layouts.Enabled = False
        cancelButton.Enabled = True : detail.Controls.Add(progressBar)
        cancellation = New CancellationTokenSource()
        RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
        Try
            Dim progress As New Progress(Of TrackProgress)(Sub(value)
                                                              If IsDisposed Then Return
                                                              progressBar.Value = Math.Clamp(value.Percent, 0, 100)
                                                              status.Text = value.Message
                                                          End Sub)
            Await action(cancellation.Token, progress)
        Catch ex As OperationCanceledException
            status.Text = "Cancelled. Existing tracks are unchanged. Choose Install Aspen to try again."
        Catch ex As Exception
            status.Text = ex.Message
        Finally
            cancellation.Dispose() : cancellation = Nothing : working = False
            installButton.Text = If(installationValid, "Rebuild Aspen", "Install Aspen")
            toggle.Enabled = True : installButton.Enabled = True : manage.Enabled = True : layouts.Enabled = True
            cancelButton.Enabled = False : detail.Controls.Remove(progressBar)
            RaiseEvent AvailabilityChanged(Me, EventArgs.Empty)
        End Try
    End Function
End Class

Public Class AspenSourceForm
    Inherits LauncherForm
    Private ReadOnly source As New ComboBox With {.Name = "Dirt3SourceFolder", .DropDownStyle = ComboBoxStyle.DropDown}
    Public ReadOnly Property SourceFolder As String
        Get
            Return source.Text.Trim().Trim(""""c)
        End Get
    End Property
    Public Sub New(previous As String, detected As String(), offer As ConversionProfile)
        Text = "Install Aspen — locate DiRT 3" : Font = New Font("Segoe UI", 10)
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
                                      If Not Directory.Exists(IO.Path.Combine(SourceFolder, "tracks/locations/usa/aspen")) Then
                                          MessageBox.Show(Me, "Choose the DiRT 3 Complete Edition game folder containing tracks\locations\usa\aspen.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
                                          Return
                                      End If
                                      DialogResult = DialogResult.OK
                                  End Sub
        buttons.Controls.AddRange({install, cancel}) : content.Controls.Add(buttons)
        Controls.Add(content) : AcceptButton = install : CancelButton = cancel : AutoScroll = True
    End Sub
End Class
