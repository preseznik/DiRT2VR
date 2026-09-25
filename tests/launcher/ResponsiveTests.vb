Imports System.Drawing
Imports System.Windows.Forms
Imports DiRT2VR

Public Module ResponsiveTests
    Public Sub Run(context As InstallContext, folder As String, check As Action(Of Boolean, String))
        Using form As New MainForm(context, Function(token) Threading.Tasks.Task.FromResult(Of ReleaseUpdate)(Nothing))
            form.ShowInTaskbar = False : form.Show() : Application.DoEvents()
            form.Location = New Point(-30000, -30000)
            Dim tabs = DirectCast(form.Controls.Find("LauncherTabs", True).Single(), TabControl)
            Dim resolution = DirectCast(form.Controls.Find("RenderScale", True).Single(), ValueSlider)
            resolution.Value = 85
            Dim vsync = DirectCast(form.Controls.Find("DesktopVSync", True).Single(), CheckBox)
            vsync.Checked = False
            Dim server = DirectCast(form.Controls.Find("LanServers", True).Single(), ListView)
            Dim example As New ListViewItem({"Example LAN host", "192.168.1.173:3074", "2 / 8 players", "HOST game running"})
            server.Items.Add(example) : example.Selected = True
            Dim factor = form.DeviceDpi / 96.0
            For Each size In {New Size(1440, 880), New Size(900, 1040), New Size(620, 1200), New Size(560, 540)}
                form.ClientSize = New Size(CInt(size.Width * factor), CInt(size.Height * factor))
                For Each page As TabPage In tabs.TabPages
                    tabs.SelectedTab = page : Application.DoEvents() : form.PerformLayout() : Application.DoEvents()
                    check(Not page.HorizontalScroll.Visible, $"{page.Text} has no horizontal page scrolling at {size}")
                    For Each name In {"SaveSettings", "OpenLogs"}.Concat(If(page.Text = "Multiplayer", Array.Empty(Of String)(), {"LaunchDesktop", "LaunchVR"}))
                        Dim button = form.Controls.Find(name, True).Single()
                        check(form.RectangleToScreen(form.ClientRectangle).Contains(button.RectangleToScreen(button.ClientRectangle)), $"{name} remains reachable on {page.Text} at {size}")
                    Next
                    For Each row In Descendants(page).OfType(Of SettingRow)()
                        check(row.Controls.Cast(Of Control).All(Function(c) c.Left >= 0 AndAlso c.Right <= row.ClientSize.Width + 1), $"{row.Controls(0).Text} fits its field row at {size}")
                    Next
                    Using bitmap As New Bitmap(form.Width, form.Height)
                        form.DrawToBitmap(bitmap, New Rectangle(Point.Empty, form.Size))
                        bitmap.Save(IO.Path.Combine(folder, $"responsive-{page.Text}-{size.Width}x{size.Height}.png"))
                    End Using
                Next
                check(resolution.Value = 85 AndAlso Not vsync.Checked, "resizing preserves unsaved values")
            Next
            tabs.SelectedIndex = 2
            form.ClientSize = New Size(CInt(900 * factor), CInt(1040 * factor)) : Application.DoEvents()
            check(Not tabs.SelectedTab.VerticalScroll.Visible, "widening restores compact Graphics rows without stale vertical spacing")
            ' Resizing itself must not cancel a keyboard capture.
            tabs.SelectedIndex = 3
            Dim key = Descendants(tabs.SelectedTab).OfType(Of Button)().First(Function(b) b.AccessibleName = "Toggle VR keyboard binding")
            key.PerformClick() : form.ClientSize = New Size(CInt(900 * factor), CInt(960 * factor)) : Application.DoEvents()
            Dim capture = GetType(MainForm).GetField("keyboardCapture", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
            check(CInt(capture.GetValue(form)) = 0, "resize keeps the active keyboard capture")
            Using about As New AboutForm(context)
                about.ShowInTaskbar = False : about.StartPosition = FormStartPosition.Manual : about.Location = New Point(-30000, -30000)
                about.Show(form) : about.ShowInstructions("Desktop graphics") : Application.DoEvents()
                Dim helpTabs = DirectCast(about.Controls.Find("HelpTabs", True).Single(), TabControl)
                check(helpTabs.TabPages.Cast(Of TabPage).Select(Function(p) p.Text).SequenceEqual({"About", "Instructions"}), "Help has About and Instructions tabs")
                Dim picker = DirectCast(about.Controls.Find("InstructionTopicPicker", True).Single(), ComboBox)
                For Each width In {900, 560, 900}
                    about.ClientSize = New Size(CInt(width * factor), CInt(620 * factor)) : Application.DoEvents()
                    check(picker.SelectedItem.ToString() = "Desktop graphics", "Instructions retains topic across reflow")
                    check(picker.Visible = (width = 560), "Instructions switches topic navigation with width")
                    Dim close = about.Controls.Find("CloseAbout", True).Single()
                    check(about.RectangleToScreen(about.ClientRectangle).Contains(close.RectangleToScreen(close.ClientRectangle)), "Help Close stays outside scrolling content")
                    Using bitmap As New Bitmap(about.Width, about.Height)
                        about.DrawToBitmap(bitmap, New Rectangle(Point.Empty, about.Size))
                        bitmap.Save(IO.Path.Combine(folder, $"responsive-Instructions-{width}.png"))
                    End Using
                Next
                Dim article = DirectCast(about.Controls.Find("InstructionArticle", True).Single(), RichTextBox)
                check(article.Text.Contains("On by default for desktop") AndAlso article.Text.Contains("independently of borderless"), "Instructions explains separate desktop VSync")
                helpTabs.SelectedIndex = 0
                about.ClientSize = New Size(CInt(560 * factor), CInt(540 * factor)) : Application.DoEvents()
                check(Not helpTabs.SelectedTab.HorizontalScroll.Visible, "About remains readable at compact width")
                Using bitmap As New Bitmap(about.Width, about.Height)
                    about.DrawToBitmap(bitmap, New Rectangle(Point.Empty, about.Size))
                    bitmap.Save(IO.Path.Combine(folder, "responsive-About-560.png"))
                End Using
                about.Close()
            End Using
            form.Close()
        End Using
    End Sub
    Private Iterator Function Descendants(parent As Control) As IEnumerable(Of Control)
        For Each child As Control In parent.Controls
            Yield child
            For Each nested In Descendants(child)
                Yield nested
            Next
        Next
    End Function
End Module
