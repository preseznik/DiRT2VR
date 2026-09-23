Imports EgoEngineLibrary.Xml
Imports System.Xml

' Only direct sessions prepare these files. Normal and LAN launches use the originals.
Public Class DirectMenus
    Public Const FlowHash As String = "D261EA3394627188AF47DBF8F36F22FDF3B99E70835921B8590D739220DC601F"
    Private Shared ReadOnly Names As String() = {"states.bin", "flow.bin"}
    Private Shared ReadOnly Hashes As String() = {StartupMovies.OriginalHash, FlowHash}
    Private ReadOnly context As InstallContext
    Private ReadOnly journalPath As String
    Public Sub New(value As InstallContext)
        context = value
        journalPath = IO.Path.Combine(context.ModRoot, "backups", "direct-menus-pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Private Shared Function One(document As XmlDocument, xpath As String) As XmlElement
        Dim found = document.SelectNodes(xpath)
        If found.Count <> 1 Then Throw New IOException("Unsupported direct-session menu definition: " & xpath)
        Return DirectCast(found(0), XmlElement)
    End Function
    Private Shared Sub Link(node As XmlElement, id As String, target As String)
        Dim child = node.OwnerDocument.CreateElement("link")
        child.SetAttribute("id", id) : child.SetAttribute("target", target) : node.AppendChild(child)
    End Sub
    Private Shared Function Node(parent As XmlNode, id As String, state As String) As XmlElement
        Dim child = parent.OwnerDocument.CreateElement("node")
        child.SetAttribute("id", id) : child.SetAttribute("state", state) : parent.AppendChild(child)
        Return child
    End Function
    Public Shared Function Patch(original As Byte(), flow As Boolean, Optional skipMovies As Boolean = False) As Byte()
        Dim expected = If(flow, FlowHash, StartupMovies.OriginalHash)
        If Convert.ToHexString(Security.Cryptography.SHA256.HashData(original)) <> expected Then Throw New IOException("Direct-session menus require the original supported system files.")
        If Not flow AndAlso skipMovies Then original = StartupMovies.Patch(original)
        Using input As New MemoryStream(original)
            Dim binary As New XmlFile(input), document = binary.Document
            If flow Then
                Dim root = One(document, "//node[@id='S']")
                ' The native hook identifies this dedicated shutdown state. The session
                ' manager restores files before starting a fresh Normal Launch process.
                Dim leave = Node(root, "d2vr_leave", "d2vr_return")
                For Each group In {("11e", "11l", "1e1", "1c6"), ("27q", "27w", "2lc", "2ih")}
                    Dim finish = One(document, "//node[@id='" & group.Item1 & "']")
                    ' Stop before career/result advancement. The unmodified graph is
                    ' restored before the subsequent Normal Launch process starts.
                    finish.RemoveAll() : finish.SetAttribute("id", group.Item1) : finish.SetAttribute("state", "d2vr_finish_menu")
                    Link(finish, "restart_race", group.Item2) : Link(finish, "back", group.Item1)
                    Link(finish, "return_menus", leave.GetAttribute("id"))
                    Dim pause = One(document, "//node[@id='" & group.Item3 & "']")
                    Link(pause, "restart_race", group.Item4)
                    Dim unpause = Node(pause.ParentNode, "d2vr_unpause_" & group.Item1, "send_unpause_msg")
                    Dim endRace = Node(pause.ParentNode.ParentNode, "d2vr_end_" & group.Item1, "end_race")
                    Link(pause, "return_menus", unpause.GetAttribute("id"))
                    Link(unpause, "next", endRace.GetAttribute("id")) : Link(endRace, "next", leave.GetAttribute("id"))
                Next
            Else
                Dim shutdown = document.CreateElement("StateShutDownGame")
                shutdown.SetAttribute("id", "d2vr_return") : document.DocumentElement.AppendChild(shutdown)
                Dim pause = One(document, "//ScreenPauseDecorator[@id='benchmark_pause_menu']")
                Dim finish = DirectCast(pause.CloneNode(True), XmlElement)
                finish.SetAttribute("id", "d2vr_finish_menu") : pause.ParentNode.AppendChild(finish)
                BuildMenu(pause, True) : BuildMenu(finish, False)
            End If
            Using output As New MemoryStream()
                binary.Write(output, XmlType.BinXml) : Return output.ToArray()
            End Using
        End Using
    End Function
    Private Shared Sub BuildMenu(screen As XmlElement, paused As Boolean)
        Dim generic = DirectCast(screen.SelectSingleNode("ScreenGeneric"), XmlElement)
        Dim template = DirectCast(generic.SelectSingleNode("Item[@id='item_0']"), XmlElement)
        Dim items As New List(Of (String, String))
        If paused Then items.Add(("lng_continue_button_title", "back"))
        items.Add(("lng_restart_button_title", "restart_race"))
        items.Add(("Return to menus", "return_menus"))
        generic.RemoveChild(template)
        For i = 0 To items.Count - 1
            Dim item = DirectCast(template.CloneNode(True), XmlElement)
            item.SetAttribute("id", "item_" & i)
            item.SetAttribute("glyph", i & ".root.library")
            Dim data = DirectCast(item.SelectSingleNode("IBScreenGenericData"), XmlElement)
            data.SetAttribute("index", i.ToString()) : data.SetAttribute("size", "1")
            DirectCast(item.SelectSingleNode("IBGlyphLink"), XmlElement).SetAttribute("linked_glyph", i & ".root.library.pill")
            DirectCast(item.SelectSingleNode("IBTextStatic"), XmlElement).SetAttribute("string", items(i).Item1)
            DirectCast(item.SelectSingleNode("IBSelectableSimple"), XmlElement).SetAttribute("link", items(i).Item2)
            generic.AppendChild(item)
        Next
        generic.SelectSingleNode("itemflow").InnerText = String.Join(" ", Enumerable.Range(0, items.Count).Select(Function(i) "item_" & i))
        If Not paused Then DirectCast(generic.SelectSingleNode("Item[@id='title']/IBTextStatic"), XmlElement).SetAttribute("string", "lng_net_race_in_progress_finished")
    End Sub
    Private Function Target(index As Integer) As String
        Return IO.Path.Combine(context.GameRoot, "system", Names(index))
    End Function
    Private Function Backup(index As Integer) As String
        Return IO.Path.Combine(context.ModRoot, "backups", "direct-menus-" & Names(index))
    End Function
    Private Sub CheckPaths()
        context.RequireClosed() : Files.NoLinks(journalPath)
        For i = 0 To 1
            Files.NoLinks(Target(i)) : Files.NoLinks(Backup(i))
        Next
    End Sub
    Public Sub Prepare(Optional skipMovies As Boolean = False, Optional afterWrite As Action(Of Integer) = Nothing)
        CheckPaths()
        If Pending OrElse New StartupMovies(context).Pending OrElse New LanTransaction(context).Pending Then Throw New IOException("Restore pending files before preparing direct-session menus.")
        Dim original = Names.Select(Function(n) File.ReadAllBytes(IO.Path.Combine(context.GameRoot, "system", n))).ToArray()
        Dim modified = {Patch(original(0), False, skipMovies), Patch(original(1), True)}
        Dim journal As New DirectMenuJournal
        For i = 0 To 1
            If File.Exists(Backup(i)) Then
                If Files.Hash(Backup(i)) <> Hashes(i) Then Throw New IOException("Original direct-session menu backup changed; it was preserved.")
            Else
                Files.AtomicWrite(Backup(i), original(i))
            End If
            journal.Applied.Add(Convert.ToHexString(Security.Cryptography.SHA256.HashData(modified(i))))
        Next
        Files.SaveJson(journalPath, journal)
        For i = 0 To 1
            If Files.Hash(Target(i)) <> Hashes(i) Then Throw New IOException("Game menu files changed during preparation.")
            Files.AtomicWrite(Target(i), modified(i)) : afterWrite?.Invoke(i)
        Next
    End Sub
    Public Sub Recover()
        CheckPaths()
        If Not Pending Then Return
        Dim journal = Files.ReadJson(Of DirectMenuJournal)(journalPath)
        If journal Is Nothing OrElse journal.Version <> 1 OrElse journal.Applied Is Nothing OrElse journal.Applied.Count <> 2 OrElse journal.Applied.Any(Function(h) h Is Nothing OrElse Not System.Text.RegularExpressions.Regex.IsMatch(h, "\A[A-Fa-f0-9]{64}\z")) Then Throw New IOException("Invalid direct-session menu journal; files preserved.")
        ' Check both before restoring either. Partly completed recovery remains repeatable.
        For i = 0 To 1
            If Not File.Exists(Backup(i)) OrElse Files.Hash(Backup(i)) <> Hashes(i) Then Throw New IOException("Original direct-session menu backup is missing or changed.")
            If Not File.Exists(Target(i)) OrElse Not {Hashes(i), journal.Applied(i)}.Contains(Files.Hash(Target(i))) Then Throw New IOException("Game menu files changed outside DiRT2VR; files and backups preserved.")
        Next
        For i = 0 To 1
            If Files.Hash(Target(i)) <> Hashes(i) Then Files.AtomicWrite(Target(i), File.ReadAllBytes(Backup(i)))
        Next
        File.Delete(journalPath)
    End Sub
End Class
Public Class DirectMenuJournal
    Public Property Version As Integer = 1
    Public Property Applied As New List(Of String)
End Class
