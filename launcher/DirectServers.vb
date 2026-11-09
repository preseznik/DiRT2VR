Imports System.Net
Imports System.Net.Sockets
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports DiRT2VR.Multiplayer

Public Class DirectServers
    Inherits FlowLayoutPanel

    Private ReadOnly entry As TextBox
    Private ReadOnly directoryName As String
    Private ReadOnly favorites As New ComboBox With {.Name = "DirectFavorites", .AccessibleName = "Favorite hosts", .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 145}
    Private ReadOnly found As New ComboBox With {.Name = "DirectFoundHosts", .AccessibleName = "Discovered LAN hosts", .DropDownStyle = ComboBoxStyle.DropDownList, .Width = 230}
    Private ReadOnly search As New Button With {.Name = "DirectRefresh", .Text = "Refresh", .AutoSize = True}
    Private ReadOnly feedback As New Label With {.Name = "DirectDiscoveryStatus", .AutoSize = True, .Margin = New Padding(4, 8, 4, 3)}
    Private ReadOnly closing As New CancellationTokenSource()

    Public Sub New(context As InstallContext, address As TextBox)
        entry = address
        directoryName = Path.Combine(context.UserRoot, "direct-servers")
        AutoSize = True : Dock = DockStyle.Fill
        Dim add As New Button With {.Name = "DirectAddFavorite", .Text = "+", .AccessibleName = "Add host IP to favorites", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
        Dim remove As New Button With {.Name = "DirectRemoveFavorite", .Text = "−", .AccessibleName = "Remove selected favorite", .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
        Controls.AddRange({LabelFor("Favorites"), favorites, add, remove, LabelFor("LAN hosts"), found, search, feedback})
        AddHandler favorites.SelectionChangeCommitted, Sub() entry.Text = CStr(favorites.SelectedItem)
        AddHandler found.SelectionChangeCommitted, Sub() entry.Text = DirectCast(found.SelectedItem, DiscoveredLobby).Address.ToString()
        AddHandler add.Click, Sub() ChangeFavorite(True)
        AddHandler remove.Click, Sub() ChangeFavorite(False)
        AddHandler search.Click, Async Sub() Await SearchAsync()
        Try
            ReloadFavorites()
            Dim last = Path.Combine(directoryName, "last-host.txt")
            If File.Exists(last) Then entry.Text = LocalAddress(File.ReadAllText(last))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            feedback.Text = "Could not read saved hosts."
        End Try
    End Sub

    Private Shared Function LabelFor(value As String) As Label
        Return New Label With {.Text = value, .AutoSize = True, .Margin = New Padding(3, 8, 3, 3)}
    End Function

    Private Shared Function LocalAddress(value As String) As String
        Dim ip As IPAddress = Nothing
        If Not IPAddress.TryParse(value, ip) OrElse Not LobbyConnection.IsPrivate(ip) Then Return ""
        Return ip.ToString()
    End Function

    Private Sub ReloadFavorites()
        favorites.Items.Clear()
        If Not Directory.Exists(directoryName) Then Return
        For Each filename In Directory.EnumerateFiles(directoryName, "*.favorite").OrderBy(Function(f) f)
            Dim ip = LocalAddress(Path.GetFileNameWithoutExtension(filename))
            If ip <> "" Then favorites.Items.Add(ip)
        Next
    End Sub

    Private Sub ChangeFavorite(adding As Boolean)
        Dim ip = LocalAddress(If(adding, entry.Text.Trim(), TryCast(favorites.SelectedItem, String)))
        If ip = "" Then
            feedback.Text = If(adding, "Enter a private host IPv4 address first.", "Select a favorite to remove.")
            Return
        End If
        Try
            Dim filename = Path.Combine(directoryName, ip & ".favorite")
            If adding Then
                Directory.CreateDirectory(directoryName)
                Files.AtomicWrite(filename, Array.Empty(Of Byte)())
            Else
                File.Delete(filename)
            End If
            ReloadFavorites()
            If adding Then favorites.SelectedItem = ip
            feedback.Text = If(adding, "Favorite saved.", "Favorite removed.")
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            feedback.Text = "Could not update favorites."
        End Try
    End Sub

    Public Sub RememberJoined(host As String)
        Dim ip = LocalAddress(host)
        If ip = "" Then Return
        Try
            Directory.CreateDirectory(directoryName)
            Files.AtomicWrite(Path.Combine(directoryName, "last-host.txt"), System.Text.Encoding.ASCII.GetBytes(ip))
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
            feedback.Text = "Could not save the last joined host."
        End Try
    End Sub

    Public Async Function SearchAsync() As Task
        If Not search.Enabled OrElse closing.IsCancellationRequested Then Return
        search.Enabled = False
        feedback.Text = "Searching…"
        Try
            Dim hosts = Await DirectDiscovery.FindAsync(LanBrowser.Targets().Select(Function(target) target.Address), closing.Token)
            If closing.IsCancellationRequested Then Return
            found.Items.Clear()
            found.Items.AddRange(hosts.Cast(Of Object)().ToArray())
            feedback.Text = If(hosts.Length = 0, "No open lobbies found.", $"{hosts.Length} found.")
        Catch ex As OperationCanceledException When closing.IsCancellationRequested
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is SocketException OrElse TypeOf ex Is Net.NetworkInformation.NetworkInformationException
            If Not closing.IsCancellationRequested Then feedback.Text = "LAN search unavailable. Enter the host IP to join."
        Finally
            If Not closing.IsCancellationRequested Then search.Enabled = True
        End Try
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing AndAlso Not IsDisposed Then
            closing.Cancel()
            closing.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub
End Class
