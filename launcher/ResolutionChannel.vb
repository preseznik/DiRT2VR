Imports System.IO.MemoryMappedFiles

Public Class ResolutionStatus
    Public Property RequestedWidth As Integer
    Public Property RequestedHeight As Integer
    Public Property Actual As Integer() = New Integer(5) {}
    Public Property UpdatedUtc As DateTime = DateTime.UtcNow
    Public Function Description() As String
        If Actual Is Nothing OrElse Actual.Length <> 6 OrElse Actual.Any(Function(n) n < 0 OrElse n > 32768) Then Return "Resolution report unavailable."
        Dim size As Func(Of Integer, String) = Function(i) If(Actual(i) = 0 OrElse Actual(i + 1) = 0, "not reported", $"{Actual(i)} × {Actual(i + 1)}")
        Dim mismatch = Actual(0) > 0 AndAlso Actual(1) > 0 AndAlso (Actual(0) <> RequestedWidth OrElse Actual(1) <> RequestedHeight)
        Return $"Last VR launch ({UpdatedUtc.ToLocalTime():g}): requested {RequestedWidth} × {RequestedHeight}." & Environment.NewLine &
            $"Game: {size(0)} → headset L: {size(2)}, R: {size(4)}." &
            If(mismatch, Environment.NewLine & "Game resolution differs from the request.", "")
    End Function
End Class

' No game-file writes. A fresh mapping prevents observations leaking between launches.
Public NotInheritable Class ResolutionChannel
    Implements IDisposable
    Private ReadOnly mapping As MemoryMappedFile
    Private ReadOnly view As MemoryMappedViewAccessor
    Private ReadOnly path As String
    Private ReadOnly report As ResolutionStatus
    Private lastSequence As Integer
    Private writes As Integer
    Public Sub New(context As InstallContext, start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_RESOLUTION_CHANNEL")
        If Not start.Environment.ContainsKey("DIRT2VR_HEADSET") OrElse start.Environment("DIRT2VR_HEADSET") <> "1" Then Return
        Dim name = "Local\DiRT2VR.Resolution." & Guid.NewGuid().ToString("N")
        mapping = MemoryMappedFile.CreateNew(name, 44)
        view = mapping.CreateViewAccessor()
        view.Write(0, &H32565252) : view.Write(4, 1)
        view.Write(8, settings.RenderWidth) : view.Write(12, settings.RenderHeight)
        path = IO.Path.Combine(context.UserRoot, "resolution.json")
        report = New ResolutionStatus With {.RequestedWidth = settings.RenderWidth, .RequestedHeight = settings.RenderHeight}
        Save()
        start.Environment("DIRT2VR_RESOLUTION_CHANNEL") = name
    End Sub
    Public Sub Poll(Optional final As Boolean = False)
        If view Is Nothing Then Return
        Dim sequence = view.ReadInt32(16)
        If (sequence And 1) <> 0 OrElse sequence = lastSequence Then Return
        Thread.MemoryBarrier()
        Dim actual(5) As Integer
        view.ReadArray(20, actual, 0, actual.Length)
        Thread.MemoryBarrier()
        If view.ReadInt32(16) <> sequence Then Return
        If actual.Any(Function(n) n < 0 OrElse n > 32768) Then Return
        If writes >= 32 AndAlso Not final Then Return
        lastSequence = sequence
        report.Actual = actual : report.UpdatedUtc = DateTime.UtcNow
        Save() : writes += 1
    End Sub
    Private Sub Save()
        Try
            Files.SaveJson(path, report)
        Catch ex As IOException
            ' A read-only/unavailable report must not interrupt gameplay or recovery.
        Catch ex As UnauthorizedAccessException
        End Try
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Poll(True)
        view?.Dispose() : mapping?.Dispose()
    End Sub
End Class
