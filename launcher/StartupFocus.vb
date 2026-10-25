' DiRT 2 replaces its initial foreground window during graphics startup.
' VR also needs an initial handoff when the runtime leaves its window behind.
' Only repair startup, never continually enforce foreground focus.
Public Class StartupFocusPolicy
    Private ReadOnly started As Long
    Private previousInput As UInteger
    Private previousForeground As IntPtr
    Private firstWindow As IntPtr
    Private hadForeground As Boolean
    Private ReadOnly activateInitial As Boolean
    Public Property Finished As Boolean
    Public Property Outcome As String = "Waiting for game window"
    Public Sub New(now As Long, lastInput As UInteger, Optional activateInitial As Boolean = False, Optional foreground As IntPtr = Nothing)
        started = now : previousInput = lastInput : Me.activateInitial = activateInitial
        previousForeground = foreground
    End Sub
    Public Function ShouldActivate(window As IntPtr, foreground As IntPtr, lastInput As UInteger, now As Long) As Boolean
        If Finished Then Return False
        If now - started >= 30000 Then
            Outcome = "Expired"
            Finished = True : Return False
        End If
        Dim inputChanged = lastInput <> previousInput
        Dim switchedWindow = foreground <> previousForeground
        previousForeground = foreground
        If activateInitial Then previousInput = lastInput
        ' Mouse movement also changes LASTINPUTINFO. It does not mean the player
        ' switched applications while waiting for the game window to appear.
        If activateInitial AndAlso firstWindow <> IntPtr.Zero AndAlso window = firstWindow AndAlso foreground <> window AndAlso switchedWindow AndAlso inputChanged Then
            Finished = True : Outcome = "Canceled: input switched away during VR startup" : Return False
        End If
        If window = IntPtr.Zero Then Return False
        If firstWindow = IntPtr.Zero Then
            firstWindow = window : Outcome = "Initial game window detected"
            If activateInitial AndAlso foreground <> window Then
                Outcome = "Requesting initial VR game focus"
                Return True
            End If
        End If
        If window = firstWindow Then
            If Not activateInitial AndAlso hadForeground AndAlso foreground <> window AndAlso inputChanged Then
                Finished = True : Outcome = "Canceled: input switched away from game" : Return False
            End If
            previousInput = lastInput
            hadForeground = hadForeground OrElse foreground = window
            If hadForeground Then Outcome = "Initial game window had focus"
            Return False
        End If
        Finished = True
        Outcome = If(Not hadForeground, "Skipped: initial window never held focus", If(foreground = window, "Replacement already focused", "Requesting replacement focus"))
        Return hadForeground AndAlso foreground <> window
    End Function
    Public Sub ActivationResult(activated As Boolean)
        hadForeground = hadForeground OrElse activated
        Outcome = If(activated, "Game window activated", "Windows declined activation")
    End Sub
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
    <DllImport("user32.dll")> Private Shared Function IsIconic(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function IsWindowVisible(window As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function ShowWindowAsync(window As IntPtr, command As Integer) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function SetWindowPos(window As IntPtr, after As IntPtr, x As Integer, y As Integer, width As Integer, height As Integer, flags As UInteger) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetLastInputInfo(ByRef value As LastInput) As Boolean
    End Function
    Private ReadOnly policy As StartupFocusPolicy
    Private ReadOnly executable As String
    Private ReadOnly vrLaunch As Boolean
    Private activationHistory As String = ""
    Public ReadOnly Property Outcome As String
        Get
            Return activationHistory & policy.Outcome
        End Get
    End Property
    Public Sub New(context As InstallContext, Optional vr As Boolean = False)
        executable = IO.Path.Combine(context.GameRoot, "dirt2_game.exe")
        vrLaunch = vr
        Dim value As New LastInput With {.Size = CUInt(Marshal.SizeOf(Of LastInput)())}
        policy = New StartupFocusPolicy(Environment.TickCount64, 0) With {.Finished = True}
        If GetLastInputInfo(value) Then policy = New StartupFocusPolicy(Environment.TickCount64, value.Tick, vr, GetForegroundWindow())
    End Sub
    Public Sub Poll()
        If policy.Finished Then Return
        Dim input As New LastInput With {.Size = CUInt(Marshal.SizeOf(Of LastInput)())}
        If Not GetLastInputInfo(input) Then
            policy.Finished = True : policy.Outcome = "Input state unavailable" : Return
        End If
        Dim window = GameWindow.Find(executable, vrLaunch)
        If policy.ShouldActivate(window, GetForegroundWindow(), input.Tick, Environment.TickCount64) Then
            Dim request = policy.Outcome
            policy.ActivationResult(If(vrLaunch, ActivateWindow(window), SetForegroundWindow(window)))
            ' At most the initial window and its replacement. Retain the result
            ' even if later input cancels startup monitoring.
            activationHistory &= request & ": " & policy.Outcome & $" (HWND={window.ToInt64():X}, visible={IsWindowVisible(window)}, minimized={IsIconic(window)}); "
        End If
    End Sub
    ' Production calls only after verifying the game executable and startup policy.
    ' Also exercised with owned test windows, including minimized/maximized ones.
    Public Shared Function ActivateWindow(window As IntPtr) As Boolean
        If window = IntPtr.Zero Then Return False
        If IsIconic(window) Then ShowWindowAsync(window, 9) ' SW_RESTORE
        ' Raise once without resizing or making it permanently topmost. Async
        ' dispatch avoids blocking on the game thread during graphics startup.
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, &H4000UI Or &H40UI Or &H10UI Or &H2UI Or &H1UI)
        Return SetForegroundWindow(window)
    End Function
End Class
