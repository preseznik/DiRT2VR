Imports System.Security.Cryptography

' Only the known executable and its one-bit LAA variant are accepted. No code,
' checksum, signature or other header fields are rewritten.
Public Class LargeAddressAware
    Public Const AppliedHash As String = "274A798EF4DC609C24690434616E42FFC6D381EB90982BFFA326DC336C58F6B4"
    Private ReadOnly context As InstallContext
    Private ReadOnly target As String, backup As String, pending As String

    Public Class Journal
        Public Property Version As Integer = 1
        Public Property ReadOnlyFile As Boolean?
    End Class

    Public Sub New(value As InstallContext)
        context = value
        target = IO.Path.Combine(context.GameRoot, "dirt2_game.exe")
        backup = IO.Path.Combine(context.ModRoot, "backups", "laa.original.exe")
        pending = IO.Path.Combine(context.ModRoot, "backups", "laa-pending.json")
    End Sub

    Public Shared Function Supported(hash As String) As Boolean
        Return hash = InstallContext.SupportedHash OrElse hash = AppliedHash
    End Function

    Public Shared Function Patch(original As Byte()) As Byte()
        If Convert.ToHexString(SHA256.HashData(original)) <> InstallContext.SupportedHash Then Throw New IOException("LAA requires the supported original DiRT 2 executable.")
        Dim result = DirectCast(original.Clone(), Byte())
        Dim characteristics = BitConverter.ToInt32(result, &H3C) + 22
        result(characteristics) = CByte(result(characteristics) Or &H20)
        If Convert.ToHexString(SHA256.HashData(result)) <> AppliedHash Then Throw New IOException("The LAA executable check failed. The original was preserved.")
        Return result
    End Function

    Private Sub CheckPaths()
        context.RequireClosed()
        For Each path In {target, backup, pending}
            Files.NoLinks(path)
        Next
    End Sub

    Public Sub Prepare(Optional afterJournal As Action = Nothing)
        CheckPaths()
        If Not Environment.Is64BitOperatingSystem Then Throw New IOException("Large Address Aware requires 64-bit Windows.")
        If File.Exists(pending) Then Throw New IOException("LAA recovery is pending. Use Restore original files before launching.")
        Dim current = Files.Hash(target)
        ' An exact external LAA patch is already effective and is not ours to undo.
        If current = AppliedHash Then Return
        Dim original = Files.ReadBytes(target), modified = Patch(original)
        If File.Exists(backup) Then
            If Files.Hash(backup) <> InstallContext.SupportedHash Then Throw New IOException("The LAA backup changed. It was preserved.")
        Else
            Files.AtomicWrite(backup, original)
        End If
        Files.SaveJson(pending, New Journal With {.ReadOnlyFile = (File.GetAttributes(target) And FileAttributes.ReadOnly) <> 0})
        afterJournal?.Invoke()
        If Files.Hash(target) <> InstallContext.SupportedHash Then Throw New IOException("The game executable changed during LAA preparation. Files were preserved.")
        Files.AtomicWrite(target, modified)
    End Sub

    Public Sub Recover()
        CheckPaths()
        If Not File.Exists(pending) Then Return
        Dim record = Files.ReadJson(Of Journal)(pending)
        If record Is Nothing OrElse record.Version <> 1 OrElse Not record.ReadOnlyFile.HasValue Then Throw New IOException("Invalid LAA recovery journal. Files were preserved.")
        If Not File.Exists(backup) OrElse Files.Hash(backup) <> InstallContext.SupportedHash Then Throw New IOException("The LAA backup is missing or changed. Files were preserved.")
        Dim current = If(File.Exists(target), Files.Hash(target), "")
        If Not Supported(current) Then Throw New IOException("The game executable changed outside DiRT2VR. The executable and LAA backup were preserved.")
        If current = AppliedHash Then Files.AtomicWrite(target, Files.ReadBytes(backup))
        CustomTracks.SafeFiles.SetReadOnly(target, record.ReadOnlyFile.Value)
        CustomTracks.SafeFiles.DeleteOwned(pending)
    End Sub
End Class
