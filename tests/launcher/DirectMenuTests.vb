Imports System.IO
Imports System.Xml
Imports DiRT2VR
Imports EgoEngineLibrary.Xml

Module DirectMenuTests
    Public Sub Run(repo As String, folder As String, check As Action(Of Boolean, String))
        Dim context As New InstallContext(Path.Combine(folder, "Direct menu game"), Path.Combine(folder, "Direct menu user"))
        Dim targets = {"states.bin", "flow.bin"}.Select(Function(n) Path.Combine(context.GameRoot, "system", n)).ToArray()
        Directory.CreateDirectory(Path.GetDirectoryName(targets(0)))
        For Each target In targets
            File.Copy(Path.Combine(repo, "artifacts/game/system", Path.GetFileName(target)), target)
        Next
        Dim original = targets.Select(Function(p) File.ReadAllBytes(p)).ToArray()
        Dim transaction As New DirectMenus(context)
        For Each skip In {False, True}
            transaction.Prepare(skip)
            If Not skip Then
                File.Copy(targets(0), Path.Combine(folder, "direct-controls-states.bin"))
                File.Copy(targets(1), Path.Combine(folder, "direct-controls-flow.bin"))
            End If
            Using input = File.OpenRead(targets(0))
                Dim doc = (New XmlFile(input)).Document
                check(doc.SelectNodes("//ScreenPauseDecorator[@id='benchmark_pause_menu']//IBSelectableSimple").Count = 3, "direct pause has Continue, Restart and menu return")
                check(doc.SelectNodes("//ScreenPauseDecorator[@id='d2vr_finish_menu']//IBSelectableSimple").Count = 2, "direct finish offers two choices")
                For Each menu In {"benchmark_pause_menu", "d2vr_finish_menu"}
                    Dim rows = doc.SelectNodes("//ScreenPauseDecorator[@id='" & menu & "']//Item[@glyph]").Cast(Of XmlElement)().ToArray()
                    check(rows.Select(Function(row) row.GetAttribute("glyph")).Distinct().Count() = rows.Length, "menu choices use distinct visible rows")
                    check(rows.All(Function(row) row.SelectSingleNode("IBScreenGenericData").Attributes("size").Value = "1" AndAlso row.SelectSingleNode("IBGlyphLink").Attributes("linked_glyph").Value = row.GetAttribute("glyph") & ".pill"), "menu highlights and layout match their rows")
                Next
                check(doc.SelectNodes("//StateVideoPreStart[@id='sting_video']").Count = If(skip, 0, 1), "direct menus compose with logo preference")
            End Using
            Using input = File.OpenRead(targets(1))
                Dim doc = (New XmlFile(input)).Document
                Dim ids = doc.SelectNodes("//node").Cast(Of XmlElement)().Select(Function(n) n.GetAttribute("id")).ToHashSet()
                check(ids.Count = doc.SelectNodes("//node").Count, "direct flow has unique node identities")
                ' The original graph already has an unresolved 2FK error link. Do not fix unrelated game data.
                check(doc.SelectNodes("//link").Cast(Of XmlElement)().Where(Function(l) l.GetAttribute("target") <> "2FK").All(Function(l) ids.Contains(l.GetAttribute("target"))), "direct menus introduce no dangling transition targets")
                check(doc.SelectSingleNode("//node[@id='c']/link[@id='skip_no_garage']").Attributes("target").Value = "d2vr_control_context", "direct startup enters native saved-profile loading")
                check(doc.SelectSingleNode("//node[@id='d2vr_control_context']").Attributes("state").Value = "create_protected_data_context" AndAlso
                      doc.SelectSingleNode("//node[@id='d2vr_control_dataset']").Attributes("state").Value = "load_profile_dataset_to_ep", "direct profile uses the existing protected-data backend")
                check(doc.SelectSingleNode("//node[@id='d2vr_control_load']").Attributes("state").Value = "auto_load_profile", "direct startup loads the existing career's control settings")
                check(doc.SelectNodes("//node[starts-with(@id,'d2vr_control_')]").Cast(Of XmlElement)().All(Function(n) Not n.GetAttribute("state").Contains("save") AndAlso Not n.GetAttribute("state").Contains("reset")), "direct control loading neither saves nor resets the career")
                check(doc.SelectNodes("//node[@id='d2vr_control_load']/link").Cast(Of XmlElement)().All(Function(l) l.GetAttribute("target") = "2FG") AndAlso
                      doc.SelectSingleNode("//node[@id='d2vr_control_enum']/link[@id='nonefound']").Attributes("target").Value = "2FG", "missing/cancelled profiles retain the original selected-event path")
                For Each id In {"11e", "27q"}
                    Dim n = DirectCast(doc.SelectSingleNode("//node[@id='" & id & "']"), XmlElement)
                    check(n.GetAttribute("state") = "d2vr_finish_menu" AndAlso n.SelectSingleNode("link[@id='next']") Is Nothing, "direct finish waits for a choice instead of advancing career or repeating")
                    check(n.SelectSingleNode("link[@id='return_menus']").Attributes("target").Value = "d2vr_leave" AndAlso doc.SelectSingleNode("//node[@id='d2vr_leave']").Attributes("state").Value = "d2vr_return", "return uses a dedicated normal shutdown state")
                Next
            End Using
            transaction.Recover()
            check(Not transaction.Pending AndAlso targets.Select(Function(p, i) File.ReadAllBytes(p).SequenceEqual(original(i))).All(Function(x) x), "direct menu originals restored byte-for-byte")
        Next
        Dim interrupted As Boolean
        Try
            transaction.Prepare(afterWrite:=Sub(i) Throw New IOException("power loss after first file"))
        Catch ex As IOException
            interrupted = True
        End Try
        transaction.Recover()
        check(interrupted AndAlso targets.Select(Function(p, i) File.ReadAllBytes(p).SequenceEqual(original(i))).All(Function(x) x), "partial two-file preparation recovers")
        transaction.Prepare()
        Dim modified = File.ReadAllBytes(targets(1))
        File.WriteAllText(targets(1), "external edit")
        Dim conflict As Boolean
        Try
            transaction.Recover()
        Catch ex As IOException
            conflict = True
        End Try
        check(conflict AndAlso transaction.Pending AndAlso File.ReadAllText(targets(1)) = "external edit", "menu recovery preserves conflicting files")
        File.WriteAllBytes(targets(1), modified)
        ' Simulate interrupted recovery where one file has already been restored.
        File.WriteAllBytes(targets(0), original(0))
        transaction.Recover() : transaction.Recover()
        check(Not transaction.Pending AndAlso targets.Select(Function(p, i) File.ReadAllBytes(p).SequenceEqual(original(i))).All(Function(x) x), "partial menu recovery is repeatable")
        Dim start As New ProcessStartInfo()
        Dim previousName As String
        Using channel As New DirectReturnChannel(start, True)
            previousName = start.Environment("DIRT2VR_RETURN_CHANNEL")
            check(Not channel.Requested, "direct return channel starts unrequested")
            Using nativeSide = Threading.EventWaitHandle.OpenExisting(previousName)
                nativeSide.Set()
                check(channel.Requested AndAlso channel.Requested, "explicit native return request persists until game exit")
            End Using
        End Using
        Using channel As New DirectReturnChannel(start, True)
            check(start.Environment("DIRT2VR_RETURN_CHANNEL") <> previousName AndAlso Not channel.Requested, "new launch cannot inherit a previous return request")
        End Using
        Using channel As New DirectReturnChannel(start, False)
            check(Not channel.Requested AndAlso Not start.Environment.ContainsKey("DIRT2VR_RETURN_CHANNEL") AndAlso Not start.Environment.ContainsKey("DIRT2VR_RETURN_PID"), "Normal and LAN launches cannot inherit direct return activation")
        End Using
    End Sub
End Module
