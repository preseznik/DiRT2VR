Imports System.Drawing
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports DiRT2VR

Public Module BorderlessTests
    <DllImport("user32.dll")>
    Private Function GetWindowLongPtrW(window As IntPtr, index As Integer) As IntPtr
    End Function
    <DllImport("user32.dll")>
    Private Function GetForegroundWindow() As IntPtr
    End Function
    Private Class ProbeWindow
        Inherits Form
        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property
    End Class
    Public Sub Run(folder As String, check As Action(Of Boolean, String))
        Dim root = IO.Path.Combine(folder, "borderless")
        Dim graphics = IO.Path.Combine(root, "graphics.xml")
        Dim context As New InstallContext(root, IO.Path.Combine(root, "user"), graphics)
        Dim original = System.Text.Encoding.UTF8.GetBytes("<hardware_settings_config><graphics_card><resolution width='1280' height='720' fullscreen='true' vsync='1' multisampling='4xmsaa'><refreshRate rate='120'/></resolution></graphics_card><shadows enabled='true'/><particles enabled='true'/></hardware_settings_config>")
        Files.AtomicWrite(graphics, original)
        Dim transaction As New GraphicsTransaction(context)
        transaction.PrepareDesktop(2560, 1440, False)
        Dim changed = XmlPatches.Read(IO.File.ReadAllBytes(graphics))
        Dim resolution = DirectCast(changed.SelectSingleNode("//resolution"), Xml.XmlElement)
        check(resolution.GetAttribute("width") = "2560" AndAlso resolution.GetAttribute("height") = "1440" AndAlso resolution.GetAttribute("fullscreen") = "false" AndAlso resolution.GetAttribute("vsync") = "0", "borderless sets only the desktop display overrides")
        check(resolution.GetAttribute("multisampling") = "4xmsaa" AndAlso changed.SelectSingleNode("//refreshRate/@rate").Value = "120" AndAlso changed.SelectSingleNode("//shadows/@enabled").Value = "true" AndAlso changed.SelectSingleNode("//particles/@enabled").Value = "true", "desktop keeps effects, antialiasing and refresh settings")
        Dim rejected As Boolean
        Try
            transaction.PrepareDesktop(1920, 1080)
        Catch ex As IO.IOException
            rejected = True
        End Try
        check(rejected, "desktop cannot overwrite a pending graphics backup")
        ' A new manager instance recovers after an interrupted preparation/failed launch.
        Call (New GraphicsTransaction(context)).Recover()
        check(IO.File.ReadAllBytes(graphics).SequenceEqual(original), "interrupted desktop preparation restores exact original bytes")
        transaction.PrepareDesktop(2560, 1440, False)
        changed = XmlPatches.Read(IO.File.ReadAllBytes(graphics))
        DirectCast(changed.SelectSingleNode("//shadows"), Xml.XmlElement).SetAttribute("enabled", "false")
        DirectCast(changed.SelectSingleNode("//resolution"), Xml.XmlElement).SetAttribute("multisampling", "8xmsaa")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(changed))
        transaction.Recover()
        changed = XmlPatches.Read(IO.File.ReadAllBytes(graphics))
        check(changed.SelectSingleNode("//resolution/@width").Value = "1280" AndAlso changed.SelectSingleNode("//resolution/@fullscreen").Value = "true" AndAlso changed.SelectSingleNode("//resolution/@vsync").Value = "1", "desktop display overrides restored during merge")
        check(changed.SelectSingleNode("//shadows/@enabled").Value = "false" AndAlso changed.SelectSingleNode("//resolution/@multisampling").Value = "8xmsaa", "desktop recovery preserves unrelated game setting edits")
        transaction.Recover()
        check(Not transaction.Pending, "desktop repeat recovery is harmless")
        For Each borderless In {False, True}
            For Each vsync In {False, True}
                Files.AtomicWrite(graphics, original)
                transaction.PrepareDesktop(If(borderless, 2560, 0), If(borderless, 1440, 0), vsync)
                changed = XmlPatches.Read(IO.File.ReadAllBytes(graphics))
                resolution = DirectCast(changed.SelectSingleNode("//resolution"), Xml.XmlElement)
                check(resolution.GetAttribute("vsync") = If(vsync, "1", "0"), "desktop VSync " & vsync & " independent of borderless " & borderless)
                check(resolution.GetAttribute("width") = If(borderless, "2560", "1280") AndAlso resolution.GetAttribute("fullscreen") = If(borderless, "false", "true"), "VSync-only override preserves display mode")
                Call (New GraphicsTransaction(context)).Recover()
                check(IO.File.ReadAllBytes(graphics).SequenceEqual(original), "VSync interrupted/failed-launch recovery restores original bytes")
            Next
        Next
        transaction.PrepareDesktop(vsync:=False)
        changed = XmlPatches.Read(IO.File.ReadAllBytes(graphics))
        DirectCast(changed.SelectSingleNode("//resolution"), Xml.XmlElement).SetAttribute("width", "1920")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(changed))
        transaction.Recover()
        changed = XmlPatches.Read(IO.File.ReadAllBytes(graphics))
        check(changed.SelectSingleNode("//resolution/@vsync").Value = "1" AndAlso changed.SelectSingleNode("//resolution/@width").Value = "1920", "VSync-only recovery preserves in-game resolution changes")
        Dim settings = System.Text.Json.JsonSerializer.Deserialize(Of VrSettings)("{""Version"":3}")
        check(Not settings.BorderlessDesktop, "older settings and new installs default borderless off")
        check(settings.DesktopVSync, "older settings and new installs default desktop VSync on")
        settings.BorderlessDesktop = True
        settings.DesktopVSync = False
        Files.SaveJson(context.PreferencesPath, settings)
        check(VrSettings.Load(context).BorderlessDesktop, "desktop borderless preference survives saving")
        check(Not VrSettings.Load(context).DesktopVSync, "explicit VSync off survives saving")

        Dim target As New Rectangle(Screen.PrimaryScreen.Bounds.Right + 200, 50, 640, 480)
        Dim manager As New BorderlessWindow(context, target)
        Dim foreground = GetForegroundWindow()
        Using first As New ProbeWindow With {.StartPosition = FormStartPosition.Manual, .Bounds = New Rectangle(target.X, 60, 400, 300), .ShowInTaskbar = False}, replacement As New ProbeWindow With {.StartPosition = FormStartPosition.Manual, .Bounds = New Rectangle(target.X, 60, 400, 300), .ShowInTaskbar = False}
            first.Show() : Application.DoEvents()
            manager.UpdateWindow(first.Handle, 0) : Application.DoEvents() : manager.UpdateWindow(first.Handle, 250)
            check(first.Bounds = target AndAlso (GetWindowLongPtrW(first.Handle, -16).ToInt64() And &HC40000L) = 0, "native window loses its frame and fills target bounds")
            check((GetWindowLongPtrW(first.Handle, -20).ToInt64() And &H8L) = 0 AndAlso GetForegroundWindow() = foreground, "borderless neither activates nor makes the window topmost")
            first.WindowState = FormWindowState.Minimized : Application.DoEvents()
            manager.UpdateWindow(first.Handle, 500)
            check(first.WindowState = FormWindowState.Minimized, "borderless respects minimization")
            replacement.Show() : Application.DoEvents()
            manager.UpdateWindow(replacement.Handle, 750) : Application.DoEvents() : manager.UpdateWindow(replacement.Handle, 1000)
            check(replacement.Bounds = target AndAlso manager.Warning = "", "replacement startup window receives borderless styling")
            replacement.Bounds = New Rectangle(target.X, 70, 500, 350)
            manager.UpdateWindow(replacement.Handle, 1500)
            check(replacement.Width = 500, "completed window is not continually forced back to fullscreen")
        End Using
        Dim failed As New BorderlessWindow(context, target)
        Using window As New ProbeWindow With {.StartPosition = FormStartPosition.Manual, .Bounds = New Rectangle(target.X, 60, 400, 300), .ShowInTaskbar = False}
            window.Show() : Application.DoEvents()
            Dim initial = window.Bounds
            Dim style = GetWindowLongPtrW(window.Handle, -16)
            failed.UpdateWindow(window.Handle, 0) : Application.DoEvents()
            window.Width = 500 ' Simulate a game refusing the requested bounds.
            failed.UpdateWindow(window.Handle, 6000) : Application.DoEvents()
            check(failed.Warning.Contains("using windowed mode") AndAlso window.Bounds = initial AndAlso GetWindowLongPtrW(window.Handle, -16) = style, "borderless rejection restores original window and reports fallback")
        End Using
    End Sub
End Module
