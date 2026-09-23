Imports System.Xml
Imports EgoEngineLibrary.Xml

Public Module XmlPatches
    Public Function Read(bytes As Byte()) As XmlDocument
        Dim document As New XmlDocument With {.PreserveWhitespace = True, .XmlResolver = Nothing}
        Using input As New MemoryStream(bytes), reader = XmlReader.Create(input, New XmlReaderSettings With {.DtdProcessing = DtdProcessing.Prohibit, .XmlResolver = Nothing})
            document.Load(reader)
        End Using
        Return document
    End Function
    Public Function Bytes(document As XmlDocument) As Byte()
        Using output As New MemoryStream()
            document.Save(output)
            Return output.ToArray()
        End Using
    End Function
    Public Function Asset(original As Byte(), camera As Boolean) As Byte()
        Using input As New MemoryStream(original)
            Dim binary As New XmlFile(input)
            Dim document = binary.Document
            If camera Then
                Dim head = document.SelectSingleNode("//View[@ident='head-cam']")
                Dim chase = document.SelectSingleNode("//View[@ident='chase_close']")
                If head Is Nothing OrElse chase Is Nothing Then Throw New IOException("This car has unsupported cockpit camera definitions.")
                Dim replacement = DirectCast(head.CloneNode(True), XmlElement)
                Parameter(replacement, "fov", "120.0")
                For Each item As XmlElement In replacement.SelectNodes("AccelerationBasedShake/Parameter[@type='scalar']")
                    item.SetAttribute("value", "0.0")
                Next
                For Each name In {"maxBodyOffset", "maxHeadOffset", "maxHeadLook"}
                    Parameter(replacement, name, "0.0")
                Next
                Parameter(replacement, "headBuffeting", "false")
                head.ParentNode.ReplaceChild(replacement, head)
                Dim chaseCopy = DirectCast(replacement.CloneNode(True), XmlElement)
                chaseCopy.SetAttribute("ident", "chase_close")
                chase.ParentNode.ReplaceChild(chaseCopy, chase)
            Else
                Dim parameters = document.SelectNodes("//ParameterGroup[@name='MotionBlur']/Param[@name='blurLength']")
                If parameters.Count = 0 Then Throw New IOException("Unsupported motion-blur definitions.")
                For Each item As XmlNode In parameters
                    item.InnerText = "0.0"
                Next
            End If
            Using output As New MemoryStream()
                binary.Write(output, XmlType.BinXml)
                Return output.ToArray()
            End Using
        End Using
    End Function
    Private Sub Parameter(element As XmlElement, name As String, value As String)
        Dim node = TryCast(element.SelectSingleNode("Parameter[@name='" & name & "']"), XmlElement)
        If node Is Nothing Then Throw New IOException("Missing camera parameter: " & name)
        node.SetAttribute("value", value)
    End Sub
End Module

Public Class AssetEntry
    Public Property Index As Integer
    Public Property OriginalHash As String = ""
    Public Property AppliedHash As String = ""
    Public Property Restored As Boolean
End Class
Public Class AssetJournal
    Public Property Version As Integer = 1
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property SettingsJournal As String = ""
    Public Property Entries As New List(Of AssetEntry)
    Public Property CarCode As String = "sti"
    Public Property PracticeConfigHash As String = ""
