Imports System.IO
Imports System.Windows.Forms
Imports DiRT2VR

Module Program
    Private passed As Integer
    Private Sub Check(condition As Boolean, name As String)
        If Not condition Then Throw New Exception(name)
        passed += 1 : Console.WriteLine("PASS " & name)
    End Sub
    Private Sub Reject(action As Action, name As String)
        Dim rejected As Boolean
        Try
            action()
        Catch ex As IOException
            rejected = True
        End Try
        Check(rejected, name)
    End Sub
    <STAThread>
    Sub Main(args As String())
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
        Application.SetColorMode(If(args.Contains("--dark"), SystemColorMode.Dark, SystemColorMode.Classic))
        Application.EnableVisualStyles()
        Dim repo = IO.Path.GetFullPath(If(args.Length > 0, args(0), "."))
        Dim folder = IO.Path.Combine(repo, "artifacts", "launcher-tests-" & DateTime.Now.ToString("yyyyMMdd-HHmmss"))
        Dim root = IO.Path.Combine(folder, "DiRT 2 Ž test")
        Directory.CreateDirectory(root)
        For Each relative In {"dirt2_game.exe", "dirt2.exe", "cars\sti\cameras.xml", "cars\n12\cameras.xml", "postprocess\effects.xml"}
            Dim target = IO.Path.Combine(root, relative)
            Directory.CreateDirectory(IO.Path.GetDirectoryName(target))
            File.Copy(IO.Path.Combine(repo, "artifacts\game", relative), target)
        Next
        Dim graphics = IO.Path.Combine(folder, "Documents\hardware_settings_config.xml")
        Directory.CreateDirectory(IO.Path.GetDirectoryName(graphics))
        File.WriteAllText(graphics, "<hardware_settings_config><crowd enabled='true'/><particles enabled='true'/><shadows enabled='true'/><postprocess quality='2'/><cpu><threadStrategy parallelUpdateRender='true'/></cpu><dynamic_ambient_occ enabled='true'/><graphics_card><resolution width='1920' height='1080' fullscreen='true' vsync='1'/></graphics_card></hardware_settings_config>")
        Dim context As New InstallContext(root, IO.Path.Combine(folder, "user"), graphics)
        context.ValidateGame()
        Check(True, "supported game in Unicode/spaced path")
        Dim bad As New InstallContext(IO.Path.Combine(folder, "missing"))
        Reject(Sub() bad.ValidateGame(), "unsupported game rejected")
        Dim payload = IO.Path.Combine(context.ModRoot, "payload\d3d11.dll")
        Files.AtomicWrite(payload, Text.Encoding.UTF8.GetBytes("test package payload one"))
        Dim manifest As New PackageManifest With {.Version = "test"}
        manifest.Files("DiRT2VR/payload/d3d11.dll") = Files.Hash(payload)
        Files.SaveJson(IO.Path.Combine(context.ModRoot, "package.json"), manifest)
        Dim proxy = IO.Path.Combine(root, "d3d11.dll")
        File.WriteAllText(proxy, "foreign proxy")
        Dim install As New Installation(context)
        Reject(Sub() install.Setup(), "foreign proxy rejected")
        Check(File.ReadAllText(proxy) = "foreign proxy", "foreign bytes preserved")
        File.Delete(proxy)
        install.Setup() : install.Setup()
        Check(Files.Hash(proxy) = Files.Hash(payload), "install and repeat setup")
        Files.AtomicWrite(payload, Text.Encoding.UTF8.GetBytes("test package payload two"))
        manifest.Files("DiRT2VR/payload/d3d11.dll") = Files.Hash(payload)
        Files.SaveJson(IO.Path.Combine(context.ModRoot, "package.json"), manifest)
        install.Setup()
        Check(Files.Hash(proxy) = Files.Hash(payload), "owned proxy upgrade")
        Dim camera = IO.Path.Combine(root, AssetTransaction.Names(0))
        Dim effects = IO.Path.Combine(root, AssetTransaction.Names(1))
        Dim cameraHash = Files.Hash(camera), effectHash = Files.Hash(effects)
        Dim transaction As New AssetTransaction(context)
        transaction.Prepare()
        Check(Files.Hash(camera) <> cameraHash AndAlso Files.Hash(effects) <> effectHash, "both assets patched locally")
        Reject(Sub() transaction.Prepare(), "pending transaction cannot replace originals")
        transaction.Recover()
        Check(Files.Hash(camera) = cameraHash AndAlso Files.Hash(effects) = effectHash, "asset bytes restored exactly")
        Reject(Sub() transaction.Prepare(Sub(index)
                                             If index = 0 Then Throw New IOException("simulated interruption")
                                         End Sub), "partial preparation interrupted")
        transaction.Recover()
        Check(Files.Hash(camera) = cameraHash AndAlso Files.Hash(effects) = effectHash, "partial transaction recovered")
        transaction.Prepare()
        Dim patched = File.ReadAllBytes(camera)
        File.WriteAllText(camera, "external edit")
        Reject(Sub() transaction.Recover(), "external asset edit blocks recovery")
        Check(File.ReadAllText(camera) = "external edit" AndAlso transaction.Pending, "conflict and pending journal preserved")
        Files.AtomicWrite(camera, patched) : transaction.Recover()
        Dim graphicsHash = Files.Hash(graphics)
        Dim gt As New GraphicsTransaction(context)
        gt.Prepare() : Reject(Sub() gt.Prepare(), "graphics originals protected")
        gt.Recover()
        Check(Files.Hash(graphics) = graphicsHash, "graphics bytes restored exactly")
        gt.Prepare()
        Dim document = XmlPatches.Read(File.ReadAllBytes(graphics))
        DirectCast(document.DocumentElement, Xml.XmlElement).SetAttribute("unrelatedTest", "keep")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document))
        gt.Recover()
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Check(document.DocumentElement.GetAttribute("unrelatedTest") = "keep", "unrelated graphics changes retained")
        Check(DirectCast(document.SelectSingleNode("/hardware_settings_config/graphics_card/resolution"), Xml.XmlElement).GetAttribute("width") <> "1600", "VR resolution restored during merge")
        Dim setting As New VrSettings()
        Check(setting.RenderWidth = 1600 AndAlso setting.RenderHeight = 1200 AndAlso setting.HeadsetScale = 50 AndAlso setting.FieldOfView = 100 AndAlso setting.Mirrors = "game", "default graphics preserve baseline")
        Files.AtomicWrite(context.PreferencesPath, Text.Encoding.UTF8.GetBytes("{""Version"":1,""ToggleKey"":118,""RecenterKey"":119,""Bindings"":[]}"))
        Dim migrated = VrSettings.Load(context)
        Check(migrated.Version = 3 AndAlso migrated.ToggleKey = 118 AndAlso migrated.RecenterKey = 119 AndAlso migrated.RenderWidth = 1600 AndAlso migrated.LaunchMode = "menus", "legacy settings migrate without changing keys")
        For Each invalid In {New VrSettings With {.RenderScale = 49}, New VrSettings With {.RenderScale = 151}, New VrSettings With {.HeadsetScale = 24}, New VrSettings With {.HeadsetScale = 101}, New VrSettings With {.FieldOfView = 69}, New VrSettings With {.FieldOfView = 101}, New VrSettings With {.Mirrors = "invalid"}, New VrSettings With {.Version = 4}, New VrSettings With {.LaunchMode = "benchmark"}, New VrSettings With {.CarCode = "..\other"}, New VrSettings With {.TrackId = "999999"}}
            Reject(Sub() invalid.Validate(), "out-of-range graphics/settings rejected")
        Next
        Files.AtomicWrite(context.PreferencesPath, Text.Encoding.UTF8.GetBytes("{""Version"":2,""RenderScale"":75,""FieldOfView"":80}"))
        migrated = VrSettings.Load(context)
        Check(migrated.Version = 3 AndAlso migrated.RenderWidth = 960 AndAlso migrated.LaunchMode = "menus", "graphics preferences survive version 2 migration")
        Dim catalog = RaceCatalog.Current
        Check(catalog.Tracks.Count = 41 AndAlso catalog.Cars.Count = 43 AndAlso catalog.Tracks.Select(Function(t) t.Id).Distinct().Count() = 41 AndAlso catalog.Cars.Select(Function(c) c.Code).Distinct().Count() = 43, "practice catalog has unique route and car IDs")
        Dim game As New InstallContext(IO.Path.Combine(repo, "artifacts/game"))
        For Each car In catalog.Cars
            Dim bytes = File.ReadAllBytes(IO.Path.Combine(game.GameRoot, "cars", car.Code, "cameras.xml"))
            Check(XmlPatches.Asset(bytes, True).Length > 0, "camera preparation accepts " & car.Code & " (not visual acceptance)")
        Next
        For Each route In catalog.Tracks
            catalog.ValidateInstalled(game, route.Id, "sti")
        Next
        Check(True, "catalog routes exist in supported installation")
        Reject(Sub() catalog.ValidateInstalled(context, "127", "sti"), "missing practice track rejected")
        Directory.CreateDirectory(catalog.Track("127").Folder(context))
        Directory.CreateDirectory(catalog.Track("129").Folder(context))
        Dim alternate = IO.Path.Combine(root, "cars/n12/cameras.xml")
        Dim alternateHash = Files.Hash(alternate)
        transaction.Prepare(carCode:="n12", trackId:="127")
        Dim config = IO.Path.Combine(root, transaction.PracticeConfig())
        document = XmlPatches.Read(File.ReadAllBytes(config))
        Check(document.SelectSingleNode("/config/track[@country='baja'][@name='baja_iron'][@route='route_0']/car[@name='n12'][@number='1']") IsNot Nothing AndAlso transaction.PracticeConfig() = "DiRT2VR/p.xml", "selected race config uses the verified short wrapper argument")
        Check(Files.Hash(camera) = cameraHash AndAlso Files.Hash(alternate) <> alternateHash, "selected car patched without modifying Subaru")
        transaction.Recover()
        Check(Files.Hash(alternate) = alternateHash AndAlso Files.Hash(effects) = effectHash AndAlso Not File.Exists(config), "selected car and generated config recovered")
        Reject(Sub() transaction.Prepare(Sub(index)
                                             If index = 0 Then Throw New IOException("interrupted practice")
                                         End Sub, "n12", "127"), "practice partial preparation interrupted")
        config = IO.Path.Combine(root, transaction.PracticeConfig())
        transaction.Recover()
        Check(Files.Hash(alternate) = alternateHash AndAlso Not File.Exists(config), "interrupted practice recovers selected car")
        transaction.Prepare(carCode:="n12", trackId:="127")
        config = IO.Path.Combine(root, transaction.PracticeConfig())
        Dim originalConfig = File.ReadAllBytes(config)
        File.WriteAllText(config, "external change")
        Reject(Sub() transaction.Recover(), "changed practice config preserved")
        Check(File.ReadAllText(config) = "external change" AndAlso transaction.Pending, "practice config conflict remains recoverable")
        Files.AtomicWrite(config, originalConfig) : transaction.Recover()
        File.WriteAllText(config, "foreign config")
        Reject(Sub() transaction.Prepare(carCode:="n12", trackId:="127"), "existing short config cannot be overwritten")
        Check(File.ReadAllText(config) = "foreign config" AndAlso Not transaction.Pending AndAlso Files.Hash(alternate) = alternateHash, "short config conflict preserves originals without pending changes")
        File.Delete(config)
        transaction.Prepare(carCode:="n12", trackId:="127")
        Dim version2Path = IO.Path.Combine(context.ModRoot, "backups/pending.json")
        Dim version2 = Files.ReadJson(Of AssetJournal)(version2Path)
        Dim oldConfig = IO.Path.Combine(context.ModRoot, "backups", version2.Id & ".xml")
        File.Move(config, oldConfig)
        version2.Version = 2 : Files.SaveJson(version2Path, version2)
        Check(transaction.PracticeConfig().EndsWith(version2.Id & ".xml"), "version 2 journal keeps original generated path")
        transaction.Recover()
        Check(Not File.Exists(oldConfig) AndAlso Files.Hash(alternate) = alternateHash, "version 2 practice journal recovers after upgrade")
        transaction.Prepare()
        Dim pending = IO.Path.Combine(context.ModRoot, "backups/pending.json")
        Dim legacy = Files.ReadJson(Of AssetJournal)(pending)
        legacy.Version = 1 : legacy.CarCode = "ignored-legacy-field"
        Files.SaveJson(pending, legacy) : transaction.Recover()
        Check(Files.Hash(camera) = cameraHash, "legacy asset journal always restores Subaru")
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Dim mirror = document.CreateElement("mirrors") : mirror.SetAttribute("enabled", "true")
        document.DocumentElement.AppendChild(mirror)
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document))
        Dim beforeCustom = Files.Hash(graphics)
        Dim custom As New VrSettings With {.RenderScale = 75, .HeadsetScale = 60, .FieldOfView = 80, .Mirrors = "off"}
        gt.Prepare(custom)
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Dim resolution = DirectCast(document.SelectSingleNode("/hardware_settings_config/graphics_card/resolution"), Xml.XmlElement)
        Check(resolution.GetAttribute("width") = "960" AndAlso resolution.GetAttribute("height") = "720", "render scale and crop reduce actual game target")
        Check(DirectCast(document.SelectSingleNode("/hardware_settings_config/mirrors"), Xml.XmlElement).GetAttribute("enabled") = "false", "mirror override applied")
        gt.Recover()
        Check(Files.Hash(graphics) = beforeCustom, "custom graphics restore exact original bytes")
        gt.Prepare(custom)
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        document.DocumentElement.SetAttribute("anotherUnrelatedChange", "preserve")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document)) : gt.Recover()
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Check(document.DocumentElement.GetAttribute("anotherUnrelatedChange") = "preserve" AndAlso DirectCast(document.SelectSingleNode("/hardware_settings_config/mirrors"), Xml.XmlElement).GetAttribute("enabled") = "true", "custom graphics recovery merges unrelated edits")
        setting.Bindings.Add(New ControllerBinding With {.Action = 0, .Source = "xinput", .Device = "0", .Buttons = New List(Of Integer) From {16, 32}})
        setting.Validate()
        setting.Bindings.Add(New ControllerBinding With {.Action = 1, .Source = "xinput", .Device = "0", .Buttons = New List(Of Integer) From {16}})
        Reject(Sub() setting.Validate(), "ambiguous button pair rejected")
        setting.Bindings.RemoveAt(1)
        Dim machine As New BindingMachine(setting.Bindings)
        Dim sample As New ControllerSample With {.Source = "xinput", .Device = "0"}
        Check(Not machine.Update(sample).Any(), "neutral controller armed")
        sample.Buttons.UnionWith({16, 32})
        Check(machine.Update(sample).SequenceEqual({0}), "button pair fires once")
        Check(Not machine.Update(sample).Any(), "held pair does not repeat")
        sample.Buttons.Remove(16) : Check(Not machine.Update(sample).Any(), "partial release does not rearm")
        sample.Buttons.Add(16) : Check(Not machine.Update(sample).Any(), "partial repress does not toggle")
        sample.Connected = False : machine.Update(sample).ToArray()
        sample.Connected = True : Check(Not machine.Update(sample).Any(), "held pair on reconnect is ignored")
        sample.Buttons.Clear() : machine.Update(sample).ToArray()
        sample.Buttons.UnionWith({16, 32}) : Check(machine.Update(sample).SequenceEqual({0}), "release rearms reconnected controller")
        install.RemoveProxy()
        Check(Not File.Exists(proxy) AndAlso Files.Hash(camera) = cameraHash, "remove proxy preserves game assets")
        Check(File.Exists(IO.Path.Combine(root, "dirt2_game.exe")), "game executable survives removal")
        Files.SaveJson(context.PreferencesPath, setting)
        Check(VrSettings.Load(context).Bindings.Count = 1, "settings persist per installation")
        Using input As New ControllerInput()
            input.Poll()
            For Each sampleState In input.Snapshot()
                Console.WriteLine($"INPUT {sampleState.Label} connected={sampleState.Connected}")
            Next
        End Using
        Using form As New MainForm(context)
            form.ShowInTaskbar = False : form.StartPosition = FormStartPosition.Manual : form.Location = New Drawing.Point(-32000, -32000)
            form.Show() : Application.DoEvents()
            Dim tabs = DirectCast(form.Controls.Find("LauncherTabs", True).Single(), TabControl)
            Check(tabs.TabPages.Cast(Of TabPage).Select(Function(page) page.Text).SequenceEqual({"Launcher", "Graphics", "Controls", "Settings"}), "launcher tabs present in order")
            Dim mode = DirectCast(form.Controls.Find("LaunchMode", True).Single(), ComboBox)
            Dim events = DirectCast(form.Controls.Find("PracticeEvent", True).Single(), ComboBox)
            Dim routes = DirectCast(form.Controls.Find("PracticeTrack", True).Single(), ComboBox)
            Dim vehicles = DirectCast(form.Controls.Find("PracticeCar", True).Single(), ComboBox)
            Check(mode.SelectedIndex = 0 AndAlso Not routes.Enabled AndAlso Not vehicles.Enabled, "menu mode keeps practice selectors inactive")
            mode.SelectedIndex = 1 : events.SelectedItem = "Rally"
            vehicles.SelectedItem = vehicles.Items.Cast(Of PracticeCar).Single(Function(c) c.Code = "n12")
            Check(routes.Enabled AndAlso vehicles.Enabled AndAlso routes.Items.Cast(Of PracticeTrack).All(Function(t) t.Event = "Rally") AndAlso DirectCast(routes.SelectedItem, PracticeTrack).Id = "129", "event selection filters installed routes")
            For Each page As TabPage In tabs.TabPages
                tabs.SelectedTab = page : Application.DoEvents()
                Using bitmap As New Drawing.Bitmap(form.Width, form.Height)
                    form.DrawToBitmap(bitmap, New Drawing.Rectangle(0, 0, form.Width, form.Height))
                    bitmap.Save(IO.Path.Combine(folder, "launcher-" & page.Text & ".png"))
                End Using
                Using bitmap As New Drawing.Bitmap(page.Width, page.Height)
                    page.DrawToBitmap(bitmap, New Drawing.Rectangle(0, 0, page.Width, page.Height))
                    ' Compare an unused page pixel with the resolved form palette. A themed
                    ' TabPage used to leave this entire region white in Windows dark mode.
                    Check(bitmap.GetPixel(4, page.Height - 8).ToArgb() = form.BackColor.ToArgb(), page.Text & " page background matches app theme")
                End Using
            Next
            tabs.SelectedIndex = 1
            DirectCast(form.Controls.Find("RenderScale", True).Single(), NumericUpDown).Value = 75
            DirectCast(form.Controls.Find("HeadsetScale", True).Single(), NumericUpDown).Value = 60
            DirectCast(form.Controls.Find("FieldOfView", True).Single(), NumericUpDown).Value = 80
            DirectCast(form.Controls.Find("Mirrors", True).Single(), ComboBox).SelectedIndex = 2
            DirectCast(form.Controls.Find("SaveSettings", True).Single(), Button).PerformClick()
            Dim saved = VrSettings.Load(context)
            Check(saved.RenderWidth = 960 AndAlso saved.RenderHeight = 720 AndAlso saved.HeadsetScale = 60 AndAlso saved.Mirrors = "off", "Graphics tab saves selected values")
            Check(saved.Bindings.Count = 1 AndAlso saved.Bindings(0).Buttons.SequenceEqual({16, 32}), "tab save preserves existing controller pair")
        Check(saved.LaunchMode = "practice" AndAlso saved.TrackId = "129" AndAlso saved.CarCode = "n12", "launcher selection persists for GUI and quick launch")
            form.Close()
        End Using
        ' Exercise the real entry point in a child process, not only MainForm in this harness.
        ' Theme startup used to crash here before any session status could be written.
        Dim app = IO.Path.Combine(repo, "launcher/bin/Release/net10.0-windows/win-x64/DiRT2VR.exe")
        For attempt = 1 To 3
            Dim start As New ProcessStartInfo(app) With {.UseShellExecute = False, .CreateNoWindow = True}
            For Each arg In {"--check-install", "--quiet", "--game", root}
                start.ArgumentList.Add(arg)
            Next
            Using child = Process.Start(start)
                Check(child.WaitForExit(15000) AndAlso child.ExitCode = 0, "real launcher child initializes Windows Forms " & attempt)
            End Using
        Next
        Console.WriteLine($"{passed} checks passed. Artifacts: {folder}")
    End Sub
End Module
