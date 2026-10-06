Imports System.IO.MemoryMappedFiles
Imports DiRT2VR.CustomTracks

Public Class CompletedLap
    Public Property Sequence As Integer
    Public Property ProcessId As Integer
    Public Property Microseconds As Long
End Class
Public NotInheritable Class BestLapChannel
    Implements IDisposable
    Private ReadOnly mapping As MemoryMappedFile
    Private ReadOnly view As MemoryMappedViewAccessor
    Private ReadOnly context As InstallContext
    Private ReadOnly layoutId As String
    Private ReadOnly carCode As String
    Private ReadOnly version As String
    Private ReadOnly createdUtc As DateTime = DateTime.UtcNow
    Private lastSequence As Integer
    Public ReadOnly Name As String
    Public Sub New(context As InstallContext, start As ProcessStartInfo, layoutId As String, carCode As String)
        start.Environment.Remove("DIRT2VR_LAP_CHANNEL")
        If Not TrackPacks.Nordschleife.IsLayout(layoutId) Then Return
        Me.context = context : Me.layoutId = layoutId : Me.carCode = carCode
        version = TrackPacks.Nordschleife.Read(context.GameRoot, False).Version
        Name = "Local\DiRT2VR.Lap." & Guid.NewGuid().ToString("N")
        mapping = MemoryMappedFile.CreateNew(Name, 32) : view = mapping.CreateViewAccessor()
        view.Write(0, &H32504C44) : view.Write(4, 1) : view.Write(8, 32)
        start.Environment("DIRT2VR_LAP_CHANNEL") = Name
    End Sub
    Public Shared Function ReadResult(view As MemoryMappedViewAccessor, lastSequence As Integer) As CompletedLap
        If view.Capacity < 32 OrElse view.ReadInt32(0) <> &H32504C44 OrElse view.ReadInt32(4) <> 1 OrElse view.ReadInt32(8) <> 32 Then Return Nothing
        Dim sequence = view.ReadInt32(12)
        If sequence <= 0 OrElse (sequence And 1) <> 0 OrElse sequence <= lastSequence Then Return Nothing
        Thread.MemoryBarrier()
        Dim pid = view.ReadInt32(16), laps = view.ReadInt32(20), duration = view.ReadInt64(24)
        Thread.MemoryBarrier()
        If view.ReadInt32(12) <> sequence OrElse pid <= 0 OrElse laps <> 1 OrElse duration < 1000000 OrElse duration > 7200000000L Then Return Nothing
        Return New CompletedLap With {.Sequence = sequence, .ProcessId = pid, .Microseconds = duration}
    End Function
    Public Sub Poll()
        If view Is Nothing Then Return
        Dim result = ReadResult(view, lastSequence)
        If result Is Nothing Then Return
        Try
            Using game = Process.GetProcessById(result.ProcessId)
                If game.HasExited OrElse game.ProcessName <> "dirt2_game" OrElse game.StartTime.ToUniversalTime() < createdUtc.AddSeconds(-1) OrElse
                    Not String.Equals(game.MainModule.FileName, IO.Path.Combine(context.GameRoot, "dirt2_game.exe"), StringComparison.OrdinalIgnoreCase) Then Return
            End Using
            lastSequence = result.Sequence
            BestLapStore.Record(context, New BestLapRecord With {.LayoutId = layoutId, .PackVersion = version, .CarCode = carCode,
                .Milliseconds = result.Microseconds \ 1000, .CompletedUtc = DateTime.UtcNow})
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is System.Text.Json.JsonException
            Console.Error.WriteLine("Best lap could not be saved: " & ex.Message)
        Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ComponentModel.Win32Exception
            ' The process may have exited between the observation and ownership check.
        End Try
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        view?.Dispose() : mapping?.Dispose()
    End Sub
End Class
