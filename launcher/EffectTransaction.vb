Imports System.Globalization
Imports System.Xml
Imports DiRT2VR.CustomTracks
Imports EgoEngineLibrary.Xml

' Shared journal for the ordered filter and bloom effect layers.
Public Class EffectTransaction
    Private Const MaxFiles As Integer = 2048
    Private ReadOnly context As InstallContext
    Private ReadOnly folder As String
    Private ReadOnly journalPath As String
    Private ReadOnly label As String
    Public Class Entry
        Public Property RelativePath As String
        Public Property OriginalHash As String
        Public Property AppliedHash As String
        Public Property ReadOnlyFile As Boolean
    End Class
    Public Class Journal
        Public Property Version As Integer = 1
        Public Property Id As String
        Public Property Entries As Entry()
    End Class
    Public Sub New(value As InstallContext, layer As String)
        If layer <> "bloom" AndAlso layer <> "filter" Then Throw New ArgumentException("Unknown effect layer.")
        label = If(layer = "bloom", "Bloom", "Filter")
        context = value
        folder = SafeFiles.Inside(context.ModRoot, layer & "-session")
        journalPath = SafeFiles.Inside(folder, "pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Private Shared Function Allowed(relative As String) As Boolean
        If String.IsNullOrWhiteSpace(relative) OrElse relative.Contains("\"c) Then Return False
        Dim parts = relative.Split("/"c)
        If parts.Any(Function(p) p = "" OrElse p = "." OrElse p = ".." OrElse p.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0) Then Return False
        Return relative = "frontend/persistentdatarender.xml" OrElse relative = "postprocess/effects.xml" OrElse
            (parts.Length >= 2 AndAlso parts.Length <= 10 AndAlso parts(0) = "tracks" AndAlso {"effects.xml", "night_effects.xml"}.Contains(parts.Last()))
    End Function
    Public Function Targets() As String()
        Dim result As New List(Of String) From {"frontend/persistentdatarender.xml", "postprocess/effects.xml"}
        Dim folders As New Queue(Of String)
        folders.Enqueue("tracks")
        Dim visited As Integer
        While folders.Count > 0
            Dim relative = folders.Dequeue(), path = SafeFiles.Inside(context.GameRoot, relative)
            visited += 1
            If visited > 10000 OrElse relative.Split("/"c).Length > 9 Then Throw New IOException("Track effects exceed the supported search limits; files were preserved.")
            For Each name In {"effects.xml", "night_effects.xml"}
                If File.Exists(SafeFiles.Inside(path, name)) Then result.Add(relative & "/" & name)
            Next
            If result.Count > MaxFiles Then Throw New IOException("Too many track effect files; files were preserved.")
            For Each child In Directory.EnumerateDirectories(path).OrderBy(Function(p) p, StringComparer.OrdinalIgnoreCase)
                SafeFiles.NoLinks(child)
                folders.Enqueue(relative & "/" & IO.Path.GetFileName(child))
            Next
        End While
        If Not result.Contains("tracks/effects.xml") Then Throw New IOException("The game's base track effects are missing; " & label & " could not be changed.")
        Return result.ToArray()
    End Function
    Public Sub Prepare(transform As Func(Of String, Byte(), Byte()), Optional afterWrite As Action(Of Integer) = Nothing)
        context.RequireClosed() : Recover()
        Dim journal As New Journal With {.Id = Guid.NewGuid().ToString("N")}
        Dim entries As New List(Of Entry), originals As New List(Of Byte()), patches As New List(Of Byte())
        Dim totalBytes As Long
        ' Validate the complete set before changing any game files.
        For Each relative In Targets()
            Dim target = SafeFiles.Inside(context.GameRoot, relative)
            If New FileInfo(target).Length > 8 * 1024 * 1024 Then Throw New IOException(label & " effect file is too large: " & relative)
            Dim original = File.ReadAllBytes(target), modified = transform(relative, original)
            totalBytes += original.Length + modified.Length
            If totalBytes > 128L * 1024 * 1024 Then Throw New IOException(label & " effect files exceed the supported size; files were preserved.")
            If original.SequenceEqual(modified) Then Continue For
            originals.Add(original) : patches.Add(modified)
            entries.Add(New Entry With {.RelativePath = relative, .OriginalHash = Digest(original), .AppliedHash = Digest(modified), .ReadOnlyFile = (File.GetAttributes(target) And FileAttributes.ReadOnly) <> 0})
        Next
        If entries.Count = 0 Then Return
        journal.Entries = entries.ToArray()
        For i = 0 To entries.Count - 1
            If SafeFiles.Hash(SafeFiles.Inside(context.GameRoot, entries(i).RelativePath)) <> entries(i).OriginalHash Then Throw New IOException("Effects changed during " & label & " preparation; files were preserved.")
            SafeFiles.Atomic(Backup(journal, i), originals(i))
            If SafeFiles.Hash(Backup(journal, i)) <> entries(i).OriginalHash Then Throw New IOException(label & " backup verification failed.")
        Next
        SafeFiles.WriteJson(journalPath, journal)
        For i = 0 To entries.Count - 1
            Dim target = SafeFiles.Inside(context.GameRoot, entries(i).RelativePath)
            If SafeFiles.Hash(target) <> entries(i).OriginalHash Then Throw New IOException("Effects changed during " & label & " preparation; backup retained.")
            SafeFiles.Atomic(target, patches(i))
            If SafeFiles.Hash(target) <> entries(i).AppliedHash Then Throw New IOException(label & " preparation verification failed.")
            afterWrite?.Invoke(i)
        Next
    End Sub
    Public Sub Recover()
        context.RequireClosed()
        If Not Pending Then Return
        SafeFiles.NoLinks(journalPath)
        If New FileInfo(journalPath).Length > 2 * 1024 * 1024 Then Throw New IOException("Invalid " & label & " recovery journal; files preserved.")
        Dim journal = SafeFiles.ReadJson(Of Journal)(journalPath), id As Guid
        If journal Is Nothing OrElse journal.Version <> 1 OrElse Not Guid.TryParseExact(journal.Id, "N", id) OrElse journal.Entries Is Nothing OrElse journal.Entries.Length = 0 OrElse journal.Entries.Length > MaxFiles OrElse
            journal.Entries.Any(Function(e) e Is Nothing OrElse Not Allowed(e.RelativePath) OrElse Not SafeFiles.Digest(e.OriginalHash) OrElse Not SafeFiles.Digest(e.AppliedHash)) Then Throw New IOException("Invalid " & label & " recovery journal; files preserved.")
        If journal.Entries.Select(Function(e) e.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() <> journal.Entries.Length Then Throw New IOException("Duplicate " & label & " recovery targets; files preserved.")
        For i = 0 To journal.Entries.Length - 1
            SafeFiles.NoLinks(SafeFiles.Inside(context.GameRoot, journal.Entries(i).RelativePath))
            If SafeFiles.Hash(Backup(journal, i)) <> journal.Entries(i).OriginalHash Then Throw New IOException(label & " backup changed; files preserved.")
        Next
        Dim conflicts As New List(Of String)
        For i = 0 To journal.Entries.Length - 1
            Dim entry = journal.Entries(i), target = SafeFiles.Inside(context.GameRoot, entry.RelativePath)
            Dim current = If(File.Exists(target), SafeFiles.Hash(target), "")
            If current = entry.AppliedHash OrElse current = "" Then
                SafeFiles.Atomic(target, File.ReadAllBytes(Backup(journal, i)))
            ElseIf current <> entry.OriginalHash Then
                conflicts.Add(entry.RelativePath) : Continue For
            End If
            If SafeFiles.Hash(target) <> entry.OriginalHash Then Throw New IOException(label & " restoration failed.")
            SafeFiles.SetReadOnly(target, entry.ReadOnlyFile)
        Next
        If conflicts.Count > 0 Then Throw New IOException(label & " recovery preserved external edits: " & String.Join(", ", conflicts) & ". Restore those files before retrying recovery.")
        File.Move(journalPath, SafeFiles.Inside(folder, journal.Id & "/recovered.json"))
    End Sub
    Private Shared Function Digest(bytes As Byte()) As String
        Return Convert.ToHexString(Security.Cryptography.SHA256.HashData(bytes))
    End Function
    Private Function Backup(journal As Journal, index As Integer) As String
        Return SafeFiles.Inside(folder, journal.Id & "/" & index.ToString(CultureInfo.InvariantCulture) & ".original")
    End Function
End Class

