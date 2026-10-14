Imports System.Drawing
Imports System.Windows.Forms

Public Class ReplayPanel
    Inherits VerticalStack
    Public Event PlaybackRequested(id As String, vr As Boolean)
    Public Event SelectionChanged As EventHandler
    Public ReadOnly Property SelectedTrackId As String
        Get
            Return TryCast(entries.SelectedItem, Entry)?.Item.Manifest?.Content?.TrackId
        End Get
    End Property
    Private ReadOnly context As InstallContext
    Private ReadOnly library As ReplayLibrary
    Private ReadOnly entries As New ListBox With {.Name = "SavedReplays", .AccessibleName = "Saved replays", .IntegralHeight = False, .Height = 220}
    Private ReadOnly detail As New Label With {.AutoSize = True, .UseMnemonic = False, .Name = "ReplayDetails"}
    Private ReadOnly message As New Label With {.AutoSize = True, .UseMnemonic = False, .Name = "ReplayStatus"}
    Private ReadOnly autoSave As New CheckBox With {.Text = "Auto-save completed runs", .AutoSize = True, .Name = "ReplayAutoSave"}
    Private ReadOnly recording As New AppearanceSwitch("ON", "OFF") With {.Name = "ReplayRecording", .AccessibleName = "Record full replays"}
    Private ReadOnly preferences As ReplayPreferences
    Private ReadOnly desktop As New Button With {.Text = "Play", .AutoSize = True, .Name = "PlayReplay"}
    Private ReadOnly headset As New Button With {.Text = "Play in VR", .AutoSize = True, .Name = "PlayReplayVR"}
    Private ReadOnly keep As New Button With {.Text = "Save last replay", .AutoSize = True, .Name = "SaveLastReplay"}
    Private ReadOnly remove As New Button With {.Text = "Delete…", .AutoSize = True, .Name = "DeleteReplay"}
    Private working As Boolean
    Private savingPreferences As Boolean
    Private Class Entry
        Public Item As ReplayLibraryItem
        Public Overrides Function ToString() As String
            If Item.Manifest Is Nothing Then Return IO.Path.GetFileName(Item.Path) & " — Unavailable"
            Return Item.Manifest.RecordedUtc.LocalDateTime.ToString("g") & "  ·  " & Item.Manifest.Name
        End Function
    End Class
    Public Sub New(value As InstallContext)
        context = value : library = New ReplayLibrary(context)
        Name = "ReplayLibrary" : Margin = New Padding(0)
        Controls.Add(New Label With {.Text = "Saved replays (Experimental)", .AutoSize = True, .Font = New Font("Segoe UI", 12, FontStyle.Bold)})
        Controls.Add(New Label With {.Text = "Record full replays", .AutoSize = True, .Font = New Font("Segoe UI", 10, FontStyle.Bold)})
        preferences = ReplayPreferences.Load(context)
        recording.Checked = preferences.RecordFullReplay
        Controls.Add(recording)
        Controls.Add(New Label With {.Text = "Off by default. Enable before launching Direct practice or Race to watch and save the complete run. Applies on the next launch. Instant replay and rewind are unchanged.", .AutoSize = True})
        autoSave.Checked = preferences.AutoSave
        Controls.Add(autoSave) : Controls.Add(entries) : Controls.Add(detail)
        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0, 8, 0, 8)}
        Dim refresh As New Button With {.Text = "Refresh", .AutoSize = True, .Name = "RefreshReplays"}
        Dim folder As New Button With {.Text = "Open folder", .AutoSize = True}
        actions.Controls.AddRange({desktop, headset, keep, refresh, remove, folder}) : Controls.Add(actions)
        Controls.Add(New Label With {.Text = "While recording is on, only the latest completed run is kept unless you save it or enable Auto-save. Saved files are compressed. Existing saved replays can be played with recording off. Replay sharing is not supported yet.", .AutoSize = True})
        Controls.Add(message)
        AddHandler entries.SelectedIndexChanged, Sub() Details()
        AddHandler refresh.Click, Async Sub() Await ReloadAsync()
        AddHandler autoSave.CheckedChanged, Sub() SavePreferences()
        AddHandler recording.CheckedChanged, Sub() SavePreferences()
        AddHandler keep.Click, Async Sub() Await ChangeAsync(Sub() library.KeepLatest(), "Replay saved.")
        AddHandler remove.Click, Async Sub()
                                     Dim item = TryCast(entries.SelectedItem, Entry)?.Item
                                     If item?.Manifest Is Nothing Then Return
                                     If MessageBox.Show(Me, "Delete “" & item.Manifest.Name & "”?", "Delete replay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
                                     Await ChangeAsync(Sub() library.Delete(item.Manifest.Id), "Replay deleted.")
                                 End Sub
        AddHandler desktop.Click, Async Sub() Await PlayAsync(False)
        AddHandler headset.Click, Async Sub() Await PlayAsync(True)
        AddHandler folder.Click, Sub()
                                     Try
                                         Files.NoLinks(library.Root) : Directory.CreateDirectory(library.Root)
                                         Process.Start(New ProcessStartInfo(library.Root) With {.UseShellExecute = True})
                                     Catch ex As Exception
                                         message.Text = ex.Message
                                     End Try
                                 End Sub
        Details()
    End Sub
    Private Sub SavePreferences()
        If savingPreferences Then Return
        savingPreferences = True
        Dim oldRecording = preferences.RecordFullReplay, oldAutoSave = preferences.AutoSave
        preferences.RecordFullReplay = recording.Checked : preferences.AutoSave = autoSave.Checked
        Try
            preferences.Save(context)
            message.Text = "Replay settings saved. Applies on the next launch."
        Catch ex As Exception
            preferences.RecordFullReplay = oldRecording : preferences.AutoSave = oldAutoSave
            recording.Checked = oldRecording : autoSave.Checked = oldAutoSave
            message.Text = ex.Message
        Finally
            savingPreferences = False
        End Try
        Details()
    End Sub
    Public Async Function ReloadAsync() As Tasks.Task
        If working Then Return
        working = True : Details()
        Try
            Dim selected = TryCast(entries.SelectedItem, Entry)?.Item.Path
            Dim items = Await Tasks.Task.Run(Function() library.Items())
            entries.BeginUpdate()
            Try
                entries.Items.Clear()
                For Each item In items : entries.Items.Add(New Entry With {.Item = item}) : Next
                entries.SelectedItem = entries.Items.Cast(Of Entry)().FirstOrDefault(Function(i) i.Item.Path = selected)
                If entries.SelectedIndex < 0 AndAlso entries.Items.Count > 0 Then entries.SelectedIndex = 0
            Finally
                entries.EndUpdate()
            End Try
            message.Text = If(items.Count = 0, "No saved replays yet.", $"{items.Count} saved replay(s). Cars and track are checked before playback.")
        Catch ex As Exception
            message.Text = ex.Message
        Finally
            working = False : Details()
        End Try
    End Function
    Private Shared Sub WithSessionLock(action As Action)
        Using guard As New Mutex(False, "Global\DiRT2VR.Session")
            Dim held As Boolean
            Try : held = guard.WaitOne(0)
            Catch ex As AbandonedMutexException : held = True
            End Try
            If Not held Then Throw New IOException("Finish the current game session before changing or playing saved replays.")
            Try : action()
            Finally : guard.ReleaseMutex()
            End Try
        End Using
    End Sub
    Private Async Function ChangeAsync(action As Action, success As String) As Tasks.Task
        If working Then Return
        working = True : Details() : message.Text = "Working…"
        Try
            Await Tasks.Task.Run(Sub() WithSessionLock(Sub()
                                                          context.RequireClosed() : action()
                                                      End Sub))
            working = False : Await ReloadAsync() : message.Text = success
        Catch ex As Exception
            message.Text = ex.Message
        Finally
            working = False : Details()
        End Try
    End Function
    Private Async Function PlayAsync(vr As Boolean) As Tasks.Task
        Dim item = TryCast(entries.SelectedItem, Entry)?.Item
        If working OrElse item?.Manifest Is Nothing Then Return
        working = True : Details() : message.Text = "Checking replay, cars and track…"
        Try
            Await Tasks.Task.Run(Sub() WithSessionLock(Sub()
                                                          context.RequireClosed()
                                                          library.RequireOwned(item.Path)
                                                          ReplayContent.RequireAvailable(context, item.Manifest.Content)
                                                      End Sub))
            RaiseEvent PlaybackRequested(item.Manifest.Id, vr)
            message.Text = "Starting replay…"
        Catch ex As Exception
            message.Text = ex.Message
        Finally
            working = False : Details()
        End Try
    End Function
    Private Sub Details()
        RaiseEvent SelectionChanged(Me, EventArgs.Empty)
        Dim item = TryCast(entries.SelectedItem, Entry)?.Item
        desktop.Enabled = Not working AndAlso item?.Manifest IsNot Nothing
        headset.Enabled = desktop.Enabled : remove.Enabled = desktop.Enabled : keep.Enabled = Not working
        recording.Enabled = Not working
        autoSave.Enabled = Not working AndAlso recording.Checked
        If item Is Nothing Then
            detail.Text = "Choose a replay to view its details."
        ElseIf item.Problem <> "" Then
            detail.Text = item.Problem
        Else
            Dim length = TimeSpan.FromSeconds(item.Manifest.DurationSeconds)
            detail.Text = item.Manifest.Name & vbCrLf &
                $"{length:hh\:mm\:ss}  ·  {item.StoredBytes / 1048576.0:F1} MB  ·  {item.Manifest.Laps} lap(s)"
            If item.Manifest.Content.Cars.Count > 1 Then detail.Text &= vbCrLf &
                String.Join(", ", item.Manifest.Content.Cars.Select(Function(c) RaceCatalog.Current.Car(c).Label))
        End If
    End Sub
    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        For Each label In Controls.OfType(Of Label)()
            label.MaximumSize = New Size(Math.Max(1, ClientSize.Width - label.Margin.Horizontal), 0)
        Next
        MyBase.OnSizeChanged(e)
    End Sub
End Class
