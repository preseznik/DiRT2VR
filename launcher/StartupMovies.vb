Imports EgoEngineLibrary.Xml

Public Class StartupMovies
    Public Const OriginalHash As String = "62606A2C6A09F8E9E7AF172141418812E95337672BB31102849C59FD6676D3AD"
    Private ReadOnly context As InstallContext
    Private ReadOnly target As String
    Private ReadOnly backup As String
    Private ReadOnly pendingPath As String
    Public Sub New(value As InstallContext)
        context = value
        target = IO.Path.Combine(context.GameRoot, "system", "states.bin")
        backup = IO.Path.Combine(context.ModRoot, "backups", "startup-movies.original.bin")
        pendingPath = IO.Path.Combine(context.ModRoot, "backups", "startup-movies-pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(pendingPath)
        End Get
    End Property
    Private Sub CheckPaths()
        context.RequireClosed()
        For Each path In {target, backup, pendingPath}
            Files.NoLinks(path)
        Next
    End Sub
    Public Shared Function Patch(original As Byte()) As Byte()
        If Convert.ToHexString(Security.Cryptography.SHA256.HashData(original)) <> OriginalHash Then Throw New IOException("Startup movie skipping requires the original supported system\states.bin.")
        Using input As New MemoryStream(original)
            Dim binary As New XmlFile(input), document = binary.Document
            For Each id In {"sting_video", "intel_sting_video", "amd_sting_video", "ego_sting_video"}
                Dim matches = document.SelectNodes("//StateVideoPreStart[@id='" & id & "']")
                If matches.Count <> 1 Then Throw New IOException("Unsupported startup video definition: " & id)
                Dim replacement = document.CreateElement("StateWayPoint")
                replacement.SetAttribute("id", id) : replacement.SetAttribute("timeDelay", "0.0")
                matches(0).ParentNode.ReplaceChild(replacement, matches(0))
            Next
            Using output As New MemoryStream()
                binary.Write(output, XmlType.BinXml)
                Return output.ToArray()
            End Using
        End Using
    End Function
    Public Sub Prepare(Optional afterJournal As Action = Nothing)
        CheckPaths()
        If Pending Then Throw New IOException("Startup movie recovery is pending.")
        If New DirectMenus(context).Pending Then Throw New IOException("Direct-session menu recovery is pending.")
        If New LanTransaction(context).Pending Then Throw New IOException("Startup logo skipping cannot change game files during a LAN session.")
        Dim original = File.ReadAllBytes(target), modified = Patch(original)
        If File.Exists(backup) Then
            If Files.Hash(backup) <> OriginalHash Then Throw New IOException("The original startup movie backup changed. It was preserved.")
        Else
            Files.AtomicWrite(backup, original)
        End If
        Files.SaveJson(pendingPath, New LanJournal With {.OriginalHash = OriginalHash, .AppliedHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(modified))})
        afterJournal?.Invoke()
        If Files.Hash(target) <> OriginalHash Then Throw New IOException("Startup movie definitions changed during preparation.")
        Files.AtomicWrite(target, modified)
    End Sub
    Public Sub Recover()
        CheckPaths()
        If Not Pending Then Return
        Dim journal = Files.ReadJson(Of LanJournal)(pendingPath)
        If journal Is Nothing OrElse journal.Version <> 1 OrElse journal.OriginalHash <> OriginalHash OrElse journal.AppliedHash Is Nothing OrElse Not System.Text.RegularExpressions.Regex.IsMatch(journal.AppliedHash, "\A[A-Fa-f0-9]{64}\z") Then Throw New IOException("Invalid startup movie recovery journal. Backups were preserved.")
        If Not File.Exists(backup) OrElse Files.Hash(backup) <> OriginalHash Then Throw New IOException("The startup movie backup is missing or changed.")
        Dim current = If(File.Exists(target), Files.Hash(target), "")
        If current <> OriginalHash Then
            If current <> journal.AppliedHash Then Throw New IOException("Startup movie definitions changed outside DiRT2VR. Current file and backup were preserved.")
            Files.AtomicWrite(target, File.ReadAllBytes(backup))
        End If
        File.Delete(pendingPath)
    End Sub
End Class
