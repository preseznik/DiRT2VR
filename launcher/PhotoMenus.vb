Imports System.Xml

' The callback replaces a screen inside StatePause; it never unpauses the race.
Public NotInheritable Class PhotoMenus
    Private Shared ReadOnly Screens As String() = {"default_pause_menu", "career_event_pause_menu", "challenge_pause_menu", "single_race_pause_menu", "time_trial_pause_menu", "benchmark_pause_menu"}

    Public Shared Sub Patch(document As XmlDocument, flow As Boolean)
        If flow Then
            For Each screen In Screens
                Dim sources = document.SelectNodes("//node[@state='" & screen & "']").Cast(Of XmlElement)().ToArray()
                If sources.Length <> 2 Then Throw New IOException("Unsupported Photo Mode pause flow: " & screen)
                For Each source In sources
                    If source.ParentNode.Attributes?("state")?.Value <> "state_pause" Then Throw New IOException("Photo Mode must remain inside native pause.")
                    Dim id = "d2vr_photo_" & source.GetAttribute("id")
                    Dim photo = document.CreateElement("node")
                    photo.SetAttribute("id", id) : photo.SetAttribute("state", "d2vr_photo_mode")
                    source.ParentNode.AppendChild(photo)
                    AddLink(source, "photo_mode", id)
                    Dim leave = AddLink(photo, "disabled", source.GetAttribute("id"))
                    leave.SetAttribute("type", "back")
                Next
            Next
        Else
            Dim template = One(document, "//StateReplayEnabledCheck[@id='replay_enabled_check']")
            Dim callback = DirectCast(template.CloneNode(True), XmlElement)
            callback.SetAttribute("id", "d2vr_photo_mode") : document.DocumentElement.AppendChild(callback)
            For Each screen In Screens
                AddItem(One(document, "//ScreenPauseDecorator[@id='" & screen & "']/ScreenGeneric"))
            Next
        End If
    End Sub

    Private Shared Sub AddItem(generic As XmlElement)
        Dim rows = generic.SelectNodes("Item[IBSelectableSimple]").Cast(Of XmlElement)().ToList()
        If rows.Count = 0 Then Throw New IOException("Photo Mode requires a selectable pause menu.")
        Dim continueRow = rows.SingleOrDefault(Function(row) row.SelectSingleNode("IBSelectableSimple/@link")?.Value = "back")
        Dim photo = DirectCast(If(continueRow, rows(0)).CloneNode(True), XmlElement)
        photo.SetAttribute("id", "d2vr_photo_mode")
        DirectCast(photo.SelectSingleNode("IBTextStatic"), XmlElement).SetAttribute("string", "PHOTO MODE")
        DirectCast(photo.SelectSingleNode("IBSelectableSimple"), XmlElement).SetAttribute("link", "photo_mode")
        If continueRow Is Nothing Then
            ' Preserve the fallback menu's existing rows; its native back link still resumes.
            generic.InsertBefore(photo, rows(0)) : rows.Insert(0, photo)
        Else
            generic.InsertAfter(photo, continueRow) : rows.Insert(rows.IndexOf(continueRow) + 1, photo)
        End If
        For i = 0 To rows.Count - 1
            rows(i).SetAttribute("glyph", i & ".root.library")
            Dim data = DirectCast(rows(i).SelectSingleNode("IBScreenGenericData"), XmlElement)
            data.SetAttribute("index", i.ToString(Globalization.CultureInfo.InvariantCulture)) : data.SetAttribute("size", "1")
            DirectCast(rows(i).SelectSingleNode("IBGlyphLink"), XmlElement).SetAttribute("linked_glyph", i & ".root.library.pill")
        Next
        generic.SelectSingleNode("itemflow").InnerText = vbLf & String.Join(vbLf, rows.Select(Function(row) row.GetAttribute("id"))) & vbLf
    End Sub

    Private Shared Function AddLink(parent As XmlElement, id As String, target As String) As XmlElement
        Dim link = parent.OwnerDocument.CreateElement("link")
        link.SetAttribute("id", id) : link.SetAttribute("target", target) : parent.AppendChild(link)
        Return link
    End Function

    Private Shared Function One(document As XmlDocument, xpath As String) As XmlElement
        Dim nodes = document.SelectNodes(xpath)
        If nodes.Count <> 1 Then Throw New IOException("Unsupported Photo Mode menu: " & xpath)
        Return DirectCast(nodes(0), XmlElement)
    End Function
End Class
