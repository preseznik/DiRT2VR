Imports System.Globalization
Imports System.Xml
Imports DiRT2VR.CustomTracks
Imports EgoEngineLibrary.Xml

' Temporary desktop diagnostics. Owned by the launcher session, never the converter.
Public Class ButtermilkPostProcess
    Private Shared ReadOnly Targets As String() = {"postprocess/effects.xml", "tracks/effects.xml"}
    Private ReadOnly context As InstallContext
    Private ReadOnly folder As String
    Private ReadOnly journalPath As String
    Public Class Entry
        Public Property OriginalHash As String
        Public Property AppliedHash As String
        Public Property ReadOnlyFile As Boolean
    End Class
    Public Class Journal
        Public Property Version As Integer = 1
        Public Property Id As String
        Public Property Profile As String
        Public Property LayoutId As String
        Public Property Entries As Entry()
    End Class
    Public Sub New(value As InstallContext)
        context = value
        folder = SafeFiles.Inside(context.ModRoot, "buttermilk-postprocess")
        journalPath = SafeFiles.Inside(folder, "pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Public Shared Function Supports(layoutId As String) As Boolean
        Return layoutId = "aspen-buttermilk-climb" OrElse layoutId = "aspen-buttermilk-descent"
    End Function
    Public Shared Sub Validate(profile As String, layoutId As String, desktop As Boolean)
        If Not {"normal", "bloom-off", "lower-exposure"}.Contains(profile) Then Throw New IOException("Unknown Buttermilk lighting test. Select Normal in the launcher.")
        If profile <> "normal" AndAlso (Not desktop OrElse Not Supports(layoutId)) Then Throw New IOException("Lighting tests are available only for Buttermilk Climb/Descent in desktop play.")
    End Sub
    Public Shared Function Patch(original As Byte(), profile As String) As Byte()
        Validate(profile, "aspen-buttermilk-climb", True)
        If profile = "normal" Then Return original
        Using input As New MemoryStream(original)
            Dim binary As New XmlFile(input), document = binary.Document
            Dim effects = document.SelectNodes("/PostProcessEffects/Effect[@id='2'][@chain='Type6']")
            If effects.Count <> 1 Then Throw New IOException("Unsupported base post-processing effect; diagnostic was not applied.")
            Dim effect = effects(0)
            Dim group = If(profile = "bloom-off", "BloomFromDsX4", "ToneMap")
            Dim names = If(profile = "bloom-off", New String() {"intensity", "mixMedium", "mixLarge", "mixHuge", "mixStreak"}, New String() {"targetLuminance"})
            For Each name In names
                Dim nodes = effect.SelectNodes("ParameterGroup[@name='" & group & "']/Param[@name='" & name & "']")
                Dim value As Double
                If nodes.Count <> 1 OrElse Not Double.TryParse(nodes(0).InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, value) OrElse Not Double.IsFinite(value) OrElse value < 0 OrElse (name = "targetLuminance" AndAlso value = 0) Then Throw New IOException("Unsupported post-processing parameter: " & name)
                nodes(0).InnerText = If(profile = "bloom-off", "0.000000", (value * 0.5).ToString("R", CultureInfo.InvariantCulture))
            Next
            Using output As New MemoryStream()
                binary.Write(output, XmlType.BinXml)
                Return output.ToArray()
            End Using
        End Using
    End Function
    Public Sub Prepare(profile As String, layoutId As String, Optional afterWrite As Action(Of Integer) = Nothing)
        Validate(profile, layoutId, True)
        context.RequireClosed() : Recover()
        If profile = "normal" Then Return
        Dim layout = TrackPacks.ForLayout(layoutId).GetLayout(layoutId)
        For Each relative In {"effects.xml", "route_0/effects.xml"}
            If File.Exists(SafeFiles.Inside(context.GameRoot, "tracks/usa/" & layout.Folder & "/" & relative)) Then Throw New IOException("This Buttermilk installation has a separate effects override. Restore the normal track build before using the lighting tests.")
        Next
        Dim journal As New Journal With {.Id = Guid.NewGuid().ToString("N"), .Profile = profile, .LayoutId = layoutId, .Entries = New Entry(Targets.Length - 1) {}}
        Dim original As New List(Of Byte()), modified As New List(Of Byte())
        For Each relative In Targets
            Dim target = SafeFiles.Inside(context.GameRoot, relative)
            original.Add(File.ReadAllBytes(target)) : modified.Add(Patch(original.Last(), profile))
        Next
        For i = 0 To Targets.Length - 1
            Dim target = SafeFiles.Inside(context.GameRoot, Targets(i))
            Dim before = Convert.ToHexString(Security.Cryptography.SHA256.HashData(original(i)))
            If SafeFiles.Hash(target) <> before Then Throw New IOException("Post-processing changed during preparation; files preserved.")
            journal.Entries(i) = New Entry With {.OriginalHash = before, .AppliedHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(modified(i))), .ReadOnlyFile = (File.GetAttributes(target) And FileAttributes.ReadOnly) <> 0}
            SafeFiles.Atomic(Backup(journal, i), original(i))
            If SafeFiles.Hash(Backup(journal, i)) <> before Then Throw New IOException("Post-processing backup verification failed.")
        Next
        SafeFiles.WriteJson(journalPath, journal)
        For i = 0 To Targets.Length - 1
            Dim target = SafeFiles.Inside(context.GameRoot, Targets(i))
            If SafeFiles.Hash(target) <> journal.Entries(i).OriginalHash Then Throw New IOException("Post-processing changed during preparation; backup retained.")
            SafeFiles.Atomic(target, modified(i))
            If SafeFiles.Hash(target) <> journal.Entries(i).AppliedHash Then Throw New IOException("Post-processing diagnostic verification failed.")
            afterWrite?.Invoke(i)
        Next
    End Sub
    Public Sub Recover()
        context.RequireClosed()
        If Not Pending Then Return
        SafeFiles.NoLinks(journalPath)
        Dim journal = SafeFiles.ReadJson(Of Journal)(journalPath), id As Guid
        If journal Is Nothing OrElse journal.Version <> 1 OrElse Not Guid.TryParseExact(journal.Id, "N", id) OrElse Not Supports(journal.LayoutId) OrElse Not {"bloom-off", "lower-exposure"}.Contains(journal.Profile) OrElse journal.Entries Is Nothing OrElse journal.Entries.Length <> Targets.Length OrElse journal.Entries.Any(Function(e) e Is Nothing OrElse Not SafeFiles.Digest(e.OriginalHash) OrElse Not SafeFiles.Digest(e.AppliedHash)) Then Throw New IOException("Invalid lighting-test recovery journal; files preserved.")
        For i = 0 To Targets.Length - 1
            If SafeFiles.Hash(Backup(journal, i)) <> journal.Entries(i).OriginalHash Then Throw New IOException("Lighting-test backup changed; files preserved.")
        Next
        Dim conflicts As New List(Of String)
        For i = 0 To Targets.Length - 1
            Dim target = SafeFiles.Inside(context.GameRoot, Targets(i)), entry = journal.Entries(i)
            Dim current = If(File.Exists(target), SafeFiles.Hash(target), "")
            If current = entry.AppliedHash OrElse current = "" Then
                SafeFiles.Atomic(target, File.ReadAllBytes(Backup(journal, i)))
            ElseIf current <> entry.OriginalHash Then
                conflicts.Add(Targets(i)) : Continue For
            End If
            If SafeFiles.Hash(target) <> entry.OriginalHash Then Throw New IOException("Lighting-test restoration failed.")
            SafeFiles.SetReadOnly(target, entry.ReadOnlyFile)
        Next
        If conflicts.Count > 0 Then Throw New IOException("Lighting-test recovery preserved external edits: " & String.Join(", ", conflicts) & ". Restore those files before retrying recovery.")
        File.Move(journalPath, SafeFiles.Inside(folder, journal.Id & "/recovered.json"))
    End Sub
    Private Function Backup(journal As Journal, index As Integer) As String
        Return SafeFiles.Inside(folder, journal.Id & "/" & index.ToString(CultureInfo.InvariantCulture) & ".original")
    End Function
End Class
