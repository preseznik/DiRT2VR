Imports System.IO
Imports DiRT2VR

Public Module VrMemoryTests
    Public Sub Run(folder As String, check As Action(Of Boolean, String))
        Dim graphics = Path.Combine(folder, "msaa-graphics.xml")
        Dim context As New InstallContext(folder, Path.Combine(folder, "msaa-user"), graphics)
        Dim original = "<hardware_settings_config><crowd enabled='true'/><particles enabled='true'/><shadows enabled='true'/><postprocess quality='2'/><cpu><threadStrategy parallelUpdateRender='true'/></cpu><dynamic_ambient_occ enabled='true'/><graphics_card><resolution width='1920' height='1080' fullscreen='true' vsync='1' multisampling='8xmsaa'/></graphics_card></hardware_settings_config>"
        File.WriteAllText(graphics, original)
        Dim transaction As New GraphicsTransaction(context)
        check(New VrSettings().RenderScale = 100, "render recommendation does not change the default")
        For Each dimensions In {(150, 100, 2400, 1800), (151, 100, 2416, 1812), (300, 100, 4800, 3600), (300, 70, 3360, 2520)}
            Dim settings As New VrSettings With {.RenderScale = dimensions.Item1, .FieldOfView = dimensions.Item2}
            settings.Validate() : Files.SaveJson(context.PreferencesPath, settings)
            check(VrSettings.Load(context).RenderScale = dimensions.Item1, "extended render scale survives saved settings")
            transaction.Prepare(settings)
            Dim resolution = XmlPatches.Read(File.ReadAllBytes(graphics)).SelectSingleNode("//resolution")
            check(resolution.Attributes("width").Value = dimensions.Item3.ToString() AndAlso resolution.Attributes("height").Value = dimensions.Item4.ToString(), "extended render scale and FOV reach game resolution")
            transaction.Recover()
            check(File.ReadAllText(graphics) = original, "high render resolution restores original XML")
        Next
        check(System.Text.Json.JsonSerializer.Deserialize(Of VrSettings)("{}").VrMsaa = 2, "existing settings without MSAA use conservative 2x default")
        For Each samples In {0, 2, 4, 8}
            Dim settings As New VrSettings With {.VrMsaa = samples}
            settings.Validate() : Files.SaveJson(context.PreferencesPath, settings)
            check(VrSettings.Load(context).VrMsaa = samples, "MSAA selection survives saved settings")
            transaction.Prepare(settings)
            Dim applied = XmlPatches.Read(File.ReadAllBytes(graphics))
            check(applied.SelectSingleNode("//resolution/@multisampling").Value = If(samples = 0, "off", samples & "xmsaa"), "selected MSAA reaches game XML")
            ' A new manager recovers the durable journal after an interrupted/failed launch.
            Dim recovery As New GraphicsTransaction(context)
            recovery.Recover()
            check(File.ReadAllText(graphics) = original, "MSAA recovery restores exact original 8x XML")
        Next
        Dim invalid As Boolean
        Try
            Dim settings As New VrSettings With {.VrMsaa = 16}
            settings.Validate()
        Catch ex As IOException
            invalid = True
        End Try
        check(invalid, "unsupported MSAA is rejected")
        transaction.Prepare()
        Dim changed = XmlPatches.Read(File.ReadAllBytes(graphics))
        changed.DocumentElement.SetAttribute("unrelated", "keep")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(changed)) : transaction.Recover()
        Dim restored = XmlPatches.Read(File.ReadAllBytes(graphics))
        check(restored.SelectSingleNode("//resolution/@multisampling").Value = "8xmsaa" AndAlso restored.DocumentElement.GetAttribute("unrelated") = "keep", "MSAA recovery preserves unrelated edits")
        transaction.PrepareDesktop(0, 0, True)
        check(XmlPatches.Read(File.ReadAllBytes(graphics)).SelectSingleNode("//resolution/@multisampling").Value = "8xmsaa", "desktop retains original MSAA")
        transaction.Recover()
        File.WriteAllText(graphics, original.Replace(" multisampling='8xmsaa'", ""))
        transaction.Prepare() : transaction.Recover()
        check(XmlPatches.Read(File.ReadAllBytes(graphics)).SelectSingleNode("//resolution/@multisampling") Is Nothing, "originally absent MSAA attribute remains absent after recovery")
    End Sub
End Module