End Class
Public Class AssetTransaction
    Public Shared ReadOnly Names As String() = {"cars\sti\cameras.xml", "postprocess\effects.xml"}
    Private ReadOnly context As InstallContext
    Private ReadOnly folder As String
    Private ReadOnly journalPath As String
    Public Sub New(value As InstallContext)
        context = value
        folder = IO.Path.Combine(context.ModRoot, "backups")
        journalPath = IO.Path.Combine(folder, "pending.json")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Public Sub Prepare(Optional afterWrite As Action(Of Integer) = Nothing, Optional carCode As String = "sti", Optional trackId As String = Nothing, Optional configOnly As Boolean = False, Optional opponents As Integer = 0, Optional opponentCars As String = "same")
        context.RequireClosed()
        If opponents < 0 OrElse opponents > 7 OrElse (opponents > 0 AndAlso trackId Is Nothing) Then Throw New IOException("Invalid race grid selection.")
        If Pending Then Throw New IOException("Asset recovery is pending.")
        Files.NoLinks(folder)
        RaceCatalog.Current.Car(carCode)
        If configOnly AndAlso trackId Is Nothing Then Throw New IOException("Desktop practice requires a track selection.")
        If trackId IsNot Nothing Then RaceCatalog.Current.ValidateInstalled(context, trackId, carCode)
        Dim configBytes = If(trackId Is Nothing, Nothing, RaceCatalog.Current.Config(trackId, carCode, opponents, opponentCars, context))
        ' Version 4 journals own only a desktop practice config, with no asset entries.
        Dim journal As New AssetJournal With {.Version = If(configOnly, 4, 3), .CarCode = carCode, .SettingsJournal = If(configOnly, "", IO.Path.Combine(context.UserRoot, "graphics-pending.json"))}
        Dim targets = AssetNames(journal)
        Dim replacements As New List(Of Byte())
        For i = 0 To If(configOnly, 0, Names.Length) - 1
            Dim target = IO.Path.Combine(context.GameRoot, targets(i))
            Files.NoLinks(target)
            Dim original = File.ReadAllBytes(target)
            Dim replacement = XmlPatches.Asset(original, i = 0)
            replacements.Add(replacement)
            Dim backup = BackupPath(journal, i)
            If File.Exists(backup) Then Throw New IOException("Backup already exists.")
            Files.AtomicWrite(backup, original)
            journal.Entries.Add(New AssetEntry With {.Index = i, .OriginalHash = Files.Hash(backup), .AppliedHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(replacement))})
        Next
        If trackId IsNot Nothing Then
            Dim config = IO.Path.Combine(context.GameRoot, ConfigRelative(journal))
            Files.NoLinks(config)
            If File.Exists(config) Then Throw New IOException("Practice configuration already exists.")
            Dim bytes = configBytes
            journal.PracticeConfigHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(bytes))
            ' Record ownership before creating the disposable config or modifying any game asset.
            Files.SaveJson(journalPath, journal)
            Files.AtomicWrite(config, bytes)
        Else
            Files.SaveJson(journalPath, journal)
        End If
        For Each entry In journal.Entries
            Dim target = IO.Path.Combine(context.GameRoot, targets(entry.Index))
            If Files.Hash(target) <> entry.OriginalHash Then Throw New IOException("Game asset changed during preparation.")
            Files.AtomicWrite(target, replacements(entry.Index))
            afterWrite?.Invoke(entry.Index)
        Next
    End Sub
    Public Sub Recover()
        context.RequireClosed()
        Files.NoLinks(folder)
        If Not Pending Then Return
        Dim journal = Files.ReadJson(Of AssetJournal)(journalPath)
        Dim parsed As Guid
        If journal Is Nothing OrElse Not {1, 2, 3, 4}.Contains(journal.Version) OrElse Not Guid.TryParseExact(journal.Id, "N", parsed) OrElse journal.Entries Is Nothing Then Throw New IOException("Invalid asset recovery journal. Backups were preserved.")
        Dim expectedEntries = If(journal.Version = 4, 0, Names.Length)
        If journal.Entries.Count <> expectedEntries OrElse journal.Entries.Select(Function(e) e.Index).Distinct().Count() <> expectedEntries OrElse (journal.Version = 4 AndAlso String.IsNullOrEmpty(journal.PracticeConfigHash)) Then Throw New IOException("Invalid asset recovery journal. Backups were preserved.")
        Dim targets = AssetNames(journal)
        If Not String.IsNullOrEmpty(journal.SettingsJournal) AndAlso File.Exists(journal.SettingsJournal) Then Throw New IOException("Graphics recovery must finish under the Windows account that started VR before restoring assets or uninstalling. Open DiRT2VR under that account and choose Restore original files.")
        For Each entry In journal.Entries
            If entry.Index < 0 OrElse entry.Index >= Names.Length Then Throw New IOException("Invalid asset index.")
            Dim target = IO.Path.Combine(context.GameRoot, targets(entry.Index))
            Dim backup = BackupPath(journal, entry.Index)
            Files.NoLinks(target) : Files.NoLinks(backup)
            If Not File.Exists(backup) OrElse Files.Hash(backup) <> entry.OriginalHash Then Throw New IOException("Original backup is missing or changed: " & backup)
            Dim current = If(File.Exists(target), Files.Hash(target), "")
            If current = entry.OriginalHash Then
                entry.Restored = True
            ElseIf current = entry.AppliedHash Then
                Files.AtomicWrite(target, File.ReadAllBytes(backup))
                entry.Restored = True
            Else
                Throw New IOException("Recovery conflict: " & target & Environment.NewLine & "Its contents changed outside DiRT2VR. The current file and original backup were preserved.")
            End If
            Files.SaveJson(journalPath, journal)
        Next
        If journal.Version >= 2 AndAlso journal.PracticeConfigHash <> "" Then
            Dim config = IO.Path.Combine(context.GameRoot, ConfigRelative(journal))
            Files.NoLinks(config)
            If File.Exists(config) Then
                If Files.Hash(config) <> journal.PracticeConfigHash Then Throw New IOException("Practice configuration changed outside DiRT2VR. It and the recovery journal were preserved.")
                File.Delete(config)
            End If
        End If
        File.Move(journalPath, IO.Path.Combine(folder, journal.Id & "-restored.json"))
    End Sub
    Private Shared Function AssetNames(journal As AssetJournal) As String()
        ' Legacy journals always refer to STI, regardless of current preferences.
        Dim code = If(journal.Version = 1, "sti", RaceCatalog.Current.Car(journal.CarCode).Code)
        Return {"cars\" & code & "\cameras.xml", Names(1)}
    End Function
    Private Shared Function ConfigRelative(journal As AssetJournal) As String
        Dim id As Guid
        If Not Guid.TryParseExact(journal.Id, "N", id) Then Throw New IOException("Invalid practice journal ID.")
        ' The installed game wrapper truncates the longer journal-derived argument.
        ' One session owns this fixed short path; preparation refuses any existing file.
        ' Preserve the old location solely for recovery of version 2 journals.
        Return If(journal.Version >= 3, "DiRT2VR/p.xml", "DiRT2VR/backups/" & journal.Id & ".xml")
    End Function
    Public Function PracticeConfig() As String
        Dim journal = Files.ReadJson(Of AssetJournal)(journalPath)
        If Not {2, 3, 4}.Contains(journal.Version) OrElse String.IsNullOrEmpty(journal.PracticeConfigHash) Then Throw New IOException("Practice preparation is incomplete.")
        Dim relative = ConfigRelative(journal)
        Dim filename = IO.Path.Combine(context.GameRoot, relative)
        Files.NoLinks(filename)
        If Files.Hash(filename) <> journal.PracticeConfigHash Then Throw New IOException("Practice configuration is damaged.")
        Return relative
    End Function
    Private Function BackupPath(journal As AssetJournal, index As Integer) As String
        Return IO.Path.Combine(folder, journal.Id & "-" & index.ToString() & ".bin")
    End Function
End Class

Public Class GraphicsChange
    Public Property Element As String = ""
    Public Property Attribute As String = ""
    Public Property Original As String
    Public Property Applied As String = ""
End Class
Public Class GraphicsJournal
    Public Property Version As Integer = 1
    Public Property OriginalHash As String = ""
    Public Property AppliedHash As String = ""
    Public Property Changes As New List(Of GraphicsChange)
End Class
Public Class GraphicsTransaction
    Private ReadOnly context As InstallContext
    Private ReadOnly journalPath As String
    Private ReadOnly backup As String
    Public Sub New(value As InstallContext)
        context = value
        journalPath = IO.Path.Combine(context.UserRoot, "graphics-pending.json")
        backup = IO.Path.Combine(context.UserRoot, "graphics-original.bin")
    End Sub
    Public ReadOnly Property Pending As Boolean
        Get
            Return File.Exists(journalPath)
        End Get
    End Property
    Public Sub Prepare(Optional settings As VrSettings = Nothing)
        context.RequireClosed()
        If Pending Then Throw New IOException("Graphics settings recovery is pending.")
        settings = If(settings, New VrSettings())
        settings.Validate()
        Dim original = File.ReadAllBytes(context.GraphicsPath)
        Dim document = XmlPatches.Read(original)
        Dim journal As New GraphicsJournal()
        Dim specs As New List(Of String) From {"crowd|enabled|false", "particles|enabled|false", "shadows|enabled|false", "postprocess|quality|0", "cpu/threadStrategy|parallelUpdateRender|false", "dynamic_ambient_occ|enabled|false", $"graphics_card/resolution|width|{settings.RenderWidth}", $"graphics_card/resolution|height|{settings.RenderHeight}", "graphics_card/resolution|fullscreen|false", "graphics_card/resolution|vsync|0"}
        If settings.Mirrors <> "game" Then specs.Add("mirrors|enabled|" & If(settings.Mirrors = "on", "true", "false"))
        ' Values from the supported game's hardware_settings_options.xml.
        For Each detail In {("trees", settings.TreeDetail), ("objects", settings.ObjectDetail)}
            If detail.Item2 = 0 Then Continue For
            specs.Add(detail.Item1 & "|lod|" & {"0.5", "0.75", "1.0", "1.25", "1.5"}(detail.Item2 - 1))
            specs.Add(detail.Item1 & "|maxlod|" & If(detail.Item2 <= 2, "1", "0"))
        Next
        For Each spec In specs
            Dim parts = spec.Split("|"c)
            Dim node = TryCast(document.SelectSingleNode("/hardware_settings_config/" & parts(0)), XmlElement)
            If node Is Nothing Then Throw New IOException("Run DiRT 2 normally once to create compatible graphics settings. Missing: " & parts(0))
            journal.Changes.Add(New GraphicsChange With {.Element = parts(0), .Attribute = parts(1), .Original = If(node.HasAttribute(parts(1)), node.GetAttribute(parts(1)), Nothing), .Applied = parts(2)})
            node.SetAttribute(parts(1), parts(2))
        Next
        Dim modified = XmlPatches.Bytes(document)
        journal.OriginalHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(original))
        journal.AppliedHash = Convert.ToHexString(Security.Cryptography.SHA256.HashData(modified))
        Files.AtomicWrite(backup, original)
        Files.SaveJson(journalPath, journal)
        If Files.Hash(context.GraphicsPath) <> journal.OriginalHash Then Throw New IOException("Graphics settings changed during preparation.")
        Files.AtomicWrite(context.GraphicsPath, modified)
    End Sub
    Public Sub Recover()
        context.RequireClosed()
        If Not Pending Then Return
        Dim journal = Files.ReadJson(Of GraphicsJournal)(journalPath)
        If journal Is Nothing OrElse journal.Version <> 1 OrElse Not File.Exists(backup) OrElse Files.Hash(backup) <> journal.OriginalHash Then Throw New IOException("Graphics recovery backup is missing or changed.")
        Dim current = Files.Hash(context.GraphicsPath)
        If current = journal.AppliedHash Then
            Files.AtomicWrite(context.GraphicsPath, File.ReadAllBytes(backup))
        ElseIf current <> journal.OriginalHash Then
            Dim document = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
            For Each change In journal.Changes
                Dim node = TryCast(document.SelectSingleNode("/hardware_settings_config/" & change.Element), XmlElement)
                If node IsNot Nothing AndAlso node.HasAttribute(change.Attribute) AndAlso node.GetAttribute(change.Attribute) = change.Applied Then
                    If change.Original Is Nothing Then node.RemoveAttribute(change.Attribute) Else node.SetAttribute(change.Attribute, change.Original)
                End If
            Next
            Files.AtomicWrite(context.GraphicsPath, XmlPatches.Bytes(document))
        End If
        File.Delete(journalPath)
    End Sub
End Class
