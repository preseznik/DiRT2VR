Imports System.Drawing
Imports System.Windows.Forms
Imports DiRT2VR
Imports System.Runtime.InteropServices

Public Module ResponsiveTests
    <DllImport("user32.dll", EntryPoint:="SendMessageW")>
    Private Function SendMessage(window As IntPtr, message As UInteger, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function
    Public Sub Run(context As InstallContext, folder As String, check As Action(Of Boolean, String))
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "resolution.json"), New ResolutionStatus With {.RequestedWidth = 4800, .RequestedHeight = 3600, .Actual = {1280, 720, 1700, 1734, 1700, 1734}})
        Using form As New MainForm(context, Function(token) Threading.Tasks.Task.FromResult(Of ReleaseUpdate)(Nothing))
            form.ShowInTaskbar = False : form.Show() : Application.DoEvents()
            form.Location = New Point(-30000, -30000)
            Dim tabs = DirectCast(form.Controls.Find("LauncherTabs", True).Single(), TabControl)
            Dim resolution = DirectCast(form.Controls.Find("RenderScale", True).Single(), ValueSlider)
            resolution.Value = 300
            check(form.Controls.Find("RequestedResolution", True).Single().Text.Contains("4800 × 3600"), "requested dimensions update with unsaved slider changes")
            check(form.Controls.Find("ActualResolution", True).Single().Text.Contains("1280 × 720") AndAlso form.Controls.Find("ActualResolution", True).Single().Text.Contains("differs"), "actual dimensions and mismatch are visible independently of requested slider")
            CheckResolutionRefresh(form, context, check)
            Dim renderTrack = DirectCast(form.Controls.Find("RenderScaleSlider", True).Single(), TrackBar)
            Dim recommendation = form.Controls.Find("RenderScaleRecommendation", True).Single()
            check(recommendation.Text = "150% recommended", "render resolution displays recommendation independently of selected value")
            check(SendMessage(renderTrack.Handle, &H403UI, New IntPtr(1), IntPtr.Zero).ToInt32() = -1 AndAlso SendMessage(renderTrack.Handle, &H403UI, IntPtr.Zero, IntPtr.Zero).ToInt32() = 150, "native render slider has a single interior notch at 150")
            GetType(Control).GetMethod("RecreateHandle", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance).Invoke(renderTrack, Nothing)
            check(SendMessage(renderTrack.Handle, &H403UI, IntPtr.Zero, IntPtr.Zero).ToInt32() = 150, "recommendation notch survives native handle recreation")
            Dim msaa = DirectCast(form.Controls.Find("VrMsaa", True).Single(), ValueSlider)
            Dim warning = form.Controls.Find("MsaaWarning", True).Single()
            check(msaa.Value = 1 AndAlso warning.Text = "", "2x MSAA default has no warning")
            msaa.Value = 2
            check(warning.Text.Contains("4×"), "4x MSAA shows a memory warning")
            msaa.Value = 3
            check(warning.Text.Contains("8×") AndAlso warning.Text.Contains("crashes"), "8x MSAA shows stronger warning")
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
                    If page.Text = "Graphics" Then check(SendMessage(renderTrack.Handle, &H403UI, IntPtr.Zero, IntPtr.Zero).ToInt32() = 150, $"recommended notch survives layout at {size}")
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
                check(resolution.Value = 300 AndAlso Not vsync.Checked AndAlso msaa.Value = 3, "resizing preserves unsaved values")
            Next
            tabs.SelectedIndex = 2
            form.ClientSize = New Size(CInt(1440 * factor), CInt(880 * factor)) : Application.DoEvents()
            check(Not tabs.SelectedTab.VerticalScroll.Visible, "widening restores two-column Graphics without stale vertical spacing")
            DirectCast(form.Controls.Find("SaveSettings", True).Single(), Button).PerformClick()
            check(VrSettings.Load(context).VrMsaa = 8, "Graphics slider saves chosen MSAA")
            check(VrSettings.Load(context).RenderScale = 300, "Graphics slider saves 300 percent render resolution")
            msaa.Value = 0 : check(warning.Text = "", "Off clears MSAA warning")
            DirectCast(form.Controls.Find("GraphicsDefaults", True).Single(), Button).PerformClick()
            check(msaa.Value = 1 AndAlso warning.Text = "", "restore defaults resets MSAA to 2x")
            check(resolution.Value = 100, "restore defaults preserves 100 percent rather than selecting recommendation")
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
    Private Sub CheckResolutionRefresh(form As MainForm, context As InstallContext, check As Action(Of Boolean, String))
        Dim label = form.Controls.Find("ActualResolution", True).Single()
        Dim refresh = GetType(MainForm).GetMethod("RefreshResolutionReport", Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
        Dim tabs = DirectCast(form.Controls.Find("LauncherTabs", True).Single(), TabControl)
        tabs.SelectedTab = tabs.TabPages.Cast(Of TabPage)().Single(Function(page) page.Text = "Graphics")
        Application.DoEvents()
        Dim changes As Integer
        Dim changed As EventHandler = Sub(sender, args) changes += 1
        AddHandler label.TextChanged, changed
        Try
            For i = 1 To 10
                refresh.Invoke(form, Nothing)
            Next
            check(changes = 0, "unchanged resolution polling does not reset or redraw the readout")
            Dim bounds = label.Bounds, watch = Stopwatch.StartNew()
            While watch.ElapsedMilliseconds < 2200
                Application.DoEvents()
                Threading.Thread.Sleep(20)
            End While
            check(changes = 0 AndAlso label.Bounds = bounds, "real timer ticks leave visible Graphics text and bounds stable")
            Dim path = IO.Path.Combine(context.UserRoot, "resolution.json")
            Files.SaveJson(path, New ResolutionStatus With {.RequestedWidth = 4800, .RequestedHeight = 3600, .Actual = {4800, 3600, 2400, 2400, 2400, 2400}})
            refresh.Invoke(form, Nothing)
            check(changes = 1 AndAlso label.Text.Contains("Game: 4800 × 3600"), "new resolution is displayed in one text change")
            IO.File.WriteAllText(path, "{")
            refresh.Invoke(form, Nothing)
            check(changes = 2 AndAlso label.Text = "Resolution report unavailable.", "invalid report has one stable fallback")
            refresh.Invoke(form, Nothing)
            check(changes = 2, "unchanged invalid report does not redraw")
            IO.File.Delete(path)
            refresh.Invoke(form, Nothing)
            check(changes = 3 AndAlso label.Text.Contains("not reported yet"), "missing report clears stale measurements once")
            refresh.Invoke(form, Nothing)
            check(changes = 3, "unchanged missing report does not redraw")
            Files.SaveJson(path, New ResolutionStatus With {.RequestedWidth = 4800, .RequestedHeight = 3600, .Actual = {1280, 720, 1700, 1734, 1700, 1734}})
            refresh.Invoke(form, Nothing)
            check(changes = 4 AndAlso label.Text.Contains("differs"), "resolution reporting resumes after a missing or invalid report")
        Finally
            RemoveHandler label.TextChanged, changed
        End Try
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
