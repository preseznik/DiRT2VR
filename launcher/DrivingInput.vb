Imports System.Text

' Nonexclusive launcher-side input, shared by driving capture and VR shortcuts.
Public Class DrivingInput
    Implements IDisposable
    <StructLayout(LayoutKind.Sequential)>
    Public Structure Sample
        Public Connected As UInteger
        Public Axes As UInteger
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=8)> Public Values As Single()
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=128)> Public Buttons As Byte()
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=4)> Public Pov As UInteger()
    End Structure
    Public Class Device
        Public Property Index As UInteger
        Public Property Id As String
        Public Property Name As String
        Public Overrides Function ToString() As String
            Return If(Name = "win_xinput", "Xbox controller " & (Integer.Parse(Id.Substring(7), Globalization.CultureInfo.InvariantCulture) + 1).ToString(), Name)
        End Function
    End Class
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)> Private Delegate Function OpenFn(window As IntPtr) As IntPtr
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)> Private Delegate Sub CloseFn(handle As IntPtr)
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)> Private Delegate Function CountFn(handle As IntPtr) As UInteger
    <UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet:=CharSet.Unicode)> Private Delegate Function NameFn(handle As IntPtr, index As UInteger, id As StringBuilder, idSize As UInteger, name As StringBuilder, nameSize As UInteger) As Integer
    <UnmanagedFunctionPointer(CallingConvention.Cdecl)> Private Delegate Function ReadFn(handle As IntPtr, index As UInteger, ByRef state As Sample) As Integer
    Private library As IntPtr
    Private handle As IntPtr
    Private closer As CloseFn
    Private reader As ReadFn
    Public ReadOnly Devices As New List(Of Device)
    Private Function Export(Of T As Class)(name As String) As T
        Return DirectCast(CObj(Marshal.GetDelegateForFunctionPointer(NativeLibrary.GetExport(library, name), GetType(T))), T)
    End Function
    Public Sub New(context As InstallContext, window As IntPtr)
        Const relative As String = "DiRT2VR/payload/driving_input.dll"
        Dim path = IO.Path.Combine(context.GameRoot, relative)
        Dim manifest = Files.ReadJson(Of PackageManifest)(IO.Path.Combine(context.ModRoot, "package.json"))
        Dim expected As String = Nothing
        Files.NoLinks(path)
        If manifest?.Files Is Nothing OrElse Not manifest.Files.TryGetValue(relative, expected) OrElse Not File.Exists(path) OrElse Files.Hash(path) <> expected Then
            Throw New IOException("Driving-input capture is missing or damaged. Install the complete updated launcher package.")
        End If
        Try
            library = NativeLibrary.Load(path)
            closer = Export(Of CloseFn)("CaptureClose") : reader = Export(Of ReadFn)("CaptureRead")
            handle = Export(Of OpenFn)("CaptureOpen")(window)
            If handle = IntPtr.Zero Then Throw New IOException("Windows controller capture could not start.")
            Dim count = Export(Of CountFn)("CaptureCount")(handle), getName = Export(Of NameFn)("CaptureName")
            If count > 256 Then Throw New IOException("Unexpected controller count.")
            For index = 0 To CInt(count) - 1
                Dim id As New StringBuilder(128), name As New StringBuilder(512)
                If getName(handle, CUInt(index), id, 128, name, 512) <> 0 Then Devices.Add(New Device With {.Index = CUInt(index), .Id = id.ToString(), .Name = name.ToString()})
            Next
        Catch
            Dispose() : Throw
        End Try
    End Sub
    Public Function Read(device As Device) As Sample
        Dim state As New Sample
        reader(handle, device.Index, state)
        Return state
    End Function
    Private Shared ReadOnly XboxButtons As String() = {"DPadUp", "DPadDown", "DPadLeft", "DPadRight", "Start", "Back", "LeftStick", "RightStick", "LeftShoulder", "RightShoulder", "", "", "A", "B", "X", "Y"}
    Private Shared ReadOnly XboxAxes As String() = {"analogLeftStickX", "analogLeftStickY", "analogRightStickX", "analogRightStickY", "buttonLeftTrigger", "buttonRightTrigger"}
    Private Shared ReadOnly DirectAxes As String() = {"X", "Y", "Z", "Rx", "Ry", "Rz", "Slider0", "Slider1"}
    ' Wheel D-pads are POV hats; the game's own wheel maps name them DPad0 axes (ProJict 8634717e).
    Private Shared ReadOnly DPadDirections As String() = {"up", "right", "down", "left"}
    Public Const DPadInput As String = "win_con_di_axisDPad0"
    Public Shared Function PovCentered(value As UInteger) As Boolean
        Return (value And &HFFFFUI) = &HFFFFUI OrElse value >= 36000UI
    End Function
    ' A D-pad press to bind: within 30° of up, right, down or left, never a diagonal; -1 otherwise.
    Public Shared Function PovDirection(value As UInteger) As Integer
        If PovCentered(value) Then Return -1
        Dim nearest = Math.Round(value / 9000.0)
        Return If(Math.Abs(value - nearest * 9000) <= 3000, CInt(nearest) Mod 4, -1)
    End Function
    ' Whether the hat points into a direction's quarter, diagonals included, as the game reads it.
    Public Shared Function DPadPressed(direction As String, pov As UInteger) As Boolean
        Dim index = Array.IndexOf(DPadDirections, direction)
        If index < 0 OrElse PovCentered(pov) Then Return False
        Dim offset = (CLng(pov) - index * 9000L + 36000L) Mod 36000L
        Return offset <= 4500L OrElse offset >= 31500L
    End Function
    ' Whether a controller binding's button, D-pad direction or axis is pressed in this sample (ProJict b7bcb77b).
    Public Shared Function IsPressed(binding As DrivingBinding, device As Device, sample As Sample) As Boolean
        If binding Is Nothing OrElse binding.Keyboard OrElse device Is Nothing OrElse binding.DeviceId <> device.Id OrElse sample.Connected = 0 Then Return False
        Dim xbox = device.Name = "win_xinput", prefix = If(xbox, "win_con_xi_", "win_con_di_")
        If Not binding.Input.StartsWith(prefix, StringComparison.Ordinal) Then Return False
        Dim control = binding.Input.Substring(prefix.Length)
        If Not xbox AndAlso binding.Input.StartsWith(DPadInput, StringComparison.Ordinal) Then
            Return sample.Pov IsNot Nothing AndAlso sample.Pov.Length > 0 AndAlso DPadPressed(binding.Input.Substring(DPadInput.Length), sample.Pov(0))
        End If
        Dim button = -1
        If xbox AndAlso control.StartsWith("button", StringComparison.Ordinal) Then button = Array.IndexOf(XboxButtons, control.Substring(6))
        If Not xbox AndAlso control.StartsWith("button", StringComparison.Ordinal) AndAlso Not Integer.TryParse(control.Substring(6), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, button) Then button = -1
        If button >= 0 Then Return sample.Buttons IsNot Nothing AndAlso button < sample.Buttons.Length AndAlso sample.Buttons(button) <> 0
        Dim axis = If(xbox, Array.IndexOf(XboxAxes, control), If(control.StartsWith("axis", StringComparison.Ordinal), Array.IndexOf(DirectAxes, control.Substring(4)), -1))
        If axis < 0 OrElse (sample.Axes And (1UI << axis)) = 0 OrElse sample.Values Is Nothing OrElse axis >= sample.Values.Length Then Return False
        Dim value = sample.Values(axis)
        ' DiRT's stick Y is down-positive; XInput's is up-positive.
        If xbox AndAlso (axis = 1 OrElse axis = 3) Then value = -value
        Select Case binding.Calibration
            Case "biDirectionalLower" : Return value < -0.5F
            Case "biDirectionalUpper" : Return value > 0.5F
            Case "uniDirectionalNegative" : Return value < -0.25F
            Case Else : Return value > If(xbox, 0.5F, 0.25F)
        End Select
    End Function
    ' Actions normally bound to a button or the D-pad: those win over an axis that
    ' happens to move with a firm press (ProJict 8634717e).
    Private Shared ReadOnly DigitalActions As New HashSet(Of String) From {
        "Gear Up", "Gear Down", "Gear 1", "Gear 2", "Gear 3", "Gear 4", "Gear 5", "Gear 6", "Gear Reverse", "Change View", "Look Back", "Horn", "Pause",
        "Menu Start Button", "Menu Select", "Menu Back", "Menu Up", "Menu Down", "Menu Left", "Menu Right"}
    ' povReady reads the D-pad even while axesOnly waits for the buttons to settle.
    Public Shared Function Detect(action As String, device As Device, baseline As Sample, current As Sample, Optional axesOnly As Boolean = False, Optional buttonsOnly As Boolean = False, Optional povReady As Boolean = False) As DrivingBinding
        If baseline.Connected = 0 OrElse current.Connected = 0 Then Return Nothing
        Dim binding As New DrivingBinding With {.Action = action, .DeviceId = device.Id, .Device = device.Name}
        Dim digital = DigitalActions.Contains(action)
        ' Prefer analog travel for steering/pedals: some devices also emit a button at the threshold.
        If Not buttonsOnly AndAlso Not digital Then
            Dim analog = DetectAxis(action, device, baseline, current)
            If analog IsNot Nothing Then Return analog
        End If
        If Not axesOnly Then
            For i = 0 To current.Buttons.Length - 1
                If current.Buttons(i) = 0 OrElse baseline.Buttons(i) <> 0 Then Continue For
                If device.Name = "win_xinput" Then
                    If i >= XboxButtons.Length OrElse XboxButtons(i) = "" Then Continue For
                    binding.Input = "win_con_xi_button" & XboxButtons(i)
                Else
                    binding.Input = "win_con_di_button" & i.ToString()
                End If
                Return binding
            Next
        End If
        If (Not axesOnly OrElse povReady) AndAlso device.Name <> "win_xinput" AndAlso current.Pov IsNot Nothing AndAlso baseline.Pov IsNot Nothing AndAlso current.Pov.Length > 0 AndAlso baseline.Pov.Length > 0 Then
            Dim direction = PovDirection(current.Pov(0))
            If direction >= 0 AndAlso PovCentered(baseline.Pov(0)) Then
                binding.Input = DPadInput & DPadDirections(direction)
                Return binding
            End If
        End If
        If Not buttonsOnly AndAlso digital Then Return DetectAxis(action, device, baseline, current)
        Return Nothing
    End Function
    Private Shared Function DetectAxis(action As String, device As Device, baseline As Sample, current As Sample) As DrivingBinding
        Dim binding As New DrivingBinding With {.Action = action, .DeviceId = device.Id, .Device = device.Name}
        Dim best = -1, movement As Single = 0
        Dim steering = action.StartsWith("Steer ", StringComparison.Ordinal)
        For i = 0 To 7
            If (current.Axes And baseline.Axes And (1UI << i)) = 0 Then Continue For
            Dim delta = Math.Abs(current.Values(i) - baseline.Values(i))
            Dim centeredAxis = Math.Abs(baseline.Values(i)) < 0.35F
            If steering AndAlso (Not centeredAxis OrElse (device.Name = "win_xinput" AndAlso i >= 4)) Then Continue For
            ' Wheel rotation needs less travel than a pedal. End-resting DI pedals
            ' span two normalized units; require almost half their full travel.
            Dim threshold = If(steering AndAlso device.Name <> "win_xinput", 0.15F,
                               If(centeredAxis OrElse device.Name = "win_xinput", 0.55F, 0.9F))
            If delta >= threshold AndAlso delta > movement Then best = i : movement = delta
        Next
        If best < 0 Then Return Nothing
        Dim negative = current.Values(best) < baseline.Values(best)
        If steering Then
            If Math.Abs(current.Values(best)) < 0.15F Then Return Nothing
            negative = current.Values(best) < 0
        End If
        Dim centered = action.StartsWith("Steer ", StringComparison.Ordinal) OrElse Math.Abs(baseline.Values(best)) < 0.35F
        If device.Name = "win_xinput" Then
            If best >= XboxAxes.Length Then Return Nothing
            binding.Input = "win_con_xi_" & XboxAxes(best)
            ' XInput has a known zero, unlike wheel/pedal axes. Never bind trigger release as inverted.
            If best >= 4 Then
                If current.Values(best) < 0.4F OrElse baseline.Values(best) > 0.15F Then Return Nothing
                centered = False : negative = False
            Else
                If Math.Abs(baseline.Values(best)) > 0.25F Then Return Nothing
                negative = current.Values(best) < 0
                ' DiRT's Y convention is down-positive (its stock Look Up binding uses Lower).
                If best = 1 OrElse best = 3 Then negative = Not negative
                centered = True
            End If
        Else
            binding.Input = "win_con_di_axis" & DirectAxes(best)
        End If
        binding.Calibration = If(centered, If(negative, "biDirectionalLower", "biDirectionalUpper"), If(negative, "uniDirectionalNegative", "uniDirectionalPositive"))
        ' Wheels, pedals and Xbox triggers start without a dead zone (ProJict b7dd76c8);
        ' Xbox thumbsticks keep 20% because a resting stick drifts.
        binding.DeadZone = If(device.Name = "win_xinput" AndAlso best < 4, 0.2D, 0D)
        Return binding
    End Function
    Public Shared Function Description(binding As DrivingBinding) As String
        If binding Is Nothing Then Return "Use game binding"
        If binding.Keyboard Then Return binding.Input.Replace("win_key_", "").ToUpperInvariant()
        Dim control = binding.Input.Replace("win_con_xi_", "").Replace("win_con_di_", "")
        control = control.Replace("analogLeftStick", "Left stick ").Replace("analogRightStick", "Right stick ").Replace("button", "Button ").Replace("axis", "Axis ")
        If binding.Input.StartsWith(DPadInput, StringComparison.Ordinal) Then control = "D-pad " & binding.Input.Substring(DPadInput.Length)
        Dim direction = If(binding.Calibration = "biDirectionalLower", " (−)", If(binding.Calibration = "biDirectionalUpper", " (+)", If(binding.Calibration = "uniDirectionalNegative", " (inverted)", "")))
        Return If(binding.Device = "win_xinput", "Xbox", binding.Device) & " · " & control & direction
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        If handle <> IntPtr.Zero Then closer(handle) : handle = IntPtr.Zero
        If library <> IntPtr.Zero Then NativeLibrary.Free(library) : library = IntPtr.Zero
    End Sub
End Class

