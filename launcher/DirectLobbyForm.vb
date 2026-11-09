Imports System.Drawing
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports DiRT2VR.Multiplayer

Public Class DirectLobbyForm
    Inherits LauncherForm
    Private ReadOnly context As InstallContext
    Private ReadOnly packageRoot As String
    Private stockRoot As String
    Private ReadOnly stockFolder As New Button With {.Name = "DirectStockFolder", .Text = "Stock game folder…", .AutoSize = True}
    Private ReadOnly playerName As New TextBox With {.Name = "DirectName", .MaxLength = 32, .Width = 150, .Text = Environment.MachineName}
    Private ReadOnly address As New TextBox With {.Name = "DirectAddress", .Width = 140, .PlaceholderText = "Host LAN IPv4"}
    Private ReadOnly servers As DirectServers
    Private joinedHostSaved As Boolean
    Private ReadOnly host As New Button With {.Name = "DirectHost", .Text = "Host lobby", .AutoSize = True}
    Private ReadOnly join As New Button With {.Name = "DirectJoin", .Text = "Join lobby", .AutoSize = True}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(610, 0)}
    Private ReadOnly track As New ComboBox With {.Name = "DirectTrack", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private remoteCircuit As RallycrossChoice
    Private ReadOnly car As New ComboBox With {.Name = "DirectCar", .DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly roster As New Label With {.Name = "DirectPlayers", .AutoSize = True}
    Private ReadOnly ready As New Button With {.Name = "DirectReady", .Text = "Ready", .AutoSize = True}
    Private ReadOnly start As New Button With {.Name = "DirectStart", .Text = "Start race", .AutoSize = True}
    Private ReadOnly force As New Button With {.Name = "DirectForce", .Text = "Force start", .AutoSize = True}
    Private ReadOnly returnButton As New Button With {.Name = "DirectReturn", .Text = "Return to lobby", .AutoSize = True}
    Private ReadOnly saveLogs As New Button With {.Name = "DirectSaveLogs", .Text = "Save race logs…", .AutoSize = True}
    Private ReadOnly lapLog As New CheckBox With {.Name = "DirectRecordLaps", .Text = "Record lap telemetry", .AutoSize = True}
    Private ReadOnly vr As New CheckBox With {.Name = "DirectVr", .Text = "Launch in VR on this PC", .AutoSize = True}
    Private ReadOnly log As New ListBox With {.Name = "DirectChatLog", .Dock = DockStyle.Fill, .HorizontalScrollbar = True, .IntegralHeight = False}
    Private ReadOnly message As New TextBox With {.Name = "DirectMessage", .MaxLength = 200, .Width = 375, .PlaceholderText = "Lobby chat"}
    Private ReadOnly send As New Button With {.Text = "Send", .AutoSize = True}
    Private room As DirectRoom
    Private updating As Boolean
    Private leaving As Boolean
    Private networkClosed As Boolean
    Private lastRunDirectory As String
    Private savingLogs As Boolean

    Public Sub New(value As InstallContext, Optional payloadRoot As String = Nothing)
        context = value
        packageRoot = If(payloadRoot, AppContext.BaseDirectory)
        stockRoot = context.GameRoot
        Text = "Direct multiplayer (Super Experimental)"
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(980, 720)
        MinimumSize = New Size(540, 430)
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(12), .ColumnCount = 1, .RowCount = 9, .AutoScroll = True}
        For i = 0 To 8
            layout.RowStyles.Add(If(i = 7, New RowStyle(SizeType.Percent, 100), New RowStyle(SizeType.AutoSize)))
        Next
        track.Items.AddRange(RallycrossCatalog.Tracks.Cast(Of Object)().ToArray())
        track.Items.AddRange(RallycrossCatalog.InstalledCircuits(context.GameRoot).Cast(Of Object)().ToArray())
        car.Items.AddRange(RallycrossCatalog.Cars.Cast(Of Object)().ToArray())
        FitChoiceToItems(track) : FitChoiceToItems(car)
        track.SelectedIndex = 0 : car.SelectedIndex = 0
        layout.Controls.Add(Row(Caption("Player"), playerName, host, address, join), 0, 0)
        servers = New DirectServers(context, address)
        layout.Controls.Add(servers, 0, 1)
        layout.Controls.Add(status, 0, 2)
        Dim trackRow = Row(Caption("Rallycross · 3 laps requested"), track, lapLog)
        NewFeatures.MarkIfNew(trackRow, lapLog)
        layout.Controls.Add(trackRow, 0, 3)
        Dim playerRow = Row(Caption("Your car"), car, stockFolder, vr)
        NewFeatures.MarkIfNew(playerRow, vr)
        layout.Controls.Add(playerRow, 0, 4)
        layout.Controls.Add(roster, 0, 5)
        layout.Controls.Add(Row(ready, start, force, returnButton, saveLogs), 0, 6)
        layout.Controls.Add(log, 0, 7)
        layout.Controls.Add(Row(message, send), 0, 8)
        Controls.Add(layout)
        AddHandler Shown, Async Sub() Await servers.SearchAsync()
        AddHandler host.Click, Async Sub() Await OpenRoom(True)
        AddHandler join.Click, Async Sub() Await OpenRoom(False)
        AddHandler stockFolder.Click, Sub()
            Using picker As New FolderBrowserDialog With {.Description = "Choose this PC's original DiRT 2 game folder", .UseDescriptionForTitle = True, .SelectedPath = stockRoot}
                If picker.ShowDialog(Me) = DialogResult.OK Then
                    stockRoot = picker.SelectedPath
                    status.Text = "Stock game files: " & stockRoot
                End If
            End Using
        End Sub
        AddHandler track.SelectedIndexChanged, AddressOf SelectionChanged
        AddHandler car.SelectedIndexChanged, AddressOf SelectionChanged
        AddHandler ready.Click, Async Sub() Await Command(Function() room.ReadyAsync(Not LocalPlayer().Ready))
        AddHandler start.Click, Async Sub() Await Command(Function() room.StartAsync(False))
        AddHandler force.Click, Async Sub() Await Command(Function() room.StartAsync(True))
        AddHandler returnButton.Click, Async Sub() Await Command(Function() room.ReturnAsync())
        AddHandler saveLogs.Click, Async Sub() Await SaveRaceLogs()
        AddHandler lapLog.CheckedChanged, Sub()
                                             If room IsNot Nothing Then room.RecordLapTelemetry = lapLog.Checked
                                         End Sub
        AddHandler send.Click, Async Sub() Await SendChat()
        AddHandler message.KeyDown, Async Sub(sender, e)
                                        If e.KeyCode <> Keys.Enter Then Return
                                        e.SuppressKeyPress = True
                                        Await SendChat()
                                    End Sub
        RefreshRoom()
    End Sub

    Protected Overrides Sub OnLoad(e As EventArgs)
        Dim available = Screen.FromControl(If(Owner, Me)).WorkingArea.Size
        Size = New Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height))
        MyBase.OnLoad(e)
    End Sub

    Private Shared Function Row(ParamArray children As Control()) As FlowLayoutPanel
        Dim panel As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        panel.Controls.AddRange(children)
        Return panel
    End Function
    Private Shared Function Caption(value As String) As Label
        Return New Label With {.AutoSize = True, .Text = value, .Margin = New Padding(3, 8, 6, 3)}
    End Function
    Private Function LocalPlayer() As Player
        Return If(room.IsHost, room.Grid.Host, room.Grid.Guest)
    End Function
    Private Shared Function PlayerLine(p As Player) As String
        Dim model = RallycrossCatalog.Cars.Single(Function(choice) choice.Id = p.Car).Name
        Return $"{p.Name}   ·   {model}   ·   {If(p.Ready, "Ready", "Choosing")}"
    End Function
    Private Sub AddChat(line As String)
        If log.Items.Count >= 150 Then log.Items.RemoveAt(0)
        log.Items.Add(line)
        log.TopIndex = log.Items.Count - 1
    End Sub
    Private Sub RefreshRoom()
        If IsDisposed Then Return
        If room IsNot Nothing AndAlso Not room.WorkerActive AndAlso room.LastRunDirectory IsNot Nothing Then
            lastRunDirectory = room.LastRunDirectory
        End If
        saveLogs.Enabled = Not savingLogs AndAlso lastRunDirectory IsNot Nothing AndAlso Not If(room?.WorkerActive, False)
        If leaving Then
            If room Is Nothing OrElse Not room.WorkerActive Then BeginInvoke(Sub() Close())
            Return
        End If
        If networkClosed AndAlso room IsNot Nothing AndAlso Not room.WorkerActive Then
            ReleaseRoom()
            Return
        End If
        updating = True
        Try
            Dim connected = room IsNot Nothing AndAlso room.Connected
            Dim editable = connected AndAlso Not room.Starting
            Dim checking = editable AndAlso room.IsHost AndAlso room.CheckingLayout
            Dim layoutReady = editable AndAlso Not checking AndAlso
                (room.Grid.Layout Is Nothing OrElse room.Grid.Agreement = LayoutAgreement.Matching)
            host.Enabled = room Is Nothing : join.Enabled = host.Enabled
            servers.Enabled = host.Enabled
            If connected AndAlso Not room.IsHost AndAlso Not joinedHostSaved Then
                joinedHostSaved = True
                servers.RememberJoined(address.Text.Trim())
            End If
            playerName.Enabled = host.Enabled : address.Enabled = host.Enabled
            stockFolder.Enabled = host.Enabled
            ready.Enabled = layoutReady : car.Enabled = editable AndAlso Not checking
            returnButton.Visible = connected AndAlso room.Starting
            returnButton.Enabled = connected AndAlso room.CanRequestReturn
            lapLog.Enabled = room Is Nothing OrElse editable
            vr.Enabled = room Is Nothing OrElse Not room.Starting
            track.Enabled = editable AndAlso room.IsHost
            message.Enabled = connected : send.Enabled = connected
            start.Enabled = layoutReady AndAlso room.IsHost AndAlso room.Grid.BothReady
            force.Enabled = layoutReady AndAlso room.IsHost AndAlso room.Grid.CanStart AndAlso Not room.Grid.BothReady
            If room Is Nothing Then
                If status.Text = "" Then status.Text = "Two players on a private LAN. Allow 12 GB for the first game copy."
            Else
                status.Text = room.Status
                If connected Then
                    Dim grid = room.Grid
                    If Not checking Then
                        Dim choice = If(grid.Layout IsNot Nothing, RallycrossCatalog.Circuit(grid.Layout.Id),
                                        RallycrossCatalog.Tracks.Single(Function(item) item.Id = grid.Track))
                        If remoteCircuit IsNot Nothing AndAlso Not remoteCircuit.Equals(choice) Then
                            track.Items.Remove(remoteCircuit) : remoteCircuit = Nothing
                        End If
                        If Not room.IsHost AndAlso Not track.Items.Contains(choice) Then
                            remoteCircuit = choice : track.Items.Add(choice)
                        End If
                        track.SelectedItem = choice
                    End If
                    car.SelectedItem = RallycrossCatalog.Cars.Single(Function(choice) choice.Id = LocalPlayer().Car)
                    roster.Text = PlayerLine(grid.Host) & vbCrLf & PlayerLine(grid.Guest)
                    ready.Text = If(LocalPlayer().Ready, "Unready", "Ready")
                    If checking Then
                        status.Text = "Checking this PC’s circuit files…"
                    ElseIf editable AndAlso grid.Layout IsNot Nothing AndAlso grid.Agreement = LayoutAgreement.Pending Then
                        status.Text = "Checking that both PCs have the same circuit files…"
                    ElseIf editable AndAlso grid.Layout IsNot Nothing AndAlso grid.Agreement = LayoutAgreement.Different Then
                        status.Text = RallycrossCatalog.Circuit(grid.Layout.Id).Name & " files differ or are missing. Install the same layout on both PCs, then select it again."
                    End If
                End If
            End If
        Finally
            updating = False
        End Try
    End Sub
    Private Async Function SaveRaceLogs() As Task
        Dim run = lastRunDirectory
        If run Is Nothing OrElse savingLogs Then Return
        Dim role = If(IO.Path.GetFileName(run).StartsWith("host-", StringComparison.Ordinal), "host", "client")
        Using picker As New SaveFileDialog With {.Title = "Save logs from your last Direct race", .Filter = "ZIP archive (*.zip)|*.zip",
                                                 .FileName = $"Direct-{role}-race-logs.zip", .DefaultExt = "zip", .AddExtension = True}
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim destination = picker.FileName
            savingLogs = True
            saveLogs.Enabled = False
            Try
                Await Task.Run(Sub() DirectRaceLogs.Save(run, destination))
                If Not IsDisposed Then AddChat("Saved race logs to " & destination)
            Catch ex As Exception
                If Not IsDisposed Then MessageBox.Show(Me, ex.Message, "Save race logs", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                savingLogs = False
                If Not IsDisposed Then RefreshRoom()
            End Try
        End Using
    End Function
    Private Async Function ConfirmRoundAsync() As Task(Of Boolean)
        Dim current = room
        If current Is Nothing OrElse Not CheckLaunchElevation(Environment.UserInteractive, False, Me) Then Return False
        If room IsNot current OrElse Not current.Connected Then Return False
        current.ConfigureLocalVr = Nothing
        If Not vr.Checked Then Return True
        status.Text = "Checking this PC’s VR runtime and headset…"
        Dim configure = Await DirectVrLaunch.ChooseAsync(Me, context, packageRoot)
        If configure Is Nothing OrElse room IsNot current OrElse Not current.Connected Then Return False
        current.ConfigureLocalVr = configure
        Return True
    End Function
    Private Async Function OpenRoom(asHost As Boolean) As Task
        If room IsNot Nothing Then Return
        joinedHostSaved = False
        networkClosed = False
        Try
            room = New DirectRoom(asHost, stockRoot, context.UserRoot, playerName.Text.Trim(), packageRoot,
                                  AddressOf ConfirmRoundAsync, context.GameRoot)
            room.RecordLapTelemetry = lapLog.Checked
            AddHandler room.Changed, AddressOf RefreshRoom
            AddHandler room.Chat, AddressOf AddChat
            AddHandler room.LocalGameClosed, AddressOf ShowLobbyAfterGame
            RefreshRoom()
            Await room.OpenAsync(address.Text.Trim())
            networkClosed = True
            If Not room.WorkerActive Then ReleaseRoom()
        Catch ex As Exception
            status.Text = ex.Message
            ReleaseRoom()
        End Try
    End Function
    Private Sub ShowLobbyAfterGame()
        If leaving OrElse IsDisposed OrElse Disposing Then Return
        If WindowState = FormWindowState.Minimized Then WindowState = FormWindowState.Normal
        Activate()
    End Sub
    Private Sub ReleaseRoom()
        networkClosed = False
        If room IsNot Nothing Then
            RemoveHandler room.Changed, AddressOf RefreshRoom
            RemoveHandler room.Chat, AddressOf AddChat
            RemoveHandler room.LocalGameClosed, AddressOf ShowLobbyAfterGame
            room.Dispose() : room = Nothing
        End If
        If remoteCircuit IsNot Nothing Then
            track.Items.Remove(remoteCircuit) : remoteCircuit = Nothing
            track.SelectedIndex = 0
        End If
        If Not IsDisposed Then
            roster.Text = ""
            RefreshRoom()
        End If
    End Sub
    Private Async Function Command(action As Func(Of Task)) As Task
        If room Is Nothing Then Return
        Try
            Await action()
        Catch ex As Exception
            If Not IsDisposed Then status.Text = ex.Message
        End Try
    End Function
    Private Async Sub SelectionChanged(sender As Object, e As EventArgs)
        If updating OrElse room Is Nothing OrElse Not room.Connected OrElse room.Starting Then Return
        Dim selectedCar = DirectCast(car.SelectedItem, RallycrossChoice).Id
        If sender Is car Then
            Await Command(Function() room.SelectAsync(selectedCar, room.Grid.Track))
            Return
        End If
        Dim selection = TryCast(track.SelectedItem, RallycrossChoice)
        If selection Is Nothing Then Return
        If room.IsHost AndAlso selection.LayoutId IsNot Nothing Then
            Try
                Await room.SelectLayoutAsync(selectedCar, selection.LayoutId)
            Catch ex As OperationCanceledException
                ' A newer selection or a closed lobby cancelled this file check.
            Catch ex As Exception
                If Not IsDisposed Then status.Text = ex.Message
            End Try
        Else
            Await Command(Function() room.SelectAsync(selectedCar, selection.Id))
        End If
    End Sub
    Private Async Function SendChat() As Task
        Dim value = message.Text.Trim()
        If value.Length = 0 Then Return
        message.Clear()
        Await Command(Function() room.ChatAsync(value))
    End Function
    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If room IsNot Nothing AndAlso room.WorkerActive Then
            e.Cancel = True : leaving = True
            status.Text = "Closing Direct. If the race is running, quit DiRT 2 to return."
        End If
        room?.Dispose()
        MyBase.OnFormClosing(e)
    End Sub
End Class
