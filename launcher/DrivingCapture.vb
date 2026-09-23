' Capture is based on change from a stable rest sample, never on an axis being nonzero.
' Callers supply monotonic milliseconds so timing/reconnection behavior can be regression tested.
Public Class DrivingCapture
    Private ReadOnly device As DrivingInput.Device
    Private ReadOnly action As String
    Private baseline As DrivingInput.Sample
    Private initialized As Boolean
    Private stableSince As Long
    Private candidateSince As Long
    Private releasedSince As Long = -1
    Private candidate As DrivingBinding
    Private activeAxis As Integer = -1
    Private activeButton As Integer = -1
    Private peak As Single
    Public Property AxesOnly As Boolean
    Public Property ButtonsOnly As Boolean
    Public Property Ready As Boolean
    Public Property WaitingForRelease As Boolean
    Public Property Completed As DrivingBinding
    Public ReadOnly Property Detected As DrivingBinding
        Get
            Return candidate
        End Get
    End Property
    Public Property Travel As Integer
    Public Sub New(value As DrivingInput.Device, actionName As String)
        device = value : action = actionName
    End Sub
    Private Shared Function Clone(value As DrivingInput.Sample) As DrivingInput.Sample
        value.Values = CType(value.Values.Clone(), Single())
        value.Buttons = CType(value.Buttons.Clone(), Byte())
        value.Pov = CType(value.Pov.Clone(), UInteger())
        Return value
    End Function
    Public Sub Reset()
        initialized = False : Ready = False : WaitingForRelease = False : Completed = Nothing
        candidate = Nothing : releasedSince = -1 : Travel = 0 : activeAxis = -1 : activeButton = -1 : peak = 0
    End Sub
    Public Sub Update(current As DrivingInput.Sample, now As Long)
        If current.Connected = 0 Then Reset() : Return
        If Completed IsNot Nothing Then Return
        If Not initialized Then
            baseline = Clone(current) : stableSince = now : initialized = True : Return
        End If
        If Not Ready Then
            Dim changed = current.Axes <> baseline.Axes OrElse (Not AxesOnly AndAlso Not current.Buttons.SequenceEqual(baseline.Buttons))
            If Not ButtonsOnly Then
                For i = 0 To 7
                    If (current.Axes And (1UI << i)) <> 0 AndAlso Math.Abs(current.Values(i) - baseline.Values(i)) > 0.06F Then changed = True
                    If device.Name = "win_xinput" AndAlso i < 6 AndAlso Math.Abs(current.Values(i)) > If(i < 4, 0.25F, 0.15F) Then changed = True
                Next
            End If
            If changed Then baseline = Clone(current) : stableSince = now
            Ready = now - stableSince >= 600
            Return
        End If
        ' A button already held at rest is ignored until it has been released.
        For i = 0 To baseline.Buttons.Length - 1
            If current.Buttons(i) = 0 Then baseline.Buttons(i) = 0
        Next
        If Not WaitingForRelease Then
            Dim detected = DrivingInput.Detect(action, device, baseline, current, AxesOnly, ButtonsOnly)
            If detected Is Nothing Then candidate = Nothing : Return
            If candidate Is Nothing OrElse candidate.Input <> detected.Input OrElse candidate.Calibration <> detected.Calibration Then
                candidate = detected : candidateSince = now
            End If
            Dim analog = candidate.Input.Contains("axis", StringComparison.Ordinal) OrElse candidate.Input.Contains("analog", StringComparison.Ordinal) OrElse candidate.Input.EndsWith("Trigger", StringComparison.Ordinal)
            If analog AndAlso now - candidateSince < 100 Then Return
            WaitingForRelease = True
            For i = 0 To 7
                If (current.Axes And baseline.Axes And (1UI << i)) <> 0 AndAlso Math.Abs(current.Values(i) - baseline.Values(i)) > peak Then
                    peak = Math.Abs(current.Values(i) - baseline.Values(i)) : activeAxis = i
                End If
            Next
            If Not analog Then
                activeAxis = -1
                For i = 0 To current.Buttons.Length - 1
                    If current.Buttons(i) = 0 OrElse baseline.Buttons(i) <> 0 Then Continue For
                    Dim isolated = Clone(current)
                    Array.Clear(isolated.Buttons) : isolated.Buttons(i) = 1
                    Dim probe = DrivingInput.Detect(action, device, baseline, isolated, False, True)
                    If probe IsNot Nothing AndAlso probe.Input = candidate.Input Then activeButton = i : Exit For
                Next
            End If
        End If
        Dim released As Boolean
        If activeAxis >= 0 Then
            Dim displacement = Math.Abs(current.Values(activeAxis) - baseline.Values(activeAxis))
            peak = Math.Max(peak, displacement)
            Travel = CInt(Math.Clamp(displacement / Math.Max(peak, 0.4F) * 100, 0, 100))
            released = displacement < 0.12F
        Else
            ' Only the chosen button matters; unrelated held switches must not block capture.
            released = activeButton >= 0 AndAlso current.Buttons(activeButton) = 0
            Travel = If(released, 0, 100)
        End If
        If Not released Then releasedSince = -1 : Return
        If releasedSince < 0 Then releasedSince = now
        If now - releasedSince >= 160 Then Completed = candidate
    End Sub
End Class
