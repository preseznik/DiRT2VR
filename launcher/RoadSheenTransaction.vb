Imports DiRT2VR.CustomTracks

' A single selected layout; large texture packs must not use the small XML
' effect transaction or accumulate all three layouts in memory.
Public Class RoadSheenTransaction
    Private ReadOnly context As InstallContext
    Private ReadOnly folder As String
    Private ReadOnly journalPath As String
    Public Class Journal
        Public Property Version As Integer = 1
        Public Property Id As String
        Public Property Layout As String
        Public Property OriginalHash As String
        Public Property AppliedHash As String
        Public Property ReadOnlyFile As Boolean
    End Class
    Public Sub New(value As InstallContext)
        context = value
        folder = SafeFiles.Inside(context.ModRoot, "road-sheen-session")
        journalPath = SafeFiles.Inside(folder, "pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Private Function Backup(journal As Journal) As String
        Return SafeFiles.Inside(folder, journal.Id & ".original")
    End Function
    Public Sub Prepare(layout As String, percent As Integer, Optional afterWrite As Action = Nothing)
        context.RequireClosed() : Recover()
        Dim target = SafeFiles.Inside(context.GameRoot, Nordschleife.RoadSheen.Target(layout))
        Dim originalHash = SafeFiles.Hash(target)
        Dim modified = Nordschleife.RoadSheen.Patch(context.GameRoot, layout, percent)
        Dim appliedHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(modified))
        If originalHash = appliedHash Then Return
        Dim journal As New Journal With {.Id = Guid.NewGuid().ToString("N"), .Layout = layout, .OriginalHash = originalHash,
            .AppliedHash = appliedHash, .ReadOnlyFile = (File.GetAttributes(target) And FileAttributes.ReadOnly) <> 0}
        If SafeFiles.Hash(target) <> originalHash Then Throw New IOException("Road textures changed during preparation; files preserved.")
        SafeFiles.Atomic(Backup(journal), File.ReadAllBytes(target))
        If SafeFiles.Hash(Backup(journal)) <> originalHash Then Throw New IOException("Road sheen backup verification failed.")
        SafeFiles.WriteJson(journalPath, journal)
        If SafeFiles.Hash(target) <> originalHash Then Throw New IOException("Road textures changed during preparation; backup retained.")
        SafeFiles.Atomic(target, modified)
        If SafeFiles.Hash(target) <> appliedHash Then Throw New IOException("Road sheen preparation verification failed.")
        afterWrite?.Invoke()
    End Sub
    Public Sub Recover()
        context.RequireClosed()
        If Not Pending Then Return
        Dim journal = SafeFiles.ReadJson(Of Journal)(journalPath), id As Guid
        If journal Is Nothing OrElse journal.Version <> 1 OrElse Not Guid.TryParseExact(journal.Id, "N", id) OrElse
            Not Nordschleife.RoadSheen.Supports(journal.Layout) OrElse Not SafeFiles.Digest(journal.OriginalHash) OrElse Not SafeFiles.Digest(journal.AppliedHash) Then
            Throw New IOException("Invalid road sheen recovery journal; files preserved.")
        End If
        Dim original = Backup(journal), target = SafeFiles.Inside(context.GameRoot, Nordschleife.RoadSheen.Target(journal.Layout))
        If New FileInfo(original).Length > Nordschleife.RoadSheen.MaxTextureBytes OrElse SafeFiles.Hash(original) <> journal.OriginalHash Then Throw New IOException("Road sheen backup changed; files preserved.")
        Dim current = If(File.Exists(target), SafeFiles.Hash(target), "")
        If current = journal.AppliedHash OrElse current = "" Then
            SafeFiles.Atomic(target, File.ReadAllBytes(original))
        ElseIf current <> journal.OriginalHash Then
            Throw New IOException("Road sheen recovery preserved external edits: " & target & ". Restore this file before retrying recovery; the backup was retained.")
        End If
        If SafeFiles.Hash(target) <> journal.OriginalHash Then Throw New IOException("Road sheen restoration failed.")
        SafeFiles.SetReadOnly(target, journal.ReadOnlyFile)
        ' Both contents and protection are restored. No need to retain a 267 MB
        ' backup after every successful session. Interrupted cleanup is harmless.
        File.Move(journalPath, SafeFiles.Inside(folder, journal.Id & ".recovered.json"))
        SafeFiles.DeleteOwned(original)
    End Sub
End Class
