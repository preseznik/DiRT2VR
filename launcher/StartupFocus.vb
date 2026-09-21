' DiRT 2 replaces its initial foreground window during graphics startup.
' Only repair that transition, never continually enforce foreground focus.
Public Class StartupFocusPolicy
    Private ReadOnly started As Long
    Private ReadOnly inputAtStart As UInteger
    Private firstWindow As IntPtr
    Private hadForeground As Boolean
    Public Property Finished As Boolean
    Public Sub New(now As Long, lastInput As UInteger)
        started = now : inputAtStart = lastInput
    End Sub
    Public Function ShouldActivate(window As IntPtr, foreground As IntPtr, lastInput As UInteger, now As Long) As Boolean
        If Finished Then Return False
        If now - started >= 30000 OrElse lastInput <> inputAtStart Then
            Finished = True : Return False
        End If
        If window = IntPtr.Zero Then Return False
        If firstWindow = IntPtr.Zero Then firstWindow = window
        If window = firstWindow Then
            hadForeground = hadForeground OrElse foreground = window
            Return False
        End If
        Finished = True
        Return hadForeground AndAlso foreground <> window
    End Function
End Class

Public Class StartupFocus
    <StructLayout(LayoutKind.Sequential)> Private Structure LastInput
        Public Size As UInteger
        Public Tick As UInteger
    End Structure
    <DllImport("user32.dll")> Public Shared Function AllowSetForegroundWindow(processId As UInteger) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetForegroundWindow() As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function SetForegroundWindow(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetLastInputInfo(ByRef value As LastInput) As Boolean
    End Function
    Private ReadOnly policy As StartupFocusPolicy
    Private ReadOnly executable As String
    Public Sub New(context As InstallContext)
        executable = IO.Path.Combine(context.GameRoot, "dirt2_game.exe")
        Dim value As New LastInput With {.Size = CUInt(Marshal.SizeOf(Of LastInput)())}
        policy = New StartupFocusPolicy(Environment.TickCount64, 0) With {.Finished = True}
        If GetLastInputInfo(value) Then policy = New StartupFocusPolicy(Environment.TickCount64, value.Tick)
    End Sub
    Public Sub Poll()
        If policy.Finished Then Return
        Dim input As New LastInput With {.Size = CUInt(Marshal.SizeOf(Of LastInput)())}
        If Not GetLastInputInfo(input) Then
            policy.Finished = True : Return
        End If
        Dim window As IntPtr
        For Each game In Process.GetProcessesByName("dirt2_game")
            Using game
                Try
                    If String.Equals(game.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase) Then window = game.MainWindowHandle
                Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
                    ' Process can exit during startup or recovery.
                End Try
            End Using
        Next
        If policy.ShouldActivate(window, GetForegroundWindow(), input.Tick, Environment.TickCount64) Then SetForegroundWindow(window)
    End Sub
End Class
