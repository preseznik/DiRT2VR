Imports DiRT2VR.CustomCars
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
    Public Function CanLaunch(track As String, mode As String) As Boolean
        If Not CustomEnabled Then Return True
        Return ReadyForDesktop AndAlso mode = "practice" AndAlso (track = "144" OrElse track = "nordschleife-daylight")
    End Function
    Public Sub New(context As InstallContext, cars As ComboBox, key As String, selectedStock As String)
        Me.context = context : Me.cars = cars : Me.key = key
        Name = "CustomCarSelection" & If(key = "stock", "Stock", "Track") : toggle.Name = Name & "Toggle"
        build.Name = Name & "Build" : manage.Name = Name & "Manage" : stopBuild.Name = Name & "Cancel" : status.Name = Name & "Status"
        Dim row As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Margin = New Padding(0)}
        row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100)) : row.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        cars.Dock = DockStyle.Fill : row.Controls.Add(cars, 0, 0) : row.Controls.Add(toggle, 1, 0)
        Controls.Add(row) : Controls.Add(status) : actions.Controls.AddRange({build, manage, stopBuild, bar}) : Controls.Add(actions)
        AddHandler toggle.CheckedChanged, Sub()
                                            If loading Then Return
                                            Dim preferences = CustomCarPreferences.Load(context)
                                            preferences.Enabled = toggle.Checked : preferences.Save(context)
                                            CustomCarService.Notify(context)
                                        End Sub
        AddHandler cars.SelectedIndexChanged, Sub()
                                                 If loading OrElse CustomEnabled Then Return
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
            toggle.Checked = CustomCarPreferences.Load(context).Enabled
            cars.Items.Clear()
            If CustomEnabled Then
                cars.Items.Add(RaceCatalog.Current.Car(CarPack.Code)) : cars.SelectedIndex = 0
            Else
                cars.Items.AddRange(RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).OrderBy(Function(c) If(c.Code = "sti", "", c.Label)).Cast(Of Object).ToArray())
                cars.SelectedItem = cars.Items.Cast(Of PracticeCar).FirstOrDefault(Function(c) c.Code = stockCode)
            End If
            ready = False
            If File.Exists(IO.Path.Combine(context.GameRoot, CarPack.Receipt)) Then
                Try
                    CarPack.Read(context.GameRoot) : ready = True
                    status.Text = "Ready for desktop testing · Direct practice · Battersea Air or Nordschleife Daylight"
                Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    status.Text = ex.Message
                End Try
            Else
                status.Text = "Ferrari F2004 · Not imported"
            End If
            status.Visible = CustomEnabled : actions.Visible = CustomEnabled
            toggle.Enabled = Not CustomCarService.Building : cars.Enabled = Not CustomCarService.Building
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
        Dim menu As New ContextMenuStrip()
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
        AddHandler menu.Closed, Sub() menu.Dispose()
        menu.Show(manage, New Drawing.Point(0, manage.Height))
    End Sub
End Class
