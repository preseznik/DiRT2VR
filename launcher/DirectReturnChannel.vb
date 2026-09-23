' Lifetime is exactly one game launch; crashes, Alt+F4 and stale signals cannot
' request a later return. Session consumes it only after all game processes exit.
Public NotInheritable Class DirectReturnChannel
    Implements IDisposable
    Private ReadOnly signal As EventWaitHandle
    Public Sub New(start As ProcessStartInfo, enabled As Boolean)
        start.Environment.Remove("DIRT2VR_RETURN_CHANNEL")
        start.Environment.Remove("DIRT2VR_RETURN_PID")
        If Not enabled Then Return
        Dim name = "Local\DiRT2VR.Return." & Guid.NewGuid().ToString("N")
        signal = New EventWaitHandle(False, EventResetMode.ManualReset, name)
        start.Environment("DIRT2VR_RETURN_CHANNEL") = name
        start.Environment("DIRT2VR_RETURN_PID") = Environment.ProcessId.ToString(Globalization.CultureInfo.InvariantCulture)
    End Sub
    Public ReadOnly Property Requested As Boolean
        Get
            Return signal IsNot Nothing AndAlso signal.WaitOne(0)
        End Get
    End Property
    Public Sub Dispose() Implements IDisposable.Dispose
        signal?.Dispose()
    End Sub
End Class
