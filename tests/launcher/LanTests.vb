Imports System.IO
Imports DiRT2VR

Module LanTests
    Public Sub Run(repo As String, folder As String, check As Action(Of Boolean, String))
        Dim root = IO.Path.Combine(folder, "LAN fixture Ž")
        Dim context As New InstallContext(root, IO.Path.Combine(folder, "LAN preferences"))
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
            check(start.ArgumentList.Count = 0 AndAlso start.Environment("DIRT2VR_ACTIVE") = "0" AndAlso Not start.Environment.ContainsKey("DIRT2VR_DIRECT_PRACTICE"), "LAN starts native menus without demo or VR")
            check(start.Environment("DIRT2VR_LAN_SKIP_INTRO") = "1" AndAlso start.Environment("DIRT2VR_LAN_SHARED_CAREER") = "1" AndAlso Not start.Environment.ContainsKey("DIRT2VR_LAN_DOCUMENTS"), "LAN uses normal career without a Documents redirect or import")
            check(start.Environment("DIRT2VR_LAN_RECEIPT") = LanSession.ReceiptPath(context) AndAlso Not Directory.Exists(IO.Path.Combine(LanSession.ProfileRoot(context), "Documents")), "LAN readiness stays in AppData and no separate save folder is created")

        Dim config = IO.Path.Combine(LanSession.ProfileRoot(context), "xlln.ini")
        Dim configHash = Files.Hash(config)
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
