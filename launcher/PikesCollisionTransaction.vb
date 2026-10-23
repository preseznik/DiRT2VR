Imports System.Security.Cryptography

' The geometry pool is allocated before the rendering proxy initializes.
' Prepare the approved two-byte capacity change before process creation and
' restore the exact original executable after play or interrupted recovery.
Public Class PikesCollisionTransaction
    Public Const AppliedHash As String = "D8E69AF5BF50B06A770A0724BF3B9E1540DCA22B2FB71ABCB695552C5967A13D"
    Public Const AppliedLaaHash As String = "9BC4DF74BFF66AC7244BC433580ADC7ED28DDCCEEF145BF87F335E8C00E8A39D"
    Private ReadOnly context As InstallContext
    Private ReadOnly target As String, pendingPath As String
    Public Class Journal
        Public Property Version As Integer = 1
        Public Property OriginalHash As String
        Public Property AppliedHash As String
        Public Property ReadOnlyFile As Boolean?
    End Class
    Public Sub New(value As InstallContext)
        context = value
        target = IO.Path.Combine(context.GameRoot, "dirt2_game.exe")
        pendingPath = IO.Path.Combine(context.ModRoot, "backups", "pikes-collision-pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(pendingPath)
        End Get
    End Property
    Private Shared Function Applied(original As String) As String
        If original = InstallContext.SupportedHash Then Return AppliedHash
        If original = LargeAddressAware.AppliedHash Then Return AppliedLaaHash
        Throw New IOException("Pikes Peak requires the supported DiRT 2 executable.")
    End Function
    Private Function Backup(record As Journal) As String
        Return IO.Path.Combine(context.ModRoot, "backups", "pikes-collision-" & record.OriginalHash & ".exe")
    End Function
    Private Function ReadJournal() As Journal
        Files.NoLinks(pendingPath)
        Dim record = Files.ReadJson(Of Journal)(pendingPath)
        If record Is Nothing OrElse record.Version <> 1 OrElse Not LargeAddressAware.Supported(record.OriginalHash) OrElse
           record.AppliedHash <> Applied(record.OriginalHash) OrElse Not record.ReadOnlyFile.HasValue Then Throw New IOException("Invalid Pikes collision recovery journal; files preserved.")
        Files.NoLinks(Backup(record))
        If Not File.Exists(Backup(record)) OrElse Files.Hash(Backup(record)) <> record.OriginalHash Then Throw New IOException("The original Pikes executable backup is missing or changed; files preserved.")
        Return record
    End Function
    Public Shared Function SupportedPending(context As InstallContext, hash As String) As Boolean
        If hash <> AppliedHash AndAlso hash <> AppliedLaaHash Then Return False
        Dim transaction As New PikesCollisionTransaction(context)
        Return transaction.Pending AndAlso transaction.ReadJournal().AppliedHash = hash
    End Function
    Public Shared Function Patch(original As Byte()) As Byte()
        Dim expected = Applied(Convert.ToHexString(SHA256.HashData(original)))
        Dim result = DirectCast(original.Clone(), Byte())
        BitConverter.GetBytes(4096).CopyTo(result, &H2C4BD7)
        If Convert.ToHexString(SHA256.HashData(result)) <> expected Then Throw New IOException("Pikes collision executable check failed; original preserved.")
        Return result
    End Function
    Public Sub Prepare(trackId As String, Optional afterJournal As Action = Nothing)
        context.RequireClosed()
        If Not DepthOfFieldTransaction.Supports(trackId) Then Throw New IOException("Unsupported Pikes collision layout.")
        For Each path In {target, pendingPath}
            Files.NoLinks(path)
        Next
        If Pending Then Throw New IOException("Pikes collision recovery is pending. Restore original files before launching.")
        Dim original = Files.ReadBytes(target), modified = Patch(original)
        Dim record As New Journal With {.OriginalHash = Convert.ToHexString(SHA256.HashData(original)), .AppliedHash = Convert.ToHexString(SHA256.HashData(modified)), .ReadOnlyFile = (File.GetAttributes(target) And FileAttributes.ReadOnly) <> 0}
        Files.NoLinks(Backup(record))
        If File.Exists(Backup(record)) Then
            If Files.Hash(Backup(record)) <> record.OriginalHash Then Throw New IOException("The Pikes executable backup changed; files preserved.")
        Else
            Files.AtomicWrite(Backup(record), original)
        End If
        Files.SaveJson(pendingPath, record)
        afterJournal?.Invoke()
        If Files.Hash(target) <> record.OriginalHash Then Throw New IOException("The executable changed during Pikes preparation; files preserved.")
        Files.AtomicWrite(target, modified)
        If Files.Hash(target) <> record.AppliedHash Then Throw New IOException("Pikes executable preparation verification failed.")
    End Sub
    Public Sub Recover()
        context.RequireClosed() : Files.NoLinks(target)
        If Not Pending Then Return
        Dim record = ReadJournal(), current = If(File.Exists(target), Files.Hash(target), "")
        If current <> record.OriginalHash AndAlso current <> record.AppliedHash Then Throw New IOException("The executable changed outside DiRT2VR; the executable and Pikes backup were preserved.")
        If current = record.AppliedHash Then Files.AtomicWrite(target, Files.ReadBytes(Backup(record)))
        CustomTracks.SafeFiles.SetReadOnly(target, record.ReadOnlyFile.Value)
        CustomTracks.SafeFiles.DeleteOwned(pendingPath)
    End Sub
End Class
