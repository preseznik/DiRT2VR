Imports System.Drawing
Imports System.ComponentModel

' Window management only: never activates a window or changes the display mode.
Public Class BorderlessWindow
    Private Const StyleIndex As Integer = -16
    Private Const ExStyleIndex As Integer = -20
    Private Const FrameBits As Long = &HC00000L Or &H40000L Or &H80000L Or &H30000L
    Private Const EdgeBits As Long = &H1L Or &H100L Or &H200L Or &H20000L
    Private Const PositionFlags As UInteger = &H10UI Or &H20UI Or &H200UI Or &H4000UI ' NoActivate, FrameChanged, NoOwnerZOrder, AsyncWindowPos
    <StructLayout(LayoutKind.Sequential)>
    Private Structure WindowRect
        Public Left, Top, Right, Bottom As Integer
    End Structure
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowLongPtrW(window As IntPtr, index As Integer) As IntPtr
    End Function
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowLongPtrW(window As IntPtr, index As Integer, value As IntPtr) As IntPtr
    End Function
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowPos(window As IntPtr, after As IntPtr, x As Integer, y As Integer, width As Integer, height As Integer, flags As UInteger) As Boolean
    End Function
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowRect(window As IntPtr, ByRef rect As WindowRect) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function IsWindowVisible(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function IsIconic(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")>
    Private Shared Function IsHungAppWindow(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)>
    Private Shared Function GetClassName(window As IntPtr, name As System.Text.StringBuilder, count As Integer) As Integer
    End Function
    Private Class Attempt
        Public Style As Long
        Public ExStyle As Long
        Public Bounds As Rectangle
        Public Deadline As Long
        Public Complete As Boolean
    End Class
    Private ReadOnly executable As String
    Private ReadOnly bounds As Rectangle
    Private ReadOnly attempts As New Dictionary(Of IntPtr, Attempt)
    Public Property Warning As String = ""
    Public Sub New(context As InstallContext, target As Rectangle)
        executable = IO.Path.Combine(context.GameRoot, "dirt2_game.exe")
        If target.Width <= 0 OrElse target.Height <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(target))
        bounds = target
    End Sub
    Public Sub Poll()
        For Each game In Process.GetProcessesByName("dirt2_game")
            Using game
                Try
                    If String.Equals(game.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase) Then
                        UpdateWindow(game.MainWindowHandle, Environment.TickCount64)
                    End If
                Catch ex As InvalidOperationException
                    ' The process can exit between enumeration and inspection.
                Catch ex As Win32Exception
                    If Not game.HasExited Then Warning = "Borderless fullscreen unavailable; using windowed mode. " & ex.Message
                End Try
            End Using
        Next
    End Sub
    ' Also used by the owned-window tests; production callers verify the executable in Poll.
    Public Sub UpdateWindow(window As IntPtr, now As Long)
        If window = IntPtr.Zero OrElse Not IsWindowVisible(window) OrElse IsIconic(window) OrElse IsHungAppWindow(window) Then Return
        Dim name As New System.Text.StringBuilder(256)
        GetClassName(window, name, name.Capacity)
        If name.ToString() = "#32770" Then Return ' Never resize an error dialog.
        Dim attempt As Attempt = Nothing
        attempts.TryGetValue(window, attempt)
        If attempt IsNot Nothing AndAlso attempt.Complete Then Return
        Try
            If attempt Is Nothing Then
                Dim original = ReadBounds(window)
                If original.Width <= 0 OrElse original.Height <= 0 Then Return
                attempt = New Attempt With {.Style = ReadStyle(window, StyleIndex), .ExStyle = ReadStyle(window, ExStyleIndex), .Bounds = original, .Deadline = now + 5000}
                attempts.Add(window, attempt)
                WriteStyle(window, StyleIndex, (attempt.Style And Not FrameBits) Or &H80000000L)
                WriteStyle(window, ExStyleIndex, attempt.ExStyle And Not EdgeBits)
                ' HWND_NOTOPMOST if needed; otherwise preserve the existing Z order.
                Position(window, bounds, (attempt.ExStyle And &H8L) <> 0)
            ElseIf ReadBounds(window) = bounds AndAlso (ReadStyle(window, StyleIndex) And FrameBits) = 0 AndAlso (ReadStyle(window, ExStyleIndex) And (EdgeBits Or &H8L)) = 0 Then
                attempt.Complete = True
            ElseIf now >= attempt.Deadline Then
                Throw New Win32Exception("The game did not accept the borderless window size.")
            End If
        Catch ex As Win32Exception
            If attempt IsNot Nothing Then
                attempt.Complete = True ' Report once; do not fight the game or the user.
                Try
                    WriteStyle(window, StyleIndex, attempt.Style)
                    WriteStyle(window, ExStyleIndex, attempt.ExStyle)
                    Position(window, attempt.Bounds, False)
                Catch rollback As Win32Exception
                    Warning = "Borderless fullscreen failed; window restoration also failed. Restart with the option off. " & rollback.Message
                    Return
                End Try
            End If
            Warning = "Borderless fullscreen unavailable; using windowed mode. " & ex.Message
        End Try
    End Sub
    Private Shared Function ReadStyle(window As IntPtr, index As Integer) As Long
        Marshal.SetLastPInvokeError(0)
        Dim value = GetWindowLongPtrW(window, index)
        If value = IntPtr.Zero AndAlso Marshal.GetLastPInvokeError() <> 0 Then Throw New Win32Exception(Marshal.GetLastPInvokeError())
        Return value.ToInt64()
    End Function
    Private Shared Sub WriteStyle(window As IntPtr, index As Integer, value As Long)
        Marshal.SetLastPInvokeError(0)
        If SetWindowLongPtrW(window, index, New IntPtr(value)) = IntPtr.Zero AndAlso Marshal.GetLastPInvokeError() <> 0 Then Throw New Win32Exception(Marshal.GetLastPInvokeError())
    End Sub
    Private Shared Function ReadBounds(window As IntPtr) As Rectangle
        Dim rect As New WindowRect
        If Not GetWindowRect(window, rect) Then Throw New Win32Exception(Marshal.GetLastPInvokeError())
        Return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
    End Function
    Private Shared Sub Position(window As IntPtr, target As Rectangle, removeTopmost As Boolean)
        If Not SetWindowPos(window, New IntPtr(-2), target.X, target.Y, target.Width, target.Height, PositionFlags Or If(removeTopmost, 0UI, &H4UI)) Then Throw New Win32Exception(Marshal.GetLastPInvokeError())
    End Sub
End Class
