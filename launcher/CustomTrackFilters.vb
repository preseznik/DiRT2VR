Imports DiRT2VR.CustomTracks
Imports System.Drawing
Imports System.Windows.Forms

' This is a catalogue filter, never a substitute for launch-time file verification.
Public Class CustomTrackFilters
    Inherits FlowLayoutPanel
    Public Event Changed As EventHandler
    Public ReadOnly Sources As New Button With {.Name = "CustomTrackSources", .AutoSize = True, .AccessibleName = "Filter by source game"}
    Public ReadOnly InstalledOnly As New CheckBox With {.Name = "CustomInstalledOnly", .Text = "Installed tracks only", .AutoSize = True}
    Private ReadOnly choices As New ContextMenuStrip
    Private ReadOnly all As New ToolStripMenuItem("All source games")
    Private ReadOnly games As New Dictionary(Of String, ToolStripMenuItem)(StringComparer.Ordinal)
    Private selected As HashSet(Of String) ' Nothing means all, including future source games.
    Private installedSources As New HashSet(Of String)(StringComparer.Ordinal)
    Public Sub New()
        Name = "CustomTrackFilters" : AutoSize = True : WrapContents = True
        Margin = New Padding(0) : Controls.AddRange({Sources, InstalledOnly})
        choices.Items.Add(all) : choices.Items.Add(New ToolStripSeparator())
        For Each source In TrackPacks.All.GroupBy(Function(p) p.SourceGame).OrderBy(Function(g) g.First().SourceName)
            Dim id = source.Key, item As New ToolStripMenuItem(source.First().SourceName)
            games.Add(id, item) : choices.Items.Add(item)
            AddHandler item.Click, Sub()
                                       If selected Is Nothing Then selected = games.Keys.ToHashSet(StringComparer.Ordinal)
                                       If Not selected.Remove(id) Then selected.Add(id)
                                       If selected.SetEquals(games.Keys) Then selected = Nothing
                                       NotifyChange()
                                   End Sub
        Next
        AddHandler all.Click, Sub()
                                 selected = Nothing : NotifyChange()
                             End Sub
        ' Multiple checks in one visit; Escape or clicking outside still closes it.
        AddHandler choices.Closing, Sub(sender, e) e.Cancel = e.CloseReason = ToolStripDropDownCloseReason.ItemClicked
        AddHandler Sources.Click, Sub()
                                      UpdateChoices()
                                      choices.Show(Sources, New Point(0, Sources.Height))
                                  End Sub
        AddHandler InstalledOnly.CheckedChanged, Sub() NotifyChange()
        AddHandler Disposed, Sub() choices.Dispose()
        UpdateChoices()
    End Sub
    Public Sub Restore(sourceIds As String(), installed As Boolean)
        selected = If(sourceIds Is Nothing, Nothing, sourceIds.Where(Function(id) Not String.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal))
        InstalledOnly.Checked = installed : UpdateChoices()
    End Sub
    Public ReadOnly Property SourceIds As String()
        Get
            Return selected?.OrderBy(Function(id) id).ToArray()
        End Get
    End Property
    Public Function Includes(pack As CustomTrackPack, isInstalled As Boolean) As Boolean
        Return (Not InstalledOnly.Checked OrElse isInstalled) AndAlso
            (selected Is Nothing OrElse (pack.Available AndAlso selected.Contains(TrackPacks.Get(pack.Id).SourceGame)))
    End Function
    Public Sub SetInstalledPacks(ids As IEnumerable(Of String))
        installedSources = ids.Select(Function(id) TrackPacks.Get(id).SourceGame).ToHashSet(StringComparer.Ordinal)
        UpdateChoices()
    End Sub
    Public Sub ShowAll()
        selected = Nothing
        If InstalledOnly.Checked Then
            InstalledOnly.Checked = False
        Else
            NotifyChange()
        End If
    End Sub
    Private Sub NotifyChange()
        UpdateChoices() : RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub
    Private Sub UpdateChoices()
        all.Checked = selected Is Nothing
        For Each entry In games
            entry.Value.Checked = selected Is Nothing OrElse selected.Contains(entry.Key)
            entry.Value.Visible = Not InstalledOnly.Checked OrElse installedSources.Contains(entry.Key)
        Next
        Dim summary = If(selected Is Nothing, "All games", If(selected.Count = 0, "None", If(selected.Count = 1 AndAlso games.ContainsKey(selected.First()), games(selected.First()).Text, selected.Count.ToString() & " games")))
        Sources.Text = "Source: " & summary & "  ▾"
        Sources.AccessibleDescription = "Choose any combination of source games. Escape closes the menu."
    End Sub
End Class
