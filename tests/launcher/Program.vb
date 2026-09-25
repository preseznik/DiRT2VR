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
        If args.Contains("--layout-only") Then
            ResponsiveTests.Run(New InstallContext(IO.Path.Combine(repo, "artifacts/game"), IO.Path.Combine(folder, "layout-user")), folder, AddressOf Check)
            Console.WriteLine(passed & " responsive checks passed. Artifacts: " & folder)
            Return
        End If
        If args.Contains("--controls-only") Then
            DrivingControlTests.Run(repo, folder, AddressOf Check)
            Console.WriteLine(passed & " driving-control checks passed. Artifacts: " & folder)
            Return
        End If
        BorderlessTests.Run(folder, AddressOf Check)
        If args.Contains("--borderless-only") Then
            Console.WriteLine(passed & " borderless checks passed. Artifacts: " & folder)
            Return
        End If
        UpdateTests.Run(folder, AddressOf Check, args.Contains("--live-updates"))
        LanTests.Run(repo, folder, AddressOf Check)
        LanBrowserTests.Run(AddressOf Check)
        StartupMovieTests.Run(repo, folder, AddressOf Check)
        DirectMenuTests.Run(repo, folder, AddressOf Check)
        DrivingControlTests.Run(repo, folder, AddressOf Check)
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
        gt.Prepare(New VrSettings With {.BorderlessDesktop = True})
        Dim vrDisplay = XmlPatches.Read(File.ReadAllBytes(graphics))
        Check(vrDisplay.SelectSingleNode("//resolution/@width").Value = "1600" AndAlso vrDisplay.SelectSingleNode("//resolution/@height").Value = "1200", "desktop borderless preference does not change VR resolution")
        gt.Recover()
        gt.Prepare()
        Dim document = XmlPatches.Read(File.ReadAllBytes(graphics))
        DirectCast(document.DocumentElement, Xml.XmlElement).SetAttribute("unrelatedTest", "keep")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document))
        gt.Recover()
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Check(document.DocumentElement.GetAttribute("unrelatedTest") = "keep", "unrelated graphics changes retained")
        Check(DirectCast(document.SelectSingleNode("/hardware_settings_config/graphics_card/resolution"), Xml.XmlElement).GetAttribute("width") <> "1600", "VR resolution restored during merge")
        Dim setting As New VrSettings()
        Check(setting.HiddenHudElements = 1, "new settings hide gauges and keep other HUD areas visible")
        Check(setting.HudDistance = 1D AndAlso System.Text.Json.JsonSerializer.Deserialize(Of VrSettings)("{}").HudDistance = 1D, "HUD distance defaults to one metre when not previously saved")
        For Each pair In {(-1D, 1D), (100D, 20D), (3.3D, 3.5D)}
            Dim distanceSettings As New VrSettings With {.HudDistance = pair.Item1}
            distanceSettings.Validate()
            Check(distanceSettings.HudDistance = pair.Item2, "HUD distance is bounded and rounded to half-metre steps")
        Next
        Check(System.Text.Json.JsonSerializer.Deserialize(Of VrSettings)("{}").HiddenHudElements = 1, "absent HUD preferences use the new defaults")
        Dim focus As New StartupFocusPolicy(0, 10)
        Check(Not focus.ShouldActivate(New IntPtr(11), New IntPtr(11), 10, 100), "initial game window does not force focus")
        Check(Not focus.ShouldActivate(IntPtr.Zero, New IntPtr(20), 10, 200), "window replacement gap does not activate another app")
        Check(focus.ShouldActivate(New IntPtr(12), New IntPtr(20), 10, 300) AndAlso Not focus.ShouldActivate(New IntPtr(12), New IntPtr(20), 10, 400), "replacement game window gets only one startup handoff")
        focus = New StartupFocusPolicy(0, 10)
        focus.ShouldActivate(New IntPtr(11), New IntPtr(11), 10, 100)
        Check(Not focus.ShouldActivate(New IntPtr(11), New IntPtr(20), 11, 200) AndAlso Not focus.ShouldActivate(New IntPtr(12), New IntPtr(20), 11, 300), "switching away from existing game window cancels focus handoff")
        focus = New StartupFocusPolicy(0, 10)
        focus.ShouldActivate(New IntPtr(11), New IntPtr(11), 11, 100)
        Check(focus.ShouldActivate(New IntPtr(12), New IntPtr(20), 12, 300), "startup input changes do not cancel the window replacement repair")
        focus = New StartupFocusPolicy(0, 10)
        focus.ShouldActivate(New IntPtr(11), New IntPtr(11), 10, 100)
        Check(Not focus.ShouldActivate(New IntPtr(12), New IntPtr(20), 10, 30001), "focus handoff expires after startup")
        focus = New StartupFocusPolicy(0, 10)
        focus.ShouldActivate(New IntPtr(11), New IntPtr(20), 10, 100)
        Check(Not focus.ShouldActivate(New IntPtr(12), New IntPtr(20), 10, 300), "background game that never held focus is left alone")
        Check(Not setting.LoggingEnabled, "diagnostic logging defaults off")
        Check(Session.CreateLogFolder(context, False) Is Nothing AndAlso Not Directory.Exists(IO.Path.Combine(context.UserRoot, "logs")), "disabled logging creates no session log folder")
        Session.SavePreflightReport(context, "display_refresh_hz=90" & Environment.NewLine & "verbose diagnostic data", Nothing)
        Check(Not Directory.Exists(IO.Path.Combine(context.UserRoot, "logs")) AndAlso Files.ReadJson(Of HeadsetStatus)(IO.Path.Combine(context.UserRoot, "headset.json")).RefreshHz = "90", "quiet preflight retains only bounded refresh summary")
        Dim enabledLog = Session.CreateLogFolder(context, True)
        Session.SavePreflightReport(context, "test preflight", enabledLog)
        Check(File.ReadAllText(IO.Path.Combine(enabledLog, "preflight.txt")) = "test preflight", "enabled logging preserves preflight details")
        Dim loggingStart As New ProcessStartInfo()
        Session.ConfigureLogging(loggingStart, enabledLog)
        Check(loggingStart.Environment("DIRT2VR_LOGGING") = "1" AndAlso loggingStart.Environment("DIRT2VR_OUTPUT") = enabledLog, "VR logging setting enables native output")
        Session.ConfigureLogging(loggingStart, Nothing)
        Check(loggingStart.Environment("DIRT2VR_LOGGING") = "0" AndAlso Not loggingStart.Environment.ContainsKey("DIRT2VR_OUTPUT"), "disabled VR logging clears inherited output")
        Check(setting.RenderWidth = 1600 AndAlso setting.RenderHeight = 1200 AndAlso setting.HeadsetScale = 50 AndAlso setting.FieldOfView = 100 AndAlso setting.Mirrors = "game", "default graphics preserve baseline")
        Files.AtomicWrite(context.PreferencesPath, Text.Encoding.UTF8.GetBytes("{""Version"":1,""ToggleKey"":118,""RecenterKey"":119,""Bindings"":[]}"))
        Dim migrated = VrSettings.Load(context)
        Check(Not migrated.LoggingEnabled, "existing preferences migrate with logging off")
        Check(migrated.Version = 3 AndAlso migrated.ToggleKey = 118 AndAlso migrated.RecenterKey = 119 AndAlso migrated.RenderWidth = 1600 AndAlso migrated.LaunchMode = "menus", "legacy settings migrate without changing keys")
        For Each invalid In {New VrSettings With {.RenderScale = 49}, New VrSettings With {.RenderScale = 151}, New VrSettings With {.HeadsetScale = 24}, New VrSettings With {.HeadsetScale = 101}, New VrSettings With {.FieldOfView = 69}, New VrSettings With {.FieldOfView = 101}, New VrSettings With {.Mirrors = "invalid"}, New VrSettings With {.Version = 4}, New VrSettings With {.LaunchMode = "benchmark"}, New VrSettings With {.CarCode = "..\other"}, New VrSettings With {.TrackId = "999999"}}
            Reject(Sub() invalid.Validate(), "out-of-range graphics/settings rejected")
        Next
        Files.AtomicWrite(context.PreferencesPath, Text.Encoding.UTF8.GetBytes("{""Version"":2,""RenderScale"":75,""FieldOfView"":80}"))
        migrated = VrSettings.Load(context)
        Check(migrated.Version = 3 AndAlso migrated.RenderWidth = 960 AndAlso migrated.LaunchMode = "menus", "graphics preferences survive version 2 migration")
        Dim catalog = RaceCatalog.Current
        Check(migrated.OpponentCars = "same", "existing preferences keep matching opponents")
        Reject(Sub() Call (New VrSettings With {.OpponentCars = "unknown"}).Validate(), "unknown opponent selection rejected")
        Reject(Sub() catalog.Config("127", "sti", 7, "mixed"), "mixed grid requires installed cars")
        Check(catalog.Cars.All(Function(c) c.ClassId <> "" AndAlso c.ClassName <> "") AndAlso catalog.Cars.Select(Function(c) c.ClassId).Distinct().Count() = 7, "all cars have one of seven game vehicle classes")
        Dim mixedGrid = XmlPatches.Read(catalog.Config("127", "sti", 7, "mixed", context))
        Check(mixedGrid.SelectNodes("/config/track/car").Count = 8 AndAlso mixedGrid.SelectSingleNode("/config/track/car[1]/@name").Value = "sti" AndAlso mixedGrid.SelectNodes("/config/track/car[@name='n12'][@number='1']").Count = 7, "mixed grid uses only installed models and repeats small pools")
        Dim soloGrid = XmlPatches.Read(catalog.Config("127", "sti", 0, "mixed", context))
        Check(soloGrid.SelectNodes("/config/track/car").Count = 1 AndAlso soloGrid.SelectSingleNode("/config/track/car/@number").Value = "1", "mixed preference leaves practice solo")
        For Each count In {1, 7}
            Dim raceXml As New Xml.XmlDocument()
            raceXml.LoadXml(Text.Encoding.UTF8.GetString(catalog.Config("127", "sti", count)))
            Check(raceXml.SelectSingleNode("/config/track/car").Attributes("number").Value = (count + 1).ToString(), "race grid includes player and requested opponents")
        Next
        Reject(Sub() catalog.Config("127", "sti", 8), "race grid exceeds engine limit")
        Reject(Sub() Call (New VrSettings With {.Opponents = 0}).Validate(), "race needs an opponent")
        Reject(Sub() Call (New VrSettings With {.Opponents = 8}).Validate(), "settings reject oversized grid")
        Check((New VrSettings With {.LaunchMode = "practice", .Opponents = 7}).GridOpponents = 0 AndAlso (New VrSettings With {.LaunchMode = "race", .Opponents = 3}).GridOpponents = 3, "solo mode ignores saved race grid")
        Reject(Sub() Call (New VrSettings With {.Laps = 0}).Validate(), "zero laps rejected")
        Reject(Sub() Call (New VrSettings With {.Laps = 21}).Validate(), "excessive laps rejected")
        Check(catalog.Tracks.Where(Function(t) t.Circuit).Count() = 14 AndAlso catalog.Track("127").Circuit AndAlso Not catalog.Track("129").Circuit, "catalog identifies circuit and point-to-point routes")
        Check((New VrSettings With {.LaunchMode = "practice", .TrackId = "127", .Laps = 3}).SessionLaps = 3 AndAlso (New VrSettings With {.LaunchMode = "race", .TrackId = "127", .Laps = 5}).SessionLaps = 5, "both direct modes use circuit lap choice")
        Check((New VrSettings With {.LaunchMode = "race", .TrackId = "129", .Laps = 5}).SessionLaps = 1, "point-to-point stages ignore saved circuit laps")
        Check(catalog.Tracks.Count = 41 AndAlso catalog.Cars.Count = 43 AndAlso catalog.Tracks.Select(Function(t) t.Id).Distinct().Count() = 41 AndAlso catalog.Cars.Select(Function(c) c.Code).Distinct().Count() = 43, "practice catalog has unique route and car IDs")
        Dim game As New InstallContext(IO.Path.Combine(repo, "artifacts/game"))
        For Each driver In catalog.Cars
            Dim classGrid = XmlPatches.Read(catalog.Config("127", driver.Code, 7, "class", game))
            Dim entries = classGrid.SelectNodes("/config/track/car").Cast(Of Xml.XmlElement).ToArray()
            Check(entries.Length = 8 AndAlso entries(0).GetAttribute("name") = driver.Code AndAlso entries.All(Function(c) c.GetAttribute("number") = "1" AndAlso catalog.Car(c.GetAttribute("name")).ClassId = driver.ClassId), "same-class eight-car grid preserves driver and class for " & driver.Code)
        Next
        mixedGrid = XmlPatches.Read(catalog.Config("127", "sti", 7, "mixed", game))
        Check(mixedGrid.SelectNodes("/config/track/car").Cast(Of Xml.XmlElement).Select(Function(c) c.GetAttribute("name")).Distinct().Count() = 8, "full mixed pool chooses different models without replacement")
        Dim onlyDriver As New RaceCatalog With {.Cars = New List(Of PracticeCar) From {catalog.Car("sti")}, .Tracks = catalog.Tracks}
        Check(XmlPatches.Read(onlyDriver.Config("127", "sti", 7, "class", context)).SelectNodes("/config/track/car[@name='sti']").Count = 8, "single-model class falls back to the driver model")
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
        Dim desktopGraphicsHash = Files.Hash(graphics)
        transaction.Prepare(carCode:="n12", trackId:="127", configOnly:=True, opponents:=7)
        Dim grid As New Xml.XmlDocument()
        grid.Load(IO.Path.Combine(root, transaction.PracticeConfig()))
        Check(grid.SelectSingleNode("/config/track/car").Attributes("number").Value = "8", "desktop transaction preserves selected race grid")
        Dim desktopJournal = Files.ReadJson(Of AssetJournal)(pending)
        Check(desktopJournal.Version = 4 AndAlso desktopJournal.Entries.Count = 0 AndAlso desktopJournal.SettingsJournal = "", "desktop journal owns only the race config")
        Check(Files.Hash(camera) = cameraHash AndAlso Files.Hash(alternate) = alternateHash AndAlso Files.Hash(effects) = effectHash AndAlso Files.Hash(graphics) = desktopGraphicsHash, "desktop preparation leaves cameras effects and graphics unchanged")
        Dim desktopConfig = IO.Path.Combine(root, transaction.PracticeConfig())
        Dim desktopBytes = File.ReadAllBytes(desktopConfig)
        File.WriteAllText(desktopConfig, "outside edit")
        Reject(Sub() transaction.Recover(), "desktop config conflict preserved")
        Files.AtomicWrite(desktopConfig, desktopBytes) : transaction.Recover()
        Check(Not File.Exists(desktopConfig) AndAlso Not transaction.Pending, "desktop config recovery completes")
        Worker.Run(context, "prepare-desktop", "sti", "127", 7, "class")
        grid.Load(IO.Path.Combine(root, transaction.PracticeConfig()))
        Check(grid.SelectNodes("/config/track/car[@name='n12']").Count = 7 AndAlso Files.Hash(alternate) = alternateHash, "worker carries class selection without patching opponent cameras")
        transaction.Recover()
        Check(Not File.Exists(desktopConfig) AndAlso Not transaction.Pending, "class grid configuration is removed by recovery")
        transaction.Prepare(carCode:="sti", trackId:="127", configOnly:=True)
        File.Delete(IO.Path.Combine(root, transaction.PracticeConfig()))
        transaction.Recover()
        Check(Not transaction.Pending, "interrupted config creation/removal recovers")
        Dim inherited = Environment.GetEnvironmentVariable("DIRT2VR_HEADSET")
        Dim inheritedReflections = Environment.GetEnvironmentVariable("DIRT2VR_WATER_REFLECTIONS")
        Try
            Environment.SetEnvironmentVariable("DIRT2VR_HEADSET", "1")
            Environment.SetEnvironmentVariable("DIRT2VR_WATER_REFLECTIONS", "1")
            Dim desktopMenu = Session.DesktopStartInfo(context, Nothing, Nothing)
            Check(desktopMenu.ArgumentList.Count = 0 AndAlso desktopMenu.Environment("DIRT2VR_ACTIVE") = "0" AndAlso Not desktopMenu.Environment.ContainsKey("DIRT2VR_HEADSET"), "regular menu launch disables inherited VR activation")
            Dim desktopRace = Session.DesktopStartInfo(context, "DiRT2VR/p.xml", folder)
            Check(Not desktopMenu.Environment.ContainsKey("DIRT2VR_WATER_REFLECTIONS") AndAlso Not desktopRace.Environment.ContainsKey("DIRT2VR_WATER_REFLECTIONS"), "desktop menus and races clear inherited reflection replay")
            Check(desktopRace.Environment("DIRT2VR_LOGGING") = "1", "desktop direct start supports opt-in logs")
            Dim quietRace = Session.DesktopStartInfo(context, "DiRT2VR/p.xml", Nothing)
            Check(quietRace.Environment("DIRT2VR_LOGGING") = "0" AndAlso Not quietRace.Environment.ContainsKey("DIRT2VR_OUTPUT"), "desktop direct start honors disabled logs")
            Check(desktopRace.ArgumentList.SequenceEqual({"-demo", "DiRT2VR/p.xml"}) AndAlso desktopRace.Environment("DIRT2VR_DESKTOP_PRACTICE") = "1" AndAlso Not desktopRace.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso Not desktopRace.Environment.ContainsKey("DIRT2VR_INPUT_CHANNEL"), "desktop practice enables human control without headset or VR input")
        Finally
            Environment.SetEnvironmentVariable("DIRT2VR_HEADSET", inherited)
            Environment.SetEnvironmentVariable("DIRT2VR_WATER_REFLECTIONS", inheritedReflections)
        End Try
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Dim mirror = document.CreateElement("mirrors") : mirror.SetAttribute("enabled", "true")
        document.DocumentElement.AppendChild(mirror)
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document))
        Dim beforeCustom = Files.Hash(graphics)
        Dim custom As New VrSettings With {.RenderScale = 75, .HeadsetScale = 60, .FieldOfView = 80, .Mirrors = "off"}
        Check(custom.TreeDetail = 0 AndAlso custom.ObjectDetail = 0, "scenery detail defaults preserve the game's settings")
        For Each badDetail In {New VrSettings With {.TreeDetail = -1}, New VrSettings With {.TreeDetail = 6}, New VrSettings With {.ObjectDetail = -1}, New VrSettings With {.ObjectDetail = 6}}
            Reject(Sub() badDetail.Validate(), "out-of-range scenery preset rejected")
        Next
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
        For Each name In {"trees", "objects"}
            Dim detail = document.CreateElement(name) : detail.SetAttribute("lod", "1.0") : detail.SetAttribute("maxlod", "0")
            document.DocumentElement.AppendChild(detail)
        Next
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document))
        Dim beforeDetail = Files.Hash(graphics)
        For level = 1 To 5
            custom.TreeDetail = level : custom.ObjectDetail = 6 - level
            gt.Prepare(custom)
            document = XmlPatches.Read(File.ReadAllBytes(graphics))
            For Each entry In {("trees", level), ("objects", 6 - level)}
                Dim detail = DirectCast(document.SelectSingleNode("/hardware_settings_config/" & entry.Item1), Xml.XmlElement)
                Check(detail.GetAttribute("lod") = {"0.5", "0.75", "1.0", "1.25", "1.5"}(entry.Item2 - 1) AndAlso detail.GetAttribute("maxlod") = If(entry.Item2 <= 2, "1", "0"), "native scenery preset mapping: " & entry.Item1 & entry.Item2)
            Next
            gt.Recover() : Check(Files.Hash(graphics) = beforeDetail, "scenery override restores exact original bytes")
        Next
        custom.TreeDetail = 5 : custom.ObjectDetail = 5 : gt.Prepare(custom)
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        document.DocumentElement.SetAttribute("detailUnrelated", "keep")
        DirectCast(document.SelectSingleNode("/hardware_settings_config/objects"), Xml.XmlElement).SetAttribute("lod", "1.25")
        Files.AtomicWrite(graphics, XmlPatches.Bytes(document)) : gt.Recover()
        document = XmlPatches.Read(File.ReadAllBytes(graphics))
        Check(document.DocumentElement.GetAttribute("detailUnrelated") = "keep" AndAlso DirectCast(document.SelectSingleNode("/hardware_settings_config/trees"), Xml.XmlElement).GetAttribute("lod") = "1.0" AndAlso DirectCast(document.SelectSingleNode("/hardware_settings_config/objects"), Xml.XmlElement).GetAttribute("lod") = "1.25", "scenery recovery preserves unrelated and later user edits")
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
        UpdateTests.Startup(context, folder, AddressOf Check)
        Using form As New MainForm(context, Function(token) Threading.Tasks.Task.FromResult(Of ReleaseUpdate)(Nothing))
            form.ShowInTaskbar = False : form.StartPosition = FormStartPosition.Manual : form.Location = New Drawing.Point(-32000, -32000)
            form.Show() : Application.DoEvents()
            Check(form.Controls.Find("HelpAbout", True).Single().AccessibleName = "Help / About", "help button is discoverable")
            Using about As New AboutForm(context)
                about.StartPosition = FormStartPosition.Manual : about.Location = New Drawing.Point(-32000, -32000)
                about.Show(form) : Application.DoEvents()
                Check(about.Controls.Find("BuildVersion", True).Single().Text.Contains(BuildInfo.Version), "About displays actual assembly version")
                Check(Not about.Controls.Find("InstallUpdate", True).Single().Enabled, "install requires a verified update check")
                Dim closeAbout = about.Controls.Find("CloseAbout", True).Single()
                Check(about.RectangleToScreen(about.ClientRectangle).Contains(closeAbout.RectangleToScreen(closeAbout.ClientRectangle)), "About close button remains visible outside scrolling content")
                Using bitmap As New Drawing.Bitmap(about.Width, about.Height)
                    about.DrawToBitmap(bitmap, New Drawing.Rectangle(0, 0, about.Width, about.Height))
                    bitmap.Save(IO.Path.Combine(folder, "launcher-About.png"))
                End Using
                about.Close()
            End Using
            Dim tabs = DirectCast(form.Controls.Find("LauncherTabs", True).Single(), TabControl)
            Check(tabs.TabPages.Cast(Of TabPage).Select(Function(page) page.Text).SequenceEqual({"Launcher", "Multiplayer", "Graphics", "Controls", "Settings"}), "launcher tabs present in order")
            Check(Screen.FromControl(form).WorkingArea.Contains(form.Bounds), "initial window fits the monitor work area")
            Dim launch = form.Controls.Find("LaunchDesktop", True).Single()
            Dim launchVr = form.Controls.Find("LaunchVR", True).Single()
            Dim save = form.Controls.Find("SaveSettings", True).Single()
            Dim logs = form.Controls.Find("OpenLogs", True).Single()
            Dim leftAt = form.PointToClient(launch.PointToScreen(Drawing.Point.Empty)).X
            Dim rightAt = form.PointToClient(logs.PointToScreen(New Drawing.Point(logs.Width, 0))).X
            Check(leftAt < form.PointToClient(launchVr.PointToScreen(Drawing.Point.Empty)).X AndAlso form.PointToClient(save.PointToScreen(Drawing.Point.Empty)).X > form.PointToClient(launchVr.PointToScreen(New Drawing.Point(launchVr.Width, 0))).X, "launch buttons precede right-side utilities")
            Dim sizeBefore = form.ClientSize
            form.ClientSize = New Drawing.Size(sizeBefore.Width + 500, sizeBefore.Height) : Application.DoEvents()
            Check(form.PointToClient(launch.PointToScreen(Drawing.Point.Empty)).X = leftAt AndAlso form.PointToClient(logs.PointToScreen(New Drawing.Point(logs.Width, 0))).X = rightAt + 500, "footer keeps launch left and utilities right when resized")
            form.ClientSize = sizeBefore : Application.DoEvents()
            Dim mode = DirectCast(form.Controls.Find("LaunchMode", True).Single(), ComboBox)
            Dim events = DirectCast(form.Controls.Find("PracticeEvent", True).Single(), ComboBox)
            Dim routes = DirectCast(form.Controls.Find("PracticeTrack", True).Single(), ComboBox)
            Dim vehicles = DirectCast(form.Controls.Find("PracticeCar", True).Single(), ComboBox)
            Dim opponents = DirectCast(form.Controls.Find("Opponents", True).Single(), ValueSlider)
            Dim laps = DirectCast(form.Controls.Find("Laps", True).Single(), ValueSlider)
            Dim opponentCars = DirectCast(form.Controls.Find("OpponentCars", True).Single(), ComboBox)
            Check(mode.Items(0).ToString() = "Normal Launch" AndAlso Not opponentCars.Enabled, "Normal Launch label and inactive opponent model choice")
            Check(mode.SelectedIndex = 0 AndAlso Not routes.Enabled AndAlso Not vehicles.Enabled, "menu mode keeps practice selectors inactive")
            Check(Not opponents.Enabled, "menus disable opponent choice")
            tabs.SelectedIndex = 1 : Application.DoEvents()
            Check(mode.Items.Count = 3 AndAlso Not launch.Visible AndAlso Not launchVr.Visible, "Multiplayer replaces main-tab LAN mode and hides solo launch buttons")
            Check(form.Controls.Find("HostLAN", True).Single().Enabled AndAlso Not form.Controls.Find("JoinLAN", True).Single().Enabled, "Multiplayer has HOST and requires an available host for JOIN")
            For Each joining In {False, True}
                For Each choiceButton As String In {"ChooseDesktop", "ChooseVR", "CancelLaunch"}
                    Using choice As New LanLaunchForm(joining), click As New System.Windows.Forms.Timer With {.Interval = 30}
                        choice.StartPosition = FormStartPosition.Manual : choice.Location = New Drawing.Point(-32000, -32000)
                        AddHandler click.Tick, Sub()
                                                   click.Stop()
                                                   DirectCast(choice.Controls.Find(choiceButton, True).Single(), Button).PerformClick()
                                               End Sub
                        click.Start()
                        Dim result = choice.ShowDialog(form)
                        Check(result = If(choiceButton = "ChooseVR", DialogResult.Yes, If(choiceButton = "ChooseDesktop", DialogResult.No, DialogResult.Cancel)), "HOST/JOIN dialog returns selected mode or cancels")
                        Check(choice.CancelButton Is choice.Controls.Find("CancelLaunch", True).Single(), "Escape cancels multiplayer launch")
                    End Using
                Next
            Next
            tabs.SelectedIndex = 0 : Application.DoEvents()
            Dim introToggle = DirectCast(form.Controls.Find("SkipIntroduction", True).Single(), CheckBox)
            Check(Not introToggle.Checked, "launcher Skip introduction checkbox defaults off")
            introToggle.Checked = True
            mode.SelectedIndex = 2 : opponents.Value = 3
            Check(opponentCars.Enabled AndAlso opponentCars.Items.Cast(Of String).SequenceEqual({"Same as driver", "Mixed", "Same class"}), "Race offers three opponent model modes")
            opponentCars.SelectedIndex = 2
            laps.Value = 3
            Check(laps.Enabled, "race circuit enables lap choice")
            Check(opponents.Enabled AndAlso routes.Enabled AndAlso vehicles.Enabled, "race enables grid and content choices")
            mode.SelectedIndex = 1 : events.SelectedItem = "Rally"
            Check(Not opponents.Enabled, "solo practice disables opponent choice")
            Check(Not opponentCars.Enabled AndAlso opponentCars.SelectedIndex = 2, "practice preserves but disables opponent models")
            Check(Not laps.Enabled, "point-to-point route disables lap choice")
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
            tabs.SelectedIndex = 2
            Dim logToggle = DirectCast(form.Controls.Find("LoggingEnabled", True).Single(), CheckBox)
            Check(Not logToggle.Checked, "Settings logging checkbox starts off")
            logToggle.Checked = True
            DirectCast(form.Controls.Find("RenderScale", True).Single(), ValueSlider).Value = 75
            DirectCast(form.Controls.Find("HeadsetScale", True).Single(), ValueSlider).Value = 60
            DirectCast(form.Controls.Find("FieldOfView", True).Single(), ValueSlider).Value = 80
            DirectCast(form.Controls.Find("TreeDetail", True).Single(), ValueSlider).Value = 5
            DirectCast(form.Controls.Find("ObjectDetail", True).Single(), ValueSlider).Value = 4
            Check(form.Controls.Find("TreeDetailValue", True).Single().Text = "Ultra" AndAlso form.Controls.Find("ObjectDetailValue", True).Single().Text = "High", "scenery sliders display named presets")
            Dim hudToggle = DirectCast(form.Controls.Find("HudFollowView", True).Single(), CheckBox)
            Dim borderlessToggle = DirectCast(form.Controls.Find("BorderlessDesktop", True).Single(), CheckBox)
            Check(Not borderlessToggle.Checked AndAlso tabs.TabPages(2).Contains(borderlessToggle), "desktop borderless toggle defaults off on Graphics")
            Dim vsyncToggle = DirectCast(form.Controls.Find("DesktopVSync", True).Single(), CheckBox)
            Check(vsyncToggle.Checked, "desktop VSync defaults on in Graphics")
            vsyncToggle.Checked = False
            borderlessToggle.Checked = True
            Check(Not hudToggle.Checked AndAlso Not (New VrSettings()).HudFollowView, "HUD follows view defaults off for existing and new settings")
            Check(tabs.TabPages(2).Contains(hudToggle), "HUD follow control is on Graphics tab")
            hudToggle.Checked = True
            Dim distanceSlider = DirectCast(form.Controls.Find("HudDistance", True).Single(), ValueSlider)
            Check(distanceSlider.Value = 2, "HUD distance slider defaults to one metre")
            distanceSlider.Value = 13
            Check(form.Controls.Find("HudDistanceValue", True).Single().Text = 6.5D.ToString("0.0") & " m", "HUD distance slider displays metres")
            Dim hudNames = {"HudGauges", "HudLapTime", "HudPosition", "HudMap", "HudProgress"}
            For Each name In hudNames
                Dim element = DirectCast(form.Controls.Find(name, True).Single(), CheckBox)
                Check(element.Checked = (name <> "HudGauges") AndAlso tabs.TabPages(2).Contains(element), name & " uses its default below follow-view on Graphics")
                element.Checked = False
            Next
            Dim scaleSlider = DirectCast(form.Controls.Find("RenderScaleSlider", True).Single(), TrackBar)
            Check(scaleSlider.Minimum = 50 AndAlso scaleSlider.Maximum = 150 AndAlso scaleSlider.SmallChange = 1 AndAlso form.Controls.Find("RenderScaleValue", True).Single().Text = "75%", "native graphics slider retains range precision and visible value")
            DirectCast(form.Controls.Find("Mirrors", True).Single(), ComboBox).SelectedIndex = 2
            DirectCast(form.Controls.Find("SaveSettings", True).Single(), Button).PerformClick()
            Dim saved = VrSettings.Load(context)
            Check(Not saved.DesktopVSync, "Graphics tab persists VSync off")
            Check(saved.BorderlessDesktop, "Graphics tab persists borderless preference")
            Check(saved.LoggingEnabled, "Settings logging opt-in persists")
            Check(saved.TreeDetail = 5 AndAlso saved.ObjectDetail = 4, "scenery detail choices persist independently")
            Check(saved.HudFollowView, "HUD follows view setting persists")
            Check(saved.HiddenHudElements = 31, "all five HUD visibility choices persist")
            Dim hudStart = Session.VrStartInfo(context, saved, "Local.TestHud", Nothing)
            Check(saved.HudDistance = 6.5D AndAlso hudStart.Environment("DIRT2VR_HUD_DISTANCE") = "6.5", "HUD distance persists and reaches native runtime with invariant decimal separator")
            Check(Not hudStart.Environment.ContainsKey("DIRT2VR_SKIP_WATER"), "VR launch keeps water visible without legacy partial shader suppression")
            Check(hudStart.Environment("DIRT2VR_WATER_REFLECTIONS") = "1", "VR launch enables validated per-eye reflection path")
            Check(hudStart.Environment("DIRT2VR_HUD_HIDE") = "31", "VR session forwards hidden HUD components")
            saved.HudGauges = True : saved.HudPosition = True : saved.HudProgress = True
            Check(saved.HiddenHudElements = 10, "HUD elements remain independently configurable")
            Check(hudStart.Environment("DIRT2VR_HUD_FOLLOW") = "1", "VR session forwards HUD follow choice")
            saved.HudFollowView = False
            Check(Session.VrStartInfo(context, saved, "Local.TestHud", Nothing).Environment("DIRT2VR_HUD_FOLLOW") = "0", "fixed HUD explicitly overrides inherited follow choice")
            Check(saved.SkipIntroduction, "launcher intro toggle persists across modes")
            Dim startupToggle = DirectCast(form.Controls.Find("SkipStartupMovies", True).Single(), CheckBox)
            Check(Not startupToggle.Checked, "startup toggle defaults off")
            Check(form.Controls.Find("ImportLanCareer", True).Length = 0 AndAlso form.Controls.Find("LanProfile", True).Length = 0, "shared career needs no import or profile selection controls")
            startupToggle.Checked = True
            Check(saved.RenderWidth = 960 AndAlso saved.RenderHeight = 720 AndAlso saved.HeadsetScale = 60 AndAlso saved.Mirrors = "off", "Graphics tab saves selected values")
            Check(saved.Bindings.Count = 1 AndAlso saved.Bindings(0).Buttons.SequenceEqual({16, 32}), "tab save preserves existing controller pair")
            Check(saved.LaunchMode = "practice" AndAlso saved.TrackId = "129" AndAlso saved.CarCode = "n12" AndAlso saved.Opponents = 3, "launcher selection persists for GUI and quick launch")
            mode.SelectedIndex = 2
            DirectCast(form.Controls.Find("SaveSettings", True).Single(), Button).PerformClick()
            saved = VrSettings.Load(context)
            Check(saved.LaunchMode = "race" AndAlso saved.GridOpponents = 3, "race mode and grid persist for both launch buttons")
            Check(saved.OpponentCars = "class", "opponent model choice persists across tabs and launch modes")
            Check(saved.Laps = 3 AndAlso saved.SessionLaps = 1, "saved circuit laps survive point-to-point selection")
            tabs.SelectedIndex = 1
            DirectCast(form.Controls.Find("SaveSettings", True).Single(), Button).PerformClick()
            saved = VrSettings.Load(context)
            Check(saved.LaunchMode = "race" AndAlso saved.SkipIntroduction AndAlso saved.TrackId = "129", "Multiplayer tab preserves solo selection when saving settings")
            Check(saved.SkipStartupMovies, "Settings saves startup movie skip")
            tabs.SelectedIndex = 2
            DirectCast(form.Controls.Find("GraphicsDefaults", True).Single(), Button).PerformClick()
            Check(Not hudToggle.Checked, "Restore graphics defaults returns HUD to fixed placement")
            Check(Not borderlessToggle.Checked, "Restore graphics defaults turns borderless off")
            Check(vsyncToggle.Checked, "Restore graphics defaults turns VSync on")
            Check(distanceSlider.Value = 2, "Restore graphics defaults restores one metre HUD distance")
            Check(hudNames.All(Function(name) DirectCast(form.Controls.Find(name, True).Single(), CheckBox).Checked = (name <> "HudGauges")), "Restore graphics defaults hides only the gauges")
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
