Imports System.IO
Imports DiRT2VR

Module LanTests
    Public Sub Run(repo As String, folder As String, check As Action(Of Boolean, String))
        Dim root = IO.Path.Combine(folder, "LAN fixture Ž")
        Dim context As New InstallContext(root, IO.Path.Combine(folder, "LAN preferences"), IO.Path.Combine(folder, "LAN graphics.xml"))
        Directory.CreateDirectory(IO.Path.Combine(context.ModRoot, "payload"))
        Dim payload = IO.Path.Combine(context.ModRoot, "payload", "xlive-lan.dll")
        Dim target = IO.Path.Combine(root, "xlive.dll")
        File.WriteAllText(payload, "test LAN shim") : File.WriteAllText(target, "original offline shim")
        Dim original = Files.Hash(target), applied = Files.Hash(payload)
        Dim manifest As New PackageManifest With {.Files = New Dictionary(Of String, String) From {{"DiRT2VR/payload/xlive-lan.dll", applied}}}
        Files.SaveJson(IO.Path.Combine(context.ModRoot, "package.json"), manifest)
        Dim transaction As New LanTransaction(context)
        For attempt = 1 To 2
            transaction.Prepare()
            check(transaction.Pending AndAlso Files.Hash(target) = applied, "LAN launcher transaction installs payload")
            transaction.Recover()
            check(Not transaction.Pending AndAlso Files.Hash(target) = original, "LAN launcher restores original across repeated sessions")
        Next
        Dim interrupted As Boolean
        Try
            transaction.Prepare(Sub() Throw New IOException("simulated termination after journal"))
        Catch ex As IOException
            interrupted = True
        End Try
        check(interrupted AndAlso transaction.Pending AndAlso Files.Hash(target) = original, "LAN journals before modification")
        transaction.Recover()
        check(Not transaction.Pending AndAlso Files.Hash(target) = original, "LAN recovers partial preparation")
        transaction.Prepare()
        File.WriteAllText(target, "outside change")
        Dim rejected As Boolean
        Try
            transaction.Recover()
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected AndAlso transaction.Pending AndAlso File.ReadAllText(target) = "outside change", "LAN preserves recovery conflicts")
        File.Copy(payload, target, True) : transaction.Recover()
        File.WriteAllText(payload, "corrupt payload") : rejected = False
        Try
            transaction.Prepare()
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected AndAlso Not transaction.Pending AndAlso Files.Hash(target) = original, "LAN rejects corrupt payload before changing original")
        File.WriteAllText(payload, "test LAN shim")
        ' A legacy lab journal is the same schema; upgrading does not strand restoration.
        Dim pending = IO.Path.Combine(context.ModRoot, "lan-backups", "pending.json")
        File.Copy(payload, target, True)
        File.WriteAllText(pending, "{""Version"":1,""OriginalHash"":""" & original & """,""AppliedHash"":""" & applied & """}")
        transaction.Recover()
        check(Not transaction.Pending AndAlso Files.Hash(target) = original, "launcher recovers legacy lab journal")
        For Each relative In {"system/states.bin", "system/flow.bin"}
            Dim destination = IO.Path.Combine(root, relative)
            Directory.CreateDirectory(IO.Path.GetDirectoryName(destination))
            File.Copy(IO.Path.Combine(repo, "artifacts/game", relative), destination)
        Next
        Dim start = LanSession.StartInfo(context, True)
        Session.ConfigureLogging(start, Session.CreateLogFolder(context, False))
        check(start.Environment("DIRT2VR_LOGGING") = "0" AndAlso Not start.Environment.ContainsKey("DIRT2VR_OUTPUT") AndAlso Not Directory.Exists(IO.Path.Combine(context.UserRoot, "logs")), "LAN logging off creates no session folder")
        Dim logFolder = Session.CreateLogFolder(context, True)
        Session.ConfigureLogging(start, logFolder)
        check(start.Environment("DIRT2VR_LOGGING") = "1" AndAlso start.Environment("DIRT2VR_OUTPUT") = logFolder AndAlso Directory.Exists(logFolder), "LAN logging opt-in uses launcher session folder")
        check(start.Environment("DIRT2VR_LAN_HOST") = "1" AndAlso Not start.Environment.ContainsKey("DIRT2VR_LAN_JOIN"), "HOST explicitly advertises launch intent")
            check(start.ArgumentList.Count = 0 AndAlso start.Environment("DIRT2VR_ACTIVE") = "0" AndAlso Not start.Environment.ContainsKey("DIRT2VR_DIRECT_PRACTICE"), "LAN starts native menus without demo or VR")
            check(start.Environment("DIRT2VR_LAN_SKIP_INTRO") = "1" AndAlso start.Environment("DIRT2VR_LAN_SHARED_CAREER") = "1" AndAlso Not start.Environment.ContainsKey("DIRT2VR_LAN_DOCUMENTS"), "LAN uses normal career without a Documents redirect or import")
            check(start.Environment("DIRT2VR_LAN_RECEIPT") = LanSession.ReceiptPath(context) AndAlso Not Directory.Exists(IO.Path.Combine(LanSession.ProfileRoot(context), "Documents")), "LAN readiness stays in AppData and no separate save folder is created")

        Dim config = IO.Path.Combine(LanSession.ProfileRoot(context), "xlln.ini")
        Dim configHash = Files.Hash(config)
        For Each vr In {False, True}
            For Each peer In New String() {Nothing, "192.168.1.25:39000"}
                Dim arguments = LanSession.LaunchArguments(vr, peer)
                check(arguments.Contains(If(vr, "--vr", "--desktop")) AndAlso Not arguments.Contains(If(vr, "--desktop", "--vr")), "LAN choice selects exactly one display mode")
                check(arguments.Contains(If(peer Is Nothing, "--lan-host", "--lan-join")) AndAlso (peer Is Nothing OrElse arguments.Last() = peer), "display choice retains HOST/JOIN target")
            Next
        Next
        Dim vrSettings As New VrSettings With {.LaunchMode = "lan", .SkipIntroduction = True, .Runtime = "test/steamxr_win32.json", .HeadsetScale = 75, .FieldOfView = 80, .ToggleKey = 118, .RecenterKey = 119}
        Dim vrHost = Session.VrStartInfo(context, vrSettings, "Local.TestInput", Nothing)
        Dim vrJoin = Session.VrStartInfo(context, vrSettings, "Local.TestInput", logFolder, "192.168.1.25:39000")
        For Each launch In {vrHost, vrJoin}
            check(launch.Environment("DIRT2VR_ACTIVE") = "1" AndAlso launch.Environment("DIRT2VR_HEADSET") = "1" AndAlso launch.Environment("DIRT2VR_INPUT_CHANNEL") = "Local.TestInput", "LAN VR activates headset and controller channel together")
            check(launch.ArgumentList.Count = 0 AndAlso launch.Environment("DIRT2VR_LAN_CONFIG") = config AndAlso launch.Environment("DIRT2VR_LAN_SHARED_CAREER") = "1", "LAN VR retains native menu startup and shared career")
            check(launch.Environment("DIRT2VR_HEADSET_SCALE") = "0.75" AndAlso launch.Environment("DIRT2VR_FOV_SCALE") = "0.8" AndAlso launch.Environment("DIRT2VR_KEYS") = "118:0,119:0" AndAlso launch.Environment("XR_RUNTIME_JSON") = vrSettings.Runtime, "LAN VR uses saved graphics keys and runtime")
        Next
        check(vrHost.Environment("DIRT2VR_LAN_HOST") = "1" AndAlso vrHost.Environment("DIRT2VR_LOGGING") = "0", "VR HOST supports disabled logging")
        check(vrJoin.Environment("DIRT2VR_LAN_HOST") = "0" AndAlso vrJoin.Environment("DIRT2VR_LAN_JOIN") = "192.168.1.25:39000" AndAlso vrJoin.Environment("DIRT2VR_OUTPUT") = logFolder, "VR JOIN preserves peer and diagnostic folder")
        check(Files.Hash(config) = configHash, "display mode does not replace LAN identity")
        For Each relative In {"dirt2_game.exe", "dirt2.exe", "cars/sti/cameras.xml", "postprocess/effects.xml"}
            Dim destination = IO.Path.Combine(root, relative)
            Directory.CreateDirectory(IO.Path.GetDirectoryName(destination))
            File.Copy(IO.Path.Combine(repo, "artifacts/game", relative), destination)
        Next
        File.WriteAllText(context.GraphicsPath, "<hardware_settings_config><crowd enabled='true'/><particles enabled='true'/><shadows enabled='true'/><postprocess quality='2'/><cpu><threadStrategy parallelUpdateRender='true'/></cpu><dynamic_ambient_occ enabled='true'/><graphics_card><resolution width='1920' height='1080' fullscreen='true' vsync='1'/></graphics_card></hardware_settings_config>")
        Dim originals = {target, context.GraphicsPath, IO.Path.Combine(root, "cars/sti/cameras.xml"), IO.Path.Combine(root, "postprocess/effects.xml"), IO.Path.Combine(root, "system/states.bin")}.ToDictionary(Function(path) path, Function(path) Files.Hash(path))
        For preparedSteps = 1 To 4
            Worker.Run(context, "prepare")
            Dim graphics As New GraphicsTransaction(context)
            If preparedSteps >= 2 Then graphics.Prepare(vrSettings)
            If preparedSteps >= 3 Then Worker.Run(context, "prepare-lan")
            If preparedSteps >= 4 Then Worker.Run(context, "prepare-movies")
            ' Same cleanup as Session.Run after failure at each preparation stage or normal exit.
            graphics.Recover() : Worker.Run(context, "recover")
            check(originals.All(Function(entry) Files.Hash(entry.Key) = entry.Value), "combined VR/LAN preparation restores every original at stage " & preparedSteps)
            check(Not graphics.Pending AndAlso Not transaction.Pending AndAlso Not (New AssetTransaction(context)).Pending AndAlso Not (New StartupMovies(context)).Pending, "combined cleanup clears all transaction journals")
        Next
        Dim joining = LanSession.StartInfo(context, False, "192.168.1.25:39000")
        check(joining.Environment("DIRT2VR_LAN_JOIN") = "192.168.1.25:39000" AndAlso joining.Environment("DIRT2VR_LAN_DISCOVERY") = "1" AndAlso joining.Environment("DIRT2VR_LAN_HOST") = "0", "JOIN passes a validated native endpoint without advertising host intent")
        File.WriteAllText(LanSession.ReceiptPath(context), "stale receipt")
        start = LanSession.StartInfo(context, False)
            check(start.Environment("DIRT2VR_LAN_SKIP_INTRO") = "0" AndAlso Files.Hash(config) = configHash AndAlso Not File.Exists(LanSession.ReceiptPath(context)), "LAN off toggle keeps identity and discards stale readiness receipt")

        File.WriteAllText(IO.Path.Combine(root, "system/flow.bin"), "modified flow") : rejected = False
        Try
            LanSession.StartInfo(context, True)
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected, "LAN skip rejects foreign flow assets")
        Dim settings As New VrSettings With {.LaunchMode = "lan", .SkipIntroduction = True}
        settings.Validate() : Files.SaveJson(context.PreferencesPath, settings)
        check(VrSettings.Load(context).SkipIntroduction AndAlso Not settings.DirectMode AndAlso settings.GridOpponents = 0 AndAlso settings.SessionLaps = 1, "LAN preference is distinct from direct race mode")
        check(Not (New VrSettings()).SkipIntroduction, "launcher intro skip defaults off")
        File.WriteAllText(context.PreferencesPath, "{""Version"":3,""LaunchMode"":""lan"",""LanProfileId"":""obsolete-copy"",""LanProfileInitialized"":true}")
        check(VrSettings.Load(context).LaunchMode = "lan", "old imported-profile preferences are ignored on upgrade")
        Dim empty As New InstallContext(IO.Path.Combine(folder, "LAN no original"), IO.Path.Combine(folder, "LAN preferences"))
        Directory.CreateDirectory(IO.Path.Combine(empty.ModRoot, "payload"))
        File.Copy(payload, IO.Path.Combine(empty.ModRoot, "payload", "xlive-lan.dll"))
        Files.SaveJson(IO.Path.Combine(empty.ModRoot, "package.json"), manifest)
        Dim withoutOriginal As New LanTransaction(empty)
        withoutOriginal.Prepare() : withoutOriginal.Recover()
        check(Not File.Exists(IO.Path.Combine(empty.GameRoot, "xlive.dll")), "LAN restores original absence")
    End Sub
End Module
