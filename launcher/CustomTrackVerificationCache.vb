Imports DiRT2VR.CustomTracks

' UI-only, successful verification cache. The launch worker still verifies files.
Public NotInheritable Class CustomTrackVerificationCache
    Implements IDisposable
    Private ReadOnly game As String
    Private ReadOnly gate As New Object
    Private ReadOnly entries As New Dictionary(Of String, Entry)(StringComparer.Ordinal)
    Private disposed As Boolean

    Private NotInheritable Class Entry
        Implements IDisposable
        Public Receipt As PackReceipt
        Private invalidated As Integer
        Private ReadOnly watchers As New List(Of FileSystemWatcher)
        Public ReadOnly Property Valid As Boolean
            Get
                Return Threading.Volatile.Read(invalidated) = 0
            End Get
        End Property
        Public Sub New(game As String, pack As TrackPack)
            Dim targets = pack.InstallRoots.Select(Function(p) SafeFiles.Inside(game, p)).Concat({
                SafeFiles.Inside(game, "DiRT2VR/custom-track-install"), SafeFiles.Inside(game, "DiRT2VR/custom-track-session")}).ToArray()
            Dim affected As Action(Of String) = Sub(path)
                                                   If targets.Any(Function(t) path.Equals(t, StringComparison.OrdinalIgnoreCase) OrElse
                                                       path.StartsWith(t & IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) OrElse
                                                       t.StartsWith(path & IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Then Invalidate()
                                               End Sub
            Try
                For Each root In {IO.Path.Combine(game, "tracks"), IO.Path.Combine(game, "DiRT2VR")}
                    Dim watcher As New FileSystemWatcher(root) With {.IncludeSubdirectories = True,
                        .NotifyFilter = NotifyFilters.FileName Or NotifyFilters.DirectoryName Or NotifyFilters.LastWrite Or NotifyFilters.Size}
                    watchers.Add(watcher)
                    AddHandler watcher.Changed, Sub(sender, e) affected(e.FullPath)
                    AddHandler watcher.Created, Sub(sender, e) affected(e.FullPath)
                    AddHandler watcher.Deleted, Sub(sender, e) affected(e.FullPath)
                    AddHandler watcher.Renamed, Sub(sender, e)
                                                    affected(e.OldFullPath) : affected(e.FullPath)
                                                End Sub
                    AddHandler watcher.Error, Sub() Invalidate()
                    watcher.EnableRaisingEvents = True
                Next
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is ArgumentException
                ' If change notifications are unavailable, use normal verification.
                Invalidate() : Dispose()
            End Try
        End Sub
        Public Sub Invalidate()
            Threading.Interlocked.Exchange(invalidated, 1)
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            For Each watcher In watchers : watcher.Dispose() : Next
            watchers.Clear()
        End Sub
    End Class

    Public Sub New(gameRoot As String)
        game = IO.Path.GetFullPath(gameRoot)
    End Sub
    Public Function TryGet(packId As String) As PackReceipt
        SyncLock gate
            Dim saved As Entry = Nothing
            If Not disposed AndAlso entries.TryGetValue(packId, saved) AndAlso saved.Valid Then Return saved.Receipt
        End SyncLock
        Return Nothing
    End Function
    Public Function Read(pack As TrackPack, token As CancellationToken) As PackReceipt
        Invalidate(pack.Id)
        Dim pending As New Entry(game, pack)
        Try
            Dim receipt = pack.Read(game, True, token)
            CustomTrackService.RequireLauncher(receipt)
            token.ThrowIfCancellationRequested()
            SyncLock gate
                If Not disposed AndAlso pending.Valid Then
                    pending.Receipt = receipt : entries(pack.Id) = pending : pending = Nothing
                End If
            End SyncLock
            Return receipt
        Finally
            pending?.Dispose()
        End Try
    End Function
    Public Sub Invalidate(packId As String)
        SyncLock gate
            Dim saved As Entry = Nothing
            If entries.Remove(packId, saved) Then saved.Dispose()
        End SyncLock
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock gate
            disposed = True
            For Each saved In entries.Values : saved.Dispose() : Next
            entries.Clear()
        End SyncLock
    End Sub
End Class
