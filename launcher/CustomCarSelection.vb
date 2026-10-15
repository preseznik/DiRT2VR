Imports DiRT2VR.CustomCars
Imports DiRT2VR.CustomTracks
Imports System.Windows.Forms

Public Class CustomCarSelection
    Inherits VerticalStack
    Private ReadOnly context As InstallContext
    Private ReadOnly cars As ComboBox
    Private ReadOnly toggle As New CheckBox With {.Text = "CUSTOM cars (Experimental)", .AutoSize = True}
    Private ReadOnly status As New TrackStatusLabel With {.AutoSize = True, .UseMnemonic = False}
    Private ReadOnly actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True}
    Private ReadOnly build As New Button With {.Text = "Build/import…", .AutoSize = True}
    Private ReadOnly manage As New Button With {.Text = "Manage…", .AutoSize = True}
    Private ReadOnly menu As New ContextMenuStrip()
    Private ReadOnly stopBuild As New Button With {.Text = "Stop build", .AutoSize = True, .Visible = False}
    Private ReadOnly bar As New ProgressBar With {.Minimum = 0, .Maximum = 100, .Width = 180, .Visible = False}
    Private key As String
    Private stockCode As String = "sti"
    Private loading As Boolean
    Private ready As Boolean
    Private lastMessage As String = ""
    Private cancellation As CancellationTokenSource
    Public Event Changed As EventHandler

    Public ReadOnly Property CustomEnabled As Boolean
        Get
            Return toggle.Checked
        End Get
    End Property
    Public ReadOnly Property StockSelection As String
        Get
            Return stockCode
        End Get
    End Property
    Public ReadOnly Property ReadyForDesktop As Boolean
        Get
            Return Not CustomEnabled OrElse (ready AndAlso Not CustomCarService.Building)
        End Get
    End Property
    Public Function CanLaunch(track As String, mode As String, Optional vr As Boolean = False) As Boolean
        If Not CustomEnabled Then Return True
        If Not ReadyForDesktop Then Return False
        Try
            Dim code = CustomCarService.SelectedCode(context)
            If code = CarPack.Code Then
                CarPack.RequireMode(track, vr, mode, If(mode = "race", 1, 0))
            Else
                ExternalCars.RequireMode(ExternalCars.Require(code), vr, mode)
            End If
            Return True
        Catch ex As IOException
            Return False
        End Try
    End Function
    Public Sub New(context As InstallContext, cars As ComboBox, key As String, selectedStock As String)
        Me.context = context : Me.cars = cars : Me.key = key
        Name = "CustomCarSelection" & If(key = "stock", "Stock", "Track") : toggle.Name = Name & "Toggle"
        build.Name = Name & "Build" : manage.Name = Name & "Manage" : stopBuild.Name = Name & "Cancel" : status.Name = Name & "Status"
        Dim row As New ResponsiveColumns With {.WideAt = 650}
        row.First.Controls.Add(cars) : row.Second.Controls.Add(toggle)
        AddHandler SizeChanged, Sub() status.MaximumSize = New Drawing.Size(Math.Max(1, ClientSize.Width - status.Margin.Horizontal), 0)
        Controls.Add(row) : Controls.Add(status) : actions.Controls.AddRange({build, manage, stopBuild, bar}) : Controls.Add(actions)
        AddHandler toggle.CheckedChanged, Sub()
                                            If loading Then Return
                                            Dim preferences = CustomCarPreferences.Load(context)
                                            preferences.Enabled = toggle.Checked : preferences.Save(context)
                                            CustomCarService.Notify(context)
                                        End Sub
        AddHandler cars.SelectedIndexChanged, Sub()
                                                 If loading Then Return
                                                 If CustomEnabled Then
                                                     Dim car = TryCast(cars.SelectedItem, PracticeCar)
                                                     If car Is Nothing Then Return
                                                     Dim saved = CustomCarPreferences.Load(context)
                                                     saved.SelectedId = If(car.PackId = "", CarPack.Code, car.PackId) : saved.Save(context)
                                                     CustomCarService.Notify(context) : Return
                                                 End If
                                                 Dim selected = TryCast(cars.SelectedItem, PracticeCar)
                                                 If selected Is Nothing OrElse CarPack.IsCustom(selected.Code) Then Return
                                                 stockCode = selected.Code
                                                 Dim preferences = CustomCarPreferences.Load(context)
                                                 preferences.StockCars(key) = stockCode : preferences.Save(context)
                                             End Sub
        AddHandler build.Click, Async Sub() Await BuildCar()
        AddHandler stopBuild.Click, Sub() cancellation?.Cancel()
        AddHandler manage.Click, Sub() ShowManage()
        AddHandler CustomCarService.Changed, AddressOf ExternalChanged
        AddHandler Disposed, Sub()
                                 RemoveHandler CustomCarService.Changed, AddressOf ExternalChanged
                                 cancellation?.Cancel()
                                 menu.Dispose()
                             End Sub
        SetStockSelection(key, selectedStock)
    End Sub
    Public Sub SetStockSelection(selectionKey As String, preferred As String)
        key = selectionKey
        If RaceCatalog.Current.Cars.Any(Function(c) c.Code = preferred) Then stockCode = preferred
        Dim preferences = CustomCarPreferences.Load(context)
        If preferences.StockCars.ContainsKey(key) Then stockCode = preferences.StockCars(key)
        RefreshChoices()
    End Sub
    Private Sub ExternalChanged(sender As Object, e As EventArgs)
        If IsDisposed OrElse DirectCast(sender, InstallContext).Id <> context.Id Then Return
        If InvokeRequired Then
            BeginInvoke(New Action(AddressOf RefreshChoices))
        Else
            RefreshChoices()
        End If
    End Sub
    Private Sub RefreshChoices()
        If IsDisposed Then Return
        loading = True
        Try
            Dim preferences = CustomCarPreferences.Load(context)
            toggle.Checked = preferences.Enabled
            cars.Items.Clear()
            If CustomEnabled Then
                cars.Items.Add(RaceCatalog.Current.Car(CarPack.Code))
                For Each pack In TrackPacks.External.Where(Function(p) p.Kind = "car")
                    cars.Items.Add(If(pack.Ready, RaceCatalog.Current.Car(pack.Manifest.Car.Code), New PracticeCar With {.Code = If(pack.Manifest?.Car?.Code, ""), .PackId = pack.Id, .Label = pack.Name & " — External (needs attention)"}))
                Next
                cars.SelectedItem = cars.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) If(c.PackId = "", CarPack.Code, c.PackId) = preferences.SelectedId)
                If cars.SelectedItem Is Nothing Then
                    Dim missing As New PracticeCar With {.PackId = preferences.SelectedId, .Label = "Missing external car — restore its folder or choose another car"}
                    cars.Items.Add(missing) : cars.SelectedItem = missing
                End If
            Else
                cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
                cars.SelectedItem = cars.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) c.Code = stockCode)
            End If
            ready = False
            If preferences.SelectedId <> CarPack.Code Then
                Dim pack = TrackPacks.External.FirstOrDefault(Function(p) p.Kind = "car" AndAlso p.Id = preferences.SelectedId)
                status.Text = If(pack Is Nothing, "Selected external car is missing. Restore its pack, then Refresh.", If(pack.Ready, "External · " & String.Join(" · ", pack.Manifest.Modes.Select(Function(mode) mode.Replace("desktop-", "Desktop ").Replace("vr-", "VR ").Replace("solo", "Practice").Replace("race", "Race"))) & " · " & pack.Manifest.Author, pack.Problem))
                ready = pack IsNot Nothing AndAlso pack.Ready
            ElseIf File.Exists(IO.Path.Combine(context.GameRoot, CarPack.Receipt)) Then
                Try
                    CarPack.Read(context.GameRoot) : ready = True
                    status.Text = "Direct practice · All compatible tracks · Desktop and VR (Experimental)"
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    status.Text = ex.Message
                End Try
            Else
                status.Text = "Ferrari F2004 · Not imported"
            End If
            status.Visible = CustomEnabled : actions.Visible = CustomEnabled
            toggle.Enabled = Not CustomCarService.Building : cars.Enabled = Not CustomCarService.Building
            build.Visible = preferences.SelectedId = CarPack.Code
            build.Enabled = Not CustomCarService.Building : manage.Enabled = Not CustomCarService.Building
            If CustomCarService.Building Then status.Text = "Building Ferrari F2004…"
            If Not CustomCarService.Building AndAlso lastMessage <> "" Then status.Text = lastMessage
        Finally
            loading = False
        End Try
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub
    Private Function PickSource() As String
        Using picker As New FolderBrowserDialog With {.Description = "Choose the Ferrari F2004 car folder or Assetto Corsa installation.", .SelectedPath = CustomCarService.SourceFolder(context), .UseDescriptionForTitle = True}
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return Nothing
            Dim source = CarConversion.SourceFolder(picker.SelectedPath)
            If Not File.Exists(IO.Path.Combine(source, "ferrari_f2004.kn5")) Then Throw New IOException("The selected folder does not contain Ferrari F2004.")
            Dim preferences = CustomCarPreferences.Load(context) : preferences.SourceFolder = source : preferences.Save(context)
            Return source
        End Using
    End Function
    Private Async Function BuildCar() As Task
        If CustomCarService.Building Then Return
        Try
            Dim source = CustomCarService.SourceFolder(context)
            If Not Directory.Exists(source) Then source = PickSource()
            If source Is Nothing Then Return
            lastMessage = ""
            cancellation = New CancellationTokenSource() : CustomCarService.Building = True : CustomCarService.Notify(context)
            bar.Value = 0 : bar.Visible = True : stopBuild.Visible = True : stopBuild.Enabled = True
            Dim progress As New Progress(Of CarProgress)(Sub(value)
                                                            If IsDisposed Then Return
                                                            bar.Value = Math.Clamp(value.Percent, 0, 100) : status.Text = value.Message
                                                            stopBuild.Enabled = value.CanCancel
                                                        End Sub)
            Await CustomCarService.InstallAsync(context, source, progress, cancellation.Token)
        Catch ex As OperationCanceledException
            lastMessage = "Build stopped. The installed car was kept."
        Catch ex As Exception
            lastMessage = ex.Message
            MessageBox.Show(Me, ex.Message, "Ferrari F2004 import", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            cancellation?.Dispose() : cancellation = Nothing : CustomCarService.Building = False
            If Not IsDisposed Then bar.Visible = False : stopBuild.Visible = False
            CustomCarService.Notify(context)
        End Try
    End Function
    Private Sub ShowManage()
        ' Keep the dropdown alive while WinForms finishes closing an item click.
        ' Recreate its choices only on the next opening, when selection may differ.
        For Each item In menu.Items.Cast(Of ToolStripItem)().ToArray()
            item.Dispose()
        Next
        Dim export = menu.Items.Add("Export pack ZIP…", Nothing, Sub()
            PackExportDialog.Export(Me, context, "car", CustomCarPreferences.Load(context).SelectedId, cars.Text)
        End Sub)
        export.Name = "ExportCustomCar" : export.Enabled = ready AndAlso Not CustomCarService.Building
        If CustomCarPreferences.Load(context).SelectedId = CarPack.Code Then
            menu.Items.Add("Rebuild from source", Nothing, Async Sub() Await BuildCar())
            menu.Items.Add("Choose source folder…", Nothing, Sub()
                                                               Try
                                                                   PickSource()
                                                               Catch ex As Exception
                                                                   MessageBox.Show(Me, ex.Message, "Ferrari source", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                                               End Try
                                                           End Sub)
            menu.Items.Add("Recheck installation", Nothing, Sub()
                                                               lastMessage = "" : RefreshChoices()
                                                           End Sub)
            menu.Items.Add("Uninstall custom car", Nothing, Async Sub()
                                                                Try
                                                                    Await CustomCarService.UninstallAsync(context)
                                                                    CustomCarService.Notify(context)
                                                                Catch ex As Exception
                                                                    MessageBox.Show(Me, ex.Message, "Ferrari uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                                                End Try
                                                            End Sub)
        End If
        menu.Items.Add("Refresh external packs", Nothing, Sub()
            Try
                TrackPacks.DiscoverExternal(context.GameRoot, BuildInfo.Version)
                lastMessage = "" : CustomCarService.Notify(context)
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "External cars", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub)
        menu.Items.Add("Open external cars folder", Nothing, Sub()
            Try
                Dim folder = SafeFiles.Inside(context.GameRoot, ExternalPacks.RelativeFolder("car"))
                Directory.CreateDirectory(folder)
                Process.Start(New ProcessStartInfo With {.FileName = folder, .UseShellExecute = True})
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "External cars", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub)
        menu.Show(manage, New Drawing.Point(0, manage.Height))
    End Sub
End Class
