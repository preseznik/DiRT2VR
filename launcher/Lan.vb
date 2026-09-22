Imports System.Text.RegularExpressions

Public Class LanJournal
    Public Property Version As Integer = 1
    Public Property OriginalHash As String = ""
    Public Property AppliedHash As String = ""
End Class

' Compatible with the lab journal; no paths from the journal are trusted.
Public Class LanTransaction
    Private ReadOnly context As InstallContext
    Private ReadOnly journalPath As String
    Private ReadOnly backup As String
    Private ReadOnly target As String
    Public Sub New(value As InstallContext)
        context = value
        journalPath = IO.Path.Combine(context.ModRoot, "lan-backups", "pending.json")
        backup = IO.Path.Combine(context.ModRoot, "lan-backups", "xlive.original.dll")
        target = IO.Path.Combine(context.GameRoot, "xlive.dll")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Private Sub CheckPaths()
        context.RequireClosed()
        For Each path In {journalPath, backup, target}
            Files.NoLinks(path)
        Next
    End Sub
    Private Shared Function HashOrEmpty(path As String) As String
        Return If(File.Exists(path), Files.Hash(path), "")
    End Function
    Public Sub Prepare(Optional afterJournal As Action = Nothing)
        CheckPaths()
        If Pending Then Throw New IOException("LAN recovery is pending.")
        Const relative As String = "DiRT2VR/payload/xlive-lan.dll"
        Dim payload = IO.Path.Combine(context.GameRoot, relative)
        Files.NoLinks(payload)
        Dim manifest = Files.ReadJson(Of PackageManifest)(IO.Path.Combine(context.ModRoot, "package.json"))
        If manifest Is Nothing OrElse manifest.Files Is Nothing OrElse Not manifest.Files.ContainsKey(relative) OrElse Not File.Exists(payload) Then Throw New IOException("LAN support is missing. Install the complete current launcher package.")
        Dim expected = manifest.Files(relative)
        If expected Is Nothing OrElse Not Regex.IsMatch(expected, "\A[A-Fa-f0-9]{64}\z") OrElse Files.Hash(payload) <> expected Then Throw New IOException("The packaged LAN DLL is damaged.")
        Dim original = HashOrEmpty(target)
        If original = expected Then Throw New IOException("The LAN DLL is installed without its recovery journal. The original cannot be determined.")
        If File.Exists(backup) Then
            If Files.Hash(backup) <> original Then Throw New IOException("An older original xlive.dll backup exists. It was preserved.")
        ElseIf original <> "" Then
            Files.AtomicWrite(backup, File.ReadAllBytes(target))
        End If
        Files.SaveJson(journalPath, New LanJournal With {.OriginalHash = original, .AppliedHash = expected})
        afterJournal?.Invoke()
        If HashOrEmpty(target) <> original Then Throw New IOException("xlive.dll changed during LAN preparation.")
        Files.AtomicWrite(target, File.ReadAllBytes(payload))
    End Sub
    Public Sub Recover()
        CheckPaths()
        If Not Pending Then Return
        Dim journal = Files.ReadJson(Of LanJournal)(journalPath)
        If journal Is Nothing OrElse journal.Version <> 1 OrElse journal.AppliedHash Is Nothing OrElse journal.OriginalHash Is Nothing OrElse
            Not Regex.IsMatch(journal.AppliedHash, "\A[A-Fa-f0-9]{64}\z") OrElse (journal.OriginalHash <> "" AndAlso Not Regex.IsMatch(journal.OriginalHash, "\A[A-Fa-f0-9]{64}\z")) Then Throw New IOException("Invalid LAN recovery journal. Backups were preserved.")
        If journal.OriginalHash <> "" AndAlso HashOrEmpty(backup) <> journal.OriginalHash Then Throw New IOException("The original LAN backup is missing or changed.")
        Dim current = HashOrEmpty(target)
        If current <> journal.OriginalHash Then
            If current <> journal.AppliedHash Then Throw New IOException("xlive.dll changed outside DiRT2VR. Current file and backup were preserved; resolve the LAN recovery conflict first.")
            If journal.OriginalHash = "" Then
                File.Delete(target)
            Else
                Files.AtomicWrite(target, File.ReadAllBytes(backup))
            End If
        End If
        File.Delete(journalPath)
    End Sub
End Class

Public Module LanSession
    Public Function ProfileRoot(context As InstallContext) As String
        Return IO.Path.Combine(context.UserRoot, "lan")
    End Function
    Public Function ReceiptPath(context As InstallContext) As String
        Return IO.Path.Combine(ProfileRoot(context), "profile-ready.txt")
    End Function
    Public Function StartInfo(context As InstallContext, skipIntroduction As Boolean, Optional joinTarget As String = Nothing) As ProcessStartInfo
        context.RequireClosed()
        If joinTarget IsNot Nothing Then joinTarget = LanBrowser.ParseEndpoint(joinTarget).ToString()
        If skipIntroduction Then
            Dim assets As New Dictionary(Of String, String) From {
                {"system/states.bin", "62606A2C6A09F8E9E7AF172141418812E95337672BB31102849C59FD6676D3AD"},
                {"system/flow.bin", "D261EA3394627188AF47DBF8F36F22FDF3B99E70835921B8590D739220DC601F"}}
            For Each pair As KeyValuePair(Of String, String) In assets
                Dim path = IO.Path.Combine(context.GameRoot, pair.Key)
                Files.NoLinks(path)
                If Not File.Exists(path) OrElse Files.Hash(path) <> pair.Value Then Throw New IOException("Skip introduction requires the original supported flow/states files. Turn it off in Settings to use normal onboarding.")
            Next
        End If
        Dim root = ProfileRoot(context), config = IO.Path.Combine(root, "xlln.ini")
        Files.NoLinks(config) : Files.NoLinks(ReceiptPath(context))
        Directory.CreateDirectory(root)
        If Not File.Exists(config) Then
            Dim name = "LAN-" & Guid.NewGuid().ToString("N").Substring(0, 8)
            Dim lines = {"[XLLN-Config-Version:1.6.2.1]", "xlive_username_p1 = " & name, "xlive_user_live_enabled_p1 = 0", "xlive_user_online_enabled_p1 = 0", "xlive_user_auto_login_p1 = 1", "xlive_fps_limit = 0", "xlive_net_disable = 0", "xlive_xhv_engine_enabled = 0", "xlln_debug_log_level = 0x00000000", ""}
            Files.AtomicWrite(config, Text.Encoding.ASCII.GetBytes(String.Join(vbCrLf, lines)))
        End If
        If File.Exists(ReceiptPath(context)) Then File.Delete(ReceiptPath(context))
        Dim start = Session.DesktopStartInfo(context, Nothing, Nothing)
        start.Environment("DIRT2VR_LAN_CONFIG") = config
        start.Environment("DIRT2VR_LAN_SHARED_CAREER") = "1"
        start.Environment("DIRT2VR_LAN_RECEIPT") = ReceiptPath(context)
        start.Environment("DIRT2VR_LAN_SKIP_INTRO") = If(skipIntroduction, "1", "0")
        start.Environment("DIRT2VR_LAN_DISCOVERY") = "1"
        start.Environment("DIRT2VR_LAN_HOST") = If(joinTarget Is Nothing, "1", "0")
        If joinTarget IsNot Nothing Then start.Environment("DIRT2VR_LAN_JOIN") = joinTarget
        Return start
    End Function
End Module
