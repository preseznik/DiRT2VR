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
    Public Shared Function Patch(original As Byte(), flow As Boolean, Optional skipMovies As Boolean = False, Optional savedReplay As Boolean = False, Optional recordReplay As Boolean = True) As Byte()
        recordReplay = recordReplay OrElse savedReplay
        Dim expected = If(flow, FlowHash, StartupMovies.OriginalHash)
        If Convert.ToHexString(Security.Cryptography.SHA256.HashData(original)) <> expected Then Throw New IOException("Direct-session menus require the original supported system files.")
        If Not flow AndAlso skipMovies Then original = StartupMovies.Patch(original)
        Using input As New MemoryStream(original)
            Dim binary As New XmlFile(input), document = binary.Document
            If flow Then
                Dim root = One(document, "//node[@id='S']")
                LoadDrivingProfile(document, root)
                ' The native hook identifies this dedicated shutdown state. The session
                ' manager restores files before starting a fresh Normal Launch process.
                Dim leave = Node(root, "d2vr_leave", "d2vr_return")
                ' Ordinary native shutdown must not signal a Return to menus relaunch.
                Dim quit = Node(root, "d2vr_quit", "exit_game")
                For Each group In {("11e", "11l", "1e1", "1c6"), ("27q", "27w", "2lc", "2ih")}
                    Dim finish = One(document, "//node[@id='" & group.Item1 & "']")
                    ' Stop before career/result advancement. The unmodified graph is
                    ' restored before the subsequent Normal Launch process starts.
                    finish.RemoveAll() : finish.SetAttribute("id", group.Item1) : finish.SetAttribute("state", "d2vr_full_replay_check")
                    For Each available In {True, False}
                        Dim menu = Node(finish.ParentNode, "d2vr_finish_" & group.Item1 & If(available, "", "_unavailable"),
                            If(available, "d2vr_finish_menu", "d2vr_finish_no_replay"))
                        Link(finish, If(available, "enabled", "disabled"), menu.GetAttribute("id"))
                        Link(menu, "restart_race", group.Item2) : Link(menu, "back", group.Item1)
                        Link(menu, "return_menus", leave.GetAttribute("id"))
                        Link(menu, "quit_game", quit.GetAttribute("id"))
                        If available Then Link(menu, "view_replay", If(group.Item1 = "11e", "10V", "276"))
                    Next
                    If Not recordReplay Then
                        finish.SetAttribute("state", "d2vr_finish_no_replay")
                        For Each child As XmlNode In finish.ChildNodes.Cast(Of XmlNode)().ToArray() : finish.RemoveChild(child) : Next
                        Link(finish, "restart_race", group.Item2) : Link(finish, "back", group.Item1)
                        Link(finish, "return_menus", leave.GetAttribute("id"))
                        Link(finish, "quit_game", quit.GetAttribute("id"))
                    End If
                    ' Keep native recording enabled, but stop at our menu before any
                    ' automatic playback. Manual viewing reuses the complete native
                    ' setup/cleanup path, including its replay interface and audio.
                    Dim first = group.Item1 = "11e"
                    Dim stopReplay = Node(finish.ParentNode, "d2vr_stop_" & group.Item1, "replay_stop")
                    One(document, "//node[@id='" & If(first, "10T", "274") & "']/link[@id='next']").SetAttribute("target", stopReplay.GetAttribute("id"))
                    Link(stopReplay, "next", If(first, "1ae", "2gq"))
                    One(document, "//node[@id='" & If(first, "119", "27l") & "']/link[@id='loop']").SetAttribute("target", If(first, "11a", "27m"))
                    ' The replay screen owns its input and presentation. Release that
                    ' screen before showing a pause screen; nesting the two hides the
                    ' menu and traps playback. Resume re-enters without replay_begin.
                    Dim replayPlay = If(first, "119", "27l")
                    Dim replayExit = If(first, "11a", "27m")
                    Dim replayScope = finish.ParentNode
                    Dim replayPause = Node(replayScope, "d2vr_replay_pause_" & group.Item1, "set_replay_system_pause")
                    Dim replayMenu = Node(replayScope, "d2vr_replay_menu_" & group.Item1, "d2vr_replay_menu")
                    Dim replayRearm = Node(replayScope, "d2vr_replay_rearm_" & group.Item1, "d2vr_replay_rearm")
                    Dim replayResume = Node(replayScope, "d2vr_replay_resume_" & group.Item1, "set_replay_system_play")
                    One(document, "//node[@id='" & replayPlay & "']/link[@id='next']").SetAttribute("target", replayPause.GetAttribute("id"))
                    Link(replayPause, "next", replayMenu.GetAttribute("id"))
                    Link(replayMenu, "back", replayRearm.GetAttribute("id"))
                    Link(replayRearm, "enabled", replayResume.GetAttribute("id"))
                    Link(replayRearm, "disabled", replayExit)
                    Link(replayResume, "next", replayPlay)
                    Link(replayMenu, "view_replay", If(first, "10V", "276"))
                    Link(replayMenu, "exit_replay", replayExit)
                    Link(replayMenu, "quit_game", quit.GetAttribute("id"))
                    ' Replay exit resumes its native playback clock, even when the
                    ' pause-decorated finish menu is visible. Pause that clock after
                    ' render cleanup; replay_begin resets it when watching again.
                    Dim holdReplay = Node(finish.ParentNode, "d2vr_hold_" & group.Item1, "set_replay_system_pause")
                    One(document, "//node[@id='" & If(first, "11d", "27p") & "']/link[@id='next']").SetAttribute("target", holdReplay.GetAttribute("id"))
                    Link(holdReplay, "next", group.Item1)
                    Dim pause = One(document, "//node[@id='" & group.Item3 & "']")
                    Link(pause, "restart_race", group.Item4)
                    Dim unpause = Node(pause.ParentNode, "d2vr_unpause_" & group.Item1, "send_unpause_msg")
                    Dim endRace = Node(pause.ParentNode.ParentNode, "d2vr_end_" & group.Item1, "end_race")
                    Link(pause, "return_menus", unpause.GetAttribute("id"))
                    Link(unpause, "next", endRace.GetAttribute("id")) : Link(endRace, "next", leave.GetAttribute("id"))
                    Dim quitUnpause = Node(pause.ParentNode, "d2vr_quit_unpause_" & group.Item1, "send_unpause_msg")
                    Dim quitRace = Node(pause.ParentNode.ParentNode, "d2vr_quit_end_" & group.Item1, "end_race")
                    Link(pause, "quit_game", quitUnpause.GetAttribute("id"))
                    Link(quitUnpause, "next", quitRace.GetAttribute("id")) : Link(quitRace, "next", quit.GetAttribute("id"))
                Next
                ' This runs only at a fresh attempt, never while resuming flashback
                ' or watching the same recording again.
                For Each record As XmlElement In document.SelectNodes("//node[@state='set_replay_system_record']").Cast(Of XmlElement)().ToArray()
                    Dim native = Node(record.ParentNode, "d2vr_record_" & record.GetAttribute("id"), "set_replay_system_record")
                    For Each child As XmlNode In record.ChildNodes.Cast(Of XmlNode)().ToArray()
                        native.AppendChild(child)
                    Next
                    record.SetAttribute("state", "d2vr_record_begin")
                    Link(record, "enabled", native.GetAttribute("id")) : Link(record, "disabled", native.GetAttribute("id"))
                Next
                If savedReplay Then PrepareSavedPlayback(document)
            Else
                Dim replayCheck = DirectCast(One(document, "//StateReplayEnabledCheck[@id='replay_enabled_check']").CloneNode(True), XmlElement)
                replayCheck.SetAttribute("id", "d2vr_full_replay_check") : document.DocumentElement.AppendChild(replayCheck)
                Dim recordBegin = DirectCast(replayCheck.CloneNode(True), XmlElement)
                recordBegin.SetAttribute("id", "d2vr_record_begin") : document.DocumentElement.AppendChild(recordBegin)
                Dim replayRearm = DirectCast(replayCheck.CloneNode(True), XmlElement)
                replayRearm.SetAttribute("id", "d2vr_replay_rearm") : document.DocumentElement.AppendChild(replayRearm)
                Dim shutdown = document.CreateElement("StateShutDownGame")
                shutdown.SetAttribute("id", "d2vr_return") : document.DocumentElement.AppendChild(shutdown)
                Dim profileFailure = DirectCast(shutdown.CloneNode(True), XmlElement)
                profileFailure.SetAttribute("id", "d2vr_profile_failed") : document.DocumentElement.AppendChild(profileFailure)
                Dim pause = One(document, "//ScreenPauseDecorator[@id='benchmark_pause_menu']")
                Dim finish = DirectCast(pause.CloneNode(True), XmlElement)
                finish.SetAttribute("id", "d2vr_finish_menu") : pause.ParentNode.AppendChild(finish)
                Dim unavailable = DirectCast(pause.CloneNode(True), XmlElement)
                unavailable.SetAttribute("id", "d2vr_finish_no_replay") : pause.ParentNode.AppendChild(unavailable)
                Dim replayMenu = DirectCast(pause.CloneNode(True), XmlElement)
                replayMenu.SetAttribute("id", "d2vr_replay_menu") : pause.ParentNode.AppendChild(replayMenu)
                BuildMenu(replayMenu, True, True, True)
                BuildMenu(pause, True) : BuildMenu(finish, False) : BuildMenu(unavailable, False, False, recordingOff:=Not recordReplay)
                If savedReplay Then
                    ' A library replay cannot restart its one-second initialization race.
                    For Each screen In {pause, finish, unavailable}
                        RemoveRestart(screen)
                    Next
                End If
            End If
            Using output As New MemoryStream()
                binary.Write(output, XmlType.BinXml) : Return output.ToArray()
            End Using
        End Using
    End Function
    Private Shared Sub RemoveRestart(screen As XmlElement)
        Dim generic = One(screen.OwnerDocument, "//*[@id='" & screen.GetAttribute("id") & "']/ScreenGeneric")
        For Each item As XmlElement In generic.SelectNodes("Item[IBSelectableSimple/@link='restart_race']")
            generic.RemoveChild(item)
        Next
        generic.SelectSingleNode("itemflow").InnerText = vbLf & String.Join(vbLf, generic.SelectNodes("Item[IBSelectableSimple]").Cast(Of XmlElement)().Select(Function(i) i.GetAttribute("id"))) & vbLf
    End Sub
    Private Shared Sub PrepareSavedPlayback(document As XmlDocument)
        For Each pair In {("11e", "10V"), ("27q", "276")}
            Dim stopNode = One(document, "//node[@id='d2vr_stop_" & pair.Item1 & "']")
            Dim gate = Node(stopNode.ParentNode, "d2vr_saved_" & pair.Item1, "d2vr_full_replay_check")
            Link(gate, "enabled", pair.Item2) : Link(gate, "disabled", pair.Item1)
            DirectCast(stopNode.SelectSingleNode("link[@id='next']"), XmlElement).SetAttribute("target", gate.GetAttribute("id"))
        Next
        For Each record As XmlElement In document.SelectNodes("//node[@state='set_replay_system_record']")
            Dim scope As XmlElement = record
            While scope IsNot Nothing AndAlso scope.GetAttribute("id") <> "Y8" AndAlso scope.GetAttribute("id") <> "24j"
                scope = TryCast(scope.ParentNode, XmlElement)
            End While
            If scope Is Nothing Then Continue For
            Dim suffix = record.GetAttribute("id")
            Dim wait = Node(record.ParentNode, "d2vr_saved_wait_" & suffix, "necessary_1_second_pause")
            Dim finish = Node(record.ParentNode, "d2vr_saved_end_" & suffix, "end_race")
            Link(wait, "next", finish.GetAttribute("id"))
            Link(finish, "next", If(scope.GetAttribute("id") = "Y8", "10G", "26R"))
            DirectCast(record.SelectSingleNode("link[@id='next']"), XmlElement).SetAttribute("target", wait.GetAttribute("id"))
        Next
    End Sub
    Private Shared Sub LoadDrivingProfile(document As XmlDocument, root As XmlElement)
        ' Demo startup skips the press-start/profile sequence. Use the native loader
        ' before constructing the selected event; never create or save a career here.
        Dim entry = One(document, "//node[@id='c']/link[@id='skip_no_garage']")
        If entry.GetAttribute("target") <> "2FG" Then Throw New IOException("Unsupported direct profile entry.")
        entry.SetAttribute("target", "d2vr_control_signin_check")
        ' Reuse the native account check: already-signed-in players go straight
        ' to loading. Only an absent account opens the provider's sign-in UI.
        Dim signedIn = Node(root, "d2vr_control_signin_check", "press_start_handle_signin")
        Link(signedIn, "autoload", "d2vr_control_context")
        Link(signedIn, "signin", "d2vr_control_signin")
        Link(signedIn, "createprofile", "d2vr_control_failed")
        Dim signIn = Node(root, "d2vr_control_signin", "signingamerprofile")
        Link(signIn, "autoload", "d2vr_control_context")
        Link(signIn, "createprofile", "d2vr_control_failed")
        Link(signIn, "press_start", "d2vr_control_failed")
        Node(root, "d2vr_control_failed", "d2vr_profile_failed")
        Link(Node(root, "d2vr_control_context", "create_protected_data_context"), "next", "d2vr_control_dataset")
        Link(Node(root, "d2vr_control_dataset", "load_profile_dataset_to_ep"), "next", "d2vr_control_enum")
        Dim enumerate = Node(root, "d2vr_control_enum", "enumerateprofiles")
        Link(enumerate, "next", "d2vr_control_load")
        Link(enumerate, "nonefound", "2FG")
        ' The regular half-second enumeration popup needs the frontend screen stack.
        ' Stay in enumeration until it completes; its overlay is unnecessary here.
        Dim load = Node(root, "d2vr_control_load", "auto_load_profile")
        For Each outcome In {"success", "cancelled", "skipped", "x360fail", "pcfail", "no_existing_save"}
            Link(load, outcome, If(outcome = "pcfail" OrElse outcome = "x360fail", "d2vr_control_failed", "2FG"))
        Next
    End Sub
    Private Shared Sub BuildMenu(screen As XmlElement, paused As Boolean, Optional replayAvailable As Boolean = True, Optional replayPaused As Boolean = False, Optional recordingOff As Boolean = False)
        Dim generic = DirectCast(screen.SelectSingleNode("ScreenGeneric"), XmlElement)
        Dim template = DirectCast(generic.SelectSingleNode("Item[@id='item_0']"), XmlElement)
        Dim items As New List(Of (String, String))
        If paused Then items.Add(("lng_continue_button_title", "back"))
        If Not paused Then items.Add((If(replayAvailable, "Full Replay", If(recordingOff, "Full Replay - recording is off", "Full Replay - recording unavailable")), "view_replay"))
        If replayPaused Then
            items.Add(("Full Replay", "view_replay"))
            items.Add(("Exit replay", "exit_replay"))
        Else
            items.Add(("lng_restart_button_title", "restart_race"))
            items.Add(("Return to menus", "return_menus"))
        End If
        items.Add(("Quit", "quit_game"))
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
            If Not paused AndAlso Not replayAvailable AndAlso i = 0 Then item.RemoveChild(item.SelectSingleNode("IBSelectableSimple"))
            generic.AppendChild(item)
        Next
        ' itemflow uses newlines for rows and spaces for columns, regardless of glyph placement.
        Dim firstSelectable = If(Not paused AndAlso Not replayAvailable, 1, 0)
        generic.SelectSingleNode("itemflow").InnerText = vbLf & String.Join(vbLf, Enumerable.Range(firstSelectable, items.Count - firstSelectable).Select(Function(i) "item_" & i)) & vbLf
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
    Public Sub Prepare(Optional skipMovies As Boolean = False, Optional afterWrite As Action(Of Integer) = Nothing, Optional savedReplay As Boolean = False, Optional recordReplay As Boolean = True)
        CheckPaths()
        If Pending OrElse New StartupMovies(context).Pending OrElse New LanTransaction(context).BlocksMenuChanges Then Throw New IOException("Restore pending files before preparing direct-session menus.")
        Dim original = Names.Select(Function(n) File.ReadAllBytes(IO.Path.Combine(context.GameRoot, "system", n))).ToArray()
        Dim modified = {Patch(original(0), False, skipMovies, savedReplay, recordReplay), Patch(original(1), True, savedReplay:=savedReplay, recordReplay:=recordReplay)}
        Dim journal As New DirectMenuJournal With {.Version = 2, .ReadOnlyFiles = New List(Of Boolean)}
        For i = 0 To 1
            If File.Exists(Backup(i)) Then
                If Files.Hash(Backup(i)) <> Hashes(i) Then Throw New IOException("Original direct-session menu backup changed; it was preserved.")
            Else
                Files.AtomicWrite(Backup(i), original(i))
            End If
            journal.Applied.Add(Convert.ToHexString(Security.Cryptography.SHA256.HashData(modified(i))))
            journal.ReadOnlyFiles.Add((File.GetAttributes(Target(i)) And FileAttributes.ReadOnly) <> 0)
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
        If journal Is Nothing OrElse Not {1, 2}.Contains(journal.Version) OrElse (journal.Version = 2 AndAlso (journal.ReadOnlyFiles Is Nothing OrElse journal.ReadOnlyFiles.Count <> 2)) OrElse journal.Applied Is Nothing OrElse journal.Applied.Count <> 2 OrElse journal.Applied.Any(Function(h) h Is Nothing OrElse Not System.Text.RegularExpressions.Regex.IsMatch(h, "\A[A-Fa-f0-9]{64}\z")) Then Throw New IOException("Invalid direct-session menu journal; files preserved.")
        ' Check both before restoring either. Partly completed recovery remains repeatable.
        For i = 0 To 1
            If Not File.Exists(Backup(i)) OrElse Files.Hash(Backup(i)) <> Hashes(i) Then Throw New IOException("Original direct-session menu backup is missing or changed.")
            If Not File.Exists(Target(i)) OrElse Not {Hashes(i), journal.Applied(i)}.Contains(Files.Hash(Target(i))) Then Throw New IOException("Game menu files changed outside DiRT2VR; files and backups preserved.")
        Next
        For i = 0 To 1
            If Files.Hash(Target(i)) <> Hashes(i) Then Files.AtomicWrite(Target(i), File.ReadAllBytes(Backup(i)))
            If journal.Version = 2 Then CustomTracks.SafeFiles.SetReadOnly(Target(i), journal.ReadOnlyFiles(i))
        Next
        CustomTracks.SafeFiles.DeleteOwned(journalPath)
    End Sub
End Class
Public Class DirectMenuJournal
    Public Property Version As Integer = 1
    Public Property Applied As New List(Of String)
    Public Property ReadOnlyFiles As List(Of Boolean)
End Class
