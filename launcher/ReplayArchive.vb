Imports System.IO.Compression
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Xml
Imports System.Xml.Linq
Imports DiRT2VR.CustomTracks

' The envelope is independent of the native codec. A hash detects corruption;
' it does not make arbitrary native replay data safe to execute.
Public Class ReplayManifest
    Public Property Format As String = "DiRT2VR.Replay"
    Public Property Version As Integer = 1
    Public Property NativeCodec As String = "experimental-native-5"
    Public Property ExecutableSha256 As String = InstallContext.SupportedHash
    Public Property Id As String = Guid.NewGuid().ToString("N")
    Public Property Name As String = ""
    Public Property RecordedUtc As DateTimeOffset = DateTimeOffset.UtcNow
    Public Property DurationSeconds As Double
    Public Property Laps As Integer = 1
    ' Older recordings were always grid races. Never reinterpret them as Automatic.
    Public Property RaceStart As String = "grid"
    Public Property LaunchConfig As String = ""
    Public Property Content As ReplayContentRequirement
    Public Property RecordingBytes As Long
    Public Property RecordingSha256 As String = ""

    Public Function EventConfig() As XDocument
        If Not {"grid", "staggered"}.Contains(RaceStart) OrElse
            (RaceStart = "staggered" AndAlso (Content Is Nothing OrElse Not DirectRaceStart.SupportsStaggered(Content.TrackId) OrElse Laps <> 1)) Then
            Throw New IOException("Invalid saved replay race start format.")
        End If
        If String.IsNullOrEmpty(LaunchConfig) OrElse LaunchConfig.Length > 65536 Then Throw New IOException("Invalid saved replay event.")
        Try
            Using input As New StringReader(LaunchConfig), reader = XmlReader.Create(input, New XmlReaderSettings With {
                .DtdProcessing = DtdProcessing.Prohibit, .XmlResolver = Nothing, .MaxCharactersInDocument = 65536})
                Dim doc = XDocument.Load(reader)
                Dim root = doc.Root
                If root Is Nothing OrElse root.Name <> "config" OrElse root.Elements().Count() <> 1 OrElse
                    root.Attributes().Count() <> 1 OrElse CStr(root.Attribute("skipreplays")) <> "false" Then Throw New IOException("Invalid saved replay event.")
                Dim track = root.Element("track")
                If track Is Nothing OrElse track.Attributes().Count() <> 3 OrElse
                    track.Attributes().Any(Function(a) Not {"country", "name", "route"}.Contains(a.Name.ToString())) OrElse
                    track.Elements().Any(Function(c) c.Name <> "car" OrElse c.HasElements OrElse c.Attributes().Count() <> 2 OrElse
                        c.Attributes().Any(Function(a) Not {"name", "number"}.Contains(a.Name.ToString()))) Then Throw New IOException("Invalid saved replay grid.")
                If Content Is Nothing OrElse Content.Cars Is Nothing OrElse
                    Not ReplayContent.RecordedCars(Content.TrackId, doc, Content).SequenceEqual(Content.Cars, StringComparer.Ordinal) Then Throw New IOException("The saved replay grid does not match its content list.")
                Return doc
            End Using
        Catch ex As XmlException
            Throw New IOException("Invalid saved replay event.", ex)
        End Try
    End Function

    Public Sub Validate()
        Dim parsed As Guid
        If Format <> "DiRT2VR.Replay" OrElse Version <> 1 OrElse Not {"experimental-native-4", "experimental-native-5"}.Contains(NativeCodec) OrElse
            ExecutableSha256 <> InstallContext.SupportedHash Then Throw New IOException("This replay was saved with an unsupported replay format or game version.")
        If Not Guid.TryParseExact(Id, "N", parsed) OrElse Id <> Id.ToLowerInvariant() OrElse
            String.IsNullOrWhiteSpace(Name) OrElse Name.Length > 100 OrElse Name.Any(Function(c) Char.IsControl(c)) OrElse
            Not Double.IsFinite(DurationSeconds) OrElse DurationSeconds <= 0 OrElse DurationSeconds > 86400 OrElse
            Laps < 1 OrElse Laps > 100 OrElse RecordedUtc = DateTimeOffset.MinValue OrElse
            RecordingBytes < 52 OrElse RecordingBytes > ReplayArchive.MaximumRecordingBytes OrElse Not SafeFiles.Digest(RecordingSha256) Then
            Throw New IOException("Invalid saved replay details.")
        End If
        EventConfig()
    End Sub
End Class

