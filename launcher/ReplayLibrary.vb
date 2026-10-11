Imports DiRT2VR.CustomTracks

Public Class ReplayLibraryItem
    Public Property Path As String = ""
    Public Property Manifest As ReplayManifest
    Public Property StoredBytes As Long
    Public Property Problem As String = ""
End Class

Public Class ReplayPreferences
    Public Property Version As Integer = 1
    Public Property RecordFullReplay As Boolean = False
    Public Property AutoSave As Boolean = False
    Public Shared Function Load(context As InstallContext) As ReplayPreferences
        Dim path = PreferencesPath(context)
        If Not File.Exists(path) Then Return New ReplayPreferences()
        Dim result = SafeFiles.ReadJson(Of ReplayPreferences)(path)
        If result.Version <> 1 Then Throw New IOException("Unsupported replay preferences.")
        Return result
    End Function
    Public Sub Save(context As InstallContext)
        If Version <> 1 Then Throw New IOException("Unsupported replay preferences.")
        Files.SaveJson(PreferencesPath(context), Me)
    End Sub
    Private Shared Function PreferencesPath(context As InstallContext) As String
        Return IO.Path.Combine(context.UserRoot, "replay-preferences.json")
    End Function
End Class

Public Class ReplayLibrary
    Private ReadOnly context As InstallContext
    Public ReadOnly Root As String
    Public Sub New(value As InstallContext)
        ' Saving user data must not require elevation in protected game folders.
        context = value : Root = IO.Path.Combine(context.UserRoot, "Replays")
    End Sub
    Public Function Items(Optional token As CancellationToken = Nothing) As List(Of ReplayLibraryItem)
        Dim result As New List(Of ReplayLibraryItem)
        Files.NoLinks(Root)
        If Not Directory.Exists(Root) Then Return result
        For Each path In Directory.EnumerateFiles(Root, "*.d2replay", SearchOption.TopDirectoryOnly)
            token.ThrowIfCancellationRequested()
            Dim item As New ReplayLibraryItem With {.Path = path}
            Try
                item.Manifest = ReplayArchive.Inspect(path)
                item.StoredBytes = New FileInfo(path).Length
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is InvalidDataException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Text.Json.JsonException
                ' One damaged recording must not hide the rest of the library.
                item.Problem = ex.Message
            End Try
            result.Add(item)
        Next
        Return result.OrderByDescending(Function(i) If(i.Manifest?.RecordedUtc, DateTimeOffset.MinValue)).ToList()
    End Function
    Public Function Save(recording As String, manifest As ReplayManifest, Optional token As CancellationToken = Nothing) As ReplayLibraryItem
        ' A fresh library ID gives each completed attempt a distinct file, even
        ' when the player repeats the same event/name within the same second.
        Dim copy = System.Text.Json.JsonSerializer.Deserialize(Of ReplayManifest)(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, ReplayArchive.Json), ReplayArchive.Json)
        copy.Id = Guid.NewGuid().ToString("N")
        Dim path = SafeFiles.Inside(Root, copy.Id & ".d2replay")
        Dim saved = ReplayArchive.Save(path, recording, copy, token)
        Register(path, saved)
        Return New ReplayLibraryItem With {.Path = path, .Manifest = saved, .StoredBytes = New FileInfo(path).Length}
    End Function
    Public Function Prepare(path As String, Optional token As CancellationToken = Nothing) As PreparedReplay
        Dim name = IO.Path.GetFileName(path)
        If Not name.EndsWith(".d2replay", StringComparison.OrdinalIgnoreCase) OrElse
            Not IO.Path.GetFullPath(path).Equals(SafeFiles.Inside(Root, name), StringComparison.OrdinalIgnoreCase) Then Throw New IOException("Choose a replay from this installation's library.")
        ' Only recordings made by this installation may enter the native codec.
        ' The ZIP checksum alone does not establish where a recording came from.
        Using locked As New FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            RequireOwned(path)
            Return ReplayArchive.Prepare(context, path, token)
        End Using
    End Function
    Private Function Receipt(id As String) As String
        Dim parsed As Guid
        If Not Guid.TryParseExact(id, "N", parsed) OrElse id <> id.ToLowerInvariant() Then Throw New IOException("Invalid replay ID.")
        Return SafeFiles.Inside(IO.Path.Combine(context.UserRoot, "replay-owned"), id & ".json")
    End Function
    Private Sub Register(path As String, manifest As ReplayManifest)
        Files.SaveJson(Receipt(manifest.Id), New ReplayOwnership With {.Id = manifest.Id, .ArchiveSha256 = Files.Hash(path)})
    End Sub
    Public Function RequireOwned(path As String) As ReplayManifest
        Files.NoLinks(path)
        Dim manifest = ReplayArchive.Inspect(path), receiptPath = Receipt(manifest.Id)
        If Not File.Exists(receiptPath) Then Throw New IOException("This experimental player only opens replays recorded by this installation. Imported replay files are not supported yet.")
        Dim owned = SafeFiles.ReadJson(Of ReplayOwnership)(receiptPath)
        If owned.Id <> manifest.Id OrElse Not SafeFiles.Digest(owned.ArchiveSha256) OrElse Files.Hash(path) <> owned.ArchiveSha256 Then Throw New IOException("This replay has changed since it was saved. Playback was blocked.")
        Return manifest
    End Function
    Public Function PathFor(id As String) As String
        Receipt(id)
        Return SafeFiles.Inside(Root, id & ".d2replay")
    End Function
    Public Function SaveLatest(recording As String, manifest As ReplayManifest, autoSave As Boolean) As ReplayLibraryItem
        Dim folder = IO.Path.Combine(context.UserRoot, "replay-latest")
        Dim previous = LatestPath()
        Dim copy = System.Text.Json.JsonSerializer.Deserialize(Of ReplayManifest)(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest, ReplayArchive.Json), ReplayArchive.Json)
        copy.Id = Guid.NewGuid().ToString("N")
        Dim path = SafeFiles.Inside(folder, copy.Id & ".d2replay")
        Dim saved = ReplayArchive.Save(path, recording, copy)
        Register(path, saved)
        Files.SaveJson(IO.Path.Combine(folder, "latest.json"), New ReplayOwnership With {.Id = saved.Id})
        If autoSave Then KeepLatest()
        ' A failed replacement leaves the older recording and its receipt intact.
        If previous IsNot Nothing AndAlso previous <> path Then
            Try
                Dim old = RequireOwned(previous)
                SafeFiles.DeleteOwned(previous)
                If Not File.Exists(PathFor(old.Id)) Then SafeFiles.DeleteOwned(Receipt(old.Id))
            Catch ex As IOException
                Trace.WriteLine("Previous replay retained: " & ex.Message)
            End Try
        End If
        Return New ReplayLibraryItem With {.Path = path, .Manifest = saved, .StoredBytes = New FileInfo(path).Length}
    End Function
    Public Function LatestPath() As String
        Dim folder = IO.Path.Combine(context.UserRoot, "replay-latest"), pointer = IO.Path.Combine(folder, "latest.json")
        Files.NoLinks(pointer)
        If Not File.Exists(pointer) Then Return Nothing
        Dim id = SafeFiles.ReadJson(Of ReplayOwnership)(pointer).Id
        Receipt(id)
        Return SafeFiles.Inside(folder, id & ".d2replay")
    End Function
    Public Function KeepLatest() As ReplayLibraryItem
        Dim source = LatestPath()
        If source Is Nothing Then Throw New IOException("Finish a Direct practice or Race session first, then return here to save it.")
        Using locked As New FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read)
            Dim manifest = RequireOwned(source), destination = PathFor(manifest.Id)
            If Not File.Exists(destination) Then
                Files.NoLinks(destination) : Directory.CreateDirectory(Root)
                Dim temporary = destination & "." & Guid.NewGuid().ToString("N") & ".tmp"
                Try
                    Using output As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                        locked.CopyTo(output) : output.Flush(True)
                    End Using
                    File.Move(temporary, destination, False)
                Finally
                    SafeFiles.DeleteOwned(temporary)
                End Try
            End If
            RequireOwned(destination)
            Return New ReplayLibraryItem With {.Path = destination, .Manifest = manifest, .StoredBytes = New FileInfo(destination).Length}
        End Using
    End Function
    Public Sub Delete(id As String)
        Dim path = PathFor(id)
        RequireOwned(path) : SafeFiles.DeleteOwned(path)
        If LatestPath() <> IO.Path.Combine(context.UserRoot, "replay-latest", id & ".d2replay") Then SafeFiles.DeleteOwned(Receipt(id))
    End Sub
End Class

Public Class ReplayOwnership
    Public Property Id As String = ""
    Public Property ArchiveSha256 As String = ""
End Class