Public Class ReplayArchive
    Public Const MaximumRecordingBytes As Long = 2147483648L
    Private Const MaximumManifestBytes As Integer = 2 * 1024 * 1024
    Public Shared ReadOnly Json As New JsonSerializerOptions With {
        .WriteIndented = True, .MaxDepth = 16, .UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow}

    ' Save to a new name; a failed/cancelled write never replaces a good replay.
    Public Shared Function Save(destination As String, recording As String, details As ReplayManifest, Optional token As CancellationToken = Nothing) As ReplayManifest
        Files.NoLinks(destination) : Files.NoLinks(recording)
        Dim manifest = JsonSerializer.Deserialize(Of ReplayManifest)(JsonSerializer.SerializeToUtf8Bytes(details, Json), Json)
        Dim temporary = destination & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Dim failure As Exception = Nothing
        Dim created As Boolean
        Try
            Using source As New FileStream(recording, FileMode.Open, FileAccess.Read, FileShare.Read)
                manifest.RecordingBytes = source.Length
                Dim nativeVersion As UInteger
                manifest.DurationSeconds = NativeDuration(source, source.Length, nativeVersion)
                manifest.NativeCodec = "experimental-native-" & nativeVersion.ToString(Globalization.CultureInfo.InvariantCulture)
                source.Position = 0
                ' Validate before creating any output. The digest is filled while compressing.
                manifest.RecordingSha256 = New String("0"c, 64) : manifest.Validate()
                token.ThrowIfCancellationRequested()
                Directory.CreateDirectory(IO.Path.GetDirectoryName(IO.Path.GetFullPath(destination)))
                Using output As New FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
                    created = True
                    Using zip As New ZipArchive(output, ZipArchiveMode.Create, True)
                        Using target = zip.CreateEntry("recording.bin", CompressionLevel.Optimal).Open()
                            manifest.RecordingSha256 = CopyRecording(source, target, source.Length, token)
                        End Using
                        Dim bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json)
                        If bytes.Length > MaximumManifestBytes Then Throw New IOException("Replay details are too large.")
                        Using target = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open() : target.Write(bytes) : End Using
                    End Using
                    output.Flush(True)
                End Using
            End Using
            token.ThrowIfCancellationRequested()
            File.Move(temporary, destination, False)
            Return manifest
        Catch ex As Exception
            failure = ex : Throw
        Finally
            If created Then CleanupFile(temporary, failure)
        End Try
    End Function

    Public Shared Function Inspect(filename As String) As ReplayManifest
        Using source = OpenArchive(filename), zip As New ZipArchive(source, ZipArchiveMode.Read)
            Return ReadManifest(zip)
        End Using
    End Function

    ' Availability is checked against the same open archive used for extraction.
    ' No game files or preferences change here; callers launch only after success.
    Public Shared Function Prepare(context As InstallContext, filename As String, Optional token As CancellationToken = Nothing) As PreparedReplay
        Using source = OpenArchive(filename), zip As New ZipArchive(source, ZipArchiveMode.Read)
            Dim manifest = ReadManifest(zip)
            ReplayContent.RequireAvailable(context, manifest.Content, token)
            ReplayContent.RecordedCars(manifest.Content.TrackId, manifest.EventConfig())
            context.RequireClosed()
            Dim folder = IO.Path.Combine(context.UserRoot, "replay-playback", Guid.NewGuid().ToString("N"))
            Files.NoLinks(folder)
            Dim prepared As New PreparedReplay(folder, manifest)
            Dim failure As Exception = Nothing
            Dim created As Boolean
            Try
                token.ThrowIfCancellationRequested()
                Directory.CreateDirectory(folder) : created = True
                Using input = zip.GetEntry("recording.bin").Open(), output As New FileStream(prepared.RecordingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
                    Dim hash = CopyRecording(input, output, manifest.RecordingBytes, token)
                    If Not hash.Equals(manifest.RecordingSha256, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("This replay is damaged: its recording checksum does not match.")
                    output.Position = 0
                    Dim nativeVersion As UInteger
                    If Math.Abs(NativeDuration(output, output.Length, nativeVersion) - manifest.DurationSeconds) > 0.000001 Then Throw New IOException("This replay's duration does not match its recording.")
                    If manifest.NativeCodec <> "experimental-native-" & nativeVersion.ToString(Globalization.CultureInfo.InvariantCulture) Then Throw New IOException("This replay's format does not match its recording.")
                    output.Flush(True)
                End Using
                Return prepared
            Catch ex As Exception
                failure = ex : Throw
            Finally
                If failure IsNot Nothing AndAlso created Then
                    Try : prepared.Dispose()
                    Catch cleanup As Exception : failure.Data("DiRT2VR.Cleanup") = cleanup.ToString()
                    End Try
                End If
            End Try
        End Using
    End Function

    Private Shared Function OpenArchive(filename As String) As FileStream
        Files.NoLinks(filename)
        Dim source As New FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
        If source.Length > MaximumRecordingBytes + MaximumManifestBytes + 16 * 1024 * 1024 Then
            source.Dispose() : Throw New IOException("This replay file is too large.")
        End If
        Return source
    End Function

    Private Shared Function ReadManifest(zip As ZipArchive) As ReplayManifest
        If zip.Entries.Count <> 2 OrElse zip.Entries.Where(Function(e) e.FullName = "manifest.json").Count() <> 1 OrElse
            zip.Entries.Where(Function(e) e.FullName = "recording.bin").Count() <> 1 Then Throw New IOException("Invalid replay archive contents.")
        Dim entry = zip.GetEntry("manifest.json")
        If entry.Length < 2 OrElse entry.Length > MaximumManifestBytes Then Throw New IOException("Invalid replay details length.")
        Dim bytes(CInt(entry.Length) - 1) As Byte
        Using input = entry.Open()
            input.ReadExactly(bytes)
            If input.ReadByte() <> -1 Then Throw New IOException("Replay details exceed their declared length.")
        End Using
        Try
            Dim manifest = JsonSerializer.Deserialize(Of ReplayManifest)(bytes, Json)
            If manifest Is Nothing Then Throw New IOException("Missing replay details.")
            manifest.Validate()
            If zip.GetEntry("recording.bin").Length <> manifest.RecordingBytes Then Throw New IOException("Replay recording length does not match its details.")
            Return manifest
        Catch ex As JsonException
            Throw New IOException("Invalid replay details.", ex)
        End Try
    End Function

    Private Shared Function CopyRecording(input As Stream, output As Stream, length As Long, token As CancellationToken) As String
        If length < 52 OrElse length > MaximumRecordingBytes Then Throw New IOException("Invalid replay recording size.")
        Using hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            Dim buffer(1024 * 1024 - 1) As Byte
            Dim remaining = length
            While remaining > 0
                token.ThrowIfCancellationRequested()
                Dim count = input.Read(buffer, 0, CInt(Math.Min(remaining, buffer.Length)))
                If count = 0 Then Throw New IOException("This replay recording is incomplete.")
                output.Write(buffer, 0, count) : hash.AppendData(buffer, 0, count) : remaining -= count
            End While
            token.ThrowIfCancellationRequested()
            If input.ReadByte() <> -1 Then Throw New IOException("Replay recording exceeds its declared length.")
            Return Convert.ToHexString(hash.GetHashAndReset())
        End Using
    End Function

    ' Envelope bounds only. Native typed records still require codec validation.
    Private Shared Function NativeDuration(stream As Stream, length As Long, ByRef nativeVersion As UInteger) As Double
        If length < 52 OrElse length > MaximumRecordingBytes Then Throw New IOException("Invalid native replay size.")
        Using reader As New BinaryReader(stream, Text.Encoding.UTF8, True)
            If Not reader.ReadBytes(8).SequenceEqual(New Byte() {68, 50, 86, 82, 76, 65, 66, 0}) Then Throw New IOException("Unsupported native replay recording.")
            nativeVersion = reader.ReadUInt32()
            If nativeVersion <> 4 AndAlso nativeVersion <> 5 Then Throw New IOException("Unsupported native replay recording.")
            Dim types = CLng(reader.ReadUInt32()), snapshot = CLng(reader.ReadUInt32()), frames = CLng(reader.ReadUInt32())
            Dim first = reader.ReadDouble(), last = reader.ReadDouble()
            Dim offset = CLng(reader.ReadUInt32()), metadata = CLng(reader.ReadUInt32()), audio = CLng(reader.ReadUInt32())
            If types < 1 OrElse types > 128 OrElse snapshot < 1 OrElse snapshot > 8 * 1024 * 1024 OrElse
                frames < 1 OrElse frames >= 2096898048L OrElse Not Double.IsFinite(first) OrElse first < 0 OrElse first >= 0.1 OrElse
                Not Double.IsFinite(last) OrElse last <= first OrElse last > 86400 OrElse offset >= frames OrElse
                metadata < types * 4 OrElse metadata > 32 * 1024 * 1024 OrElse audio > 4096 OrElse
                length <> 52 + types * 4 + audio * 56 + metadata + snapshot + frames Then Throw New IOException("Invalid native replay bounds.")
            Return last
        End Using
    End Function

    Private Shared Sub CleanupFile(filename As String, failure As Exception)
        Try : SafeFiles.DeleteOwned(filename)
        Catch cleanup As Exception
            If failure Is Nothing Then Throw
            failure.Data("DiRT2VR.Cleanup") = cleanup.ToString()
        End Try
    End Sub
End Class

Public NotInheritable Class PreparedReplay
    Implements IDisposable
    Public ReadOnly Folder As String
    Public ReadOnly Manifest As ReplayManifest
    Public ReadOnly RecordingPath As String
    Friend Sub New(path As String, details As ReplayManifest)
        Folder = path : Manifest = details : RecordingPath = IO.Path.Combine(path, "recording.bin")
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        ' Only files created by extraction/session setup. Unexpected files prevent removal.
        Files.NoLinks(Folder)
        SafeFiles.DeleteOwned(RecordingPath)
        For Each name In {"event.json", "error.txt"}
            SafeFiles.DeleteOwned(IO.Path.Combine(Folder, name))
        Next
        If Directory.Exists(Folder) Then Directory.Delete(Folder, False)
    End Sub
End Class
