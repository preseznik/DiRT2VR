Imports System.Text

' This helper is used only while the capture dialog is open, never by the game.
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
    Public Shared Function Detect(action As String, device As Device, baseline As Sample, current As Sample, Optional axesOnly As Boolean = False, Optional buttonsOnly As Boolean = False) As DrivingBinding
        If baseline.Connected = 0 OrElse current.Connected = 0 Then Return Nothing
        Dim binding As New DrivingBinding With {.Action = action, .DeviceId = device.Id, .Device = device.Name}
        ' Prefer analog travel for steering/pedals: some devices also emit a button at the threshold.
        If Not buttonsOnly Then
            Dim analog = DetectAxis(action, device, baseline, current)
            If analog IsNot Nothing Then Return analog
        End If
        If axesOnly Then Return Nothing
        For i = 0 To current.Buttons.Length - 1
            If current.Buttons(i) = 0 OrElse baseline.Buttons(i) <> 0 Then Continue For
            If device.Name = "win_xinput" Then
                Dim names = {"DPadUp", "DPadDown", "DPadLeft", "DPadRight", "Start", "Back", "LeftStick", "RightStick", "LeftShoulder", "RightShoulder", "", "", "A", "B", "X", "Y"}
                If i >= names.Length OrElse names(i) = "" Then Continue For
                binding.Input = "win_con_xi_button" & names(i)
            Else
                binding.Input = "win_con_di_button" & i.ToString()
            End If
            Return binding
        Next
        Return Nothing
    End Function
    Private Shared Function DetectAxis(action As String, device As Device, baseline As Sample, current As Sample) As DrivingBinding
        Dim binding As New DrivingBinding With {.Action = action, .DeviceId = device.Id, .Device = device.Name}
        Dim best = -1, movement As Single = 0.4F
        For i = 0 To 7
            If (current.Axes And baseline.Axes And (1UI << i)) = 0 Then Continue For
            Dim delta = Math.Abs(current.Values(i) - baseline.Values(i))
            If delta > movement Then best = i : movement = delta
        Next
        If best < 0 Then Return Nothing
        Dim negative = current.Values(best) < baseline.Values(best)
        Dim centered = action.StartsWith("Steer ", StringComparison.Ordinal) OrElse Math.Abs(baseline.Values(best)) < 0.35F
        If device.Name = "win_xinput" Then
            Dim names = {"analogLeftStickX", "analogLeftStickY", "analogRightStickX", "analogRightStickY", "buttonLeftTrigger", "buttonRightTrigger"}
            If best >= names.Length Then Return Nothing
            binding.Input = "win_con_xi_" & names(best)
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
            binding.Input = "win_con_di_axis" & {"X", "Y", "Z", "Rx", "Ry", "Rz", "Slider0", "Slider1"}(best)
        End If
        binding.Calibration = If(centered, If(negative, "biDirectionalLower", "biDirectionalUpper"), If(negative, "uniDirectionalNegative", "uniDirectionalPositive"))
        binding.DeadZone = If(device.Name = "win_xinput" AndAlso best < 4, 0.2D, 0.03D)
        Return binding
    End Function
    Public Shared Function Description(binding As DrivingBinding) As String
        If binding Is Nothing Then Return "Use game binding"
        If binding.Keyboard Then Return binding.Input.Replace("win_key_", "").ToUpperInvariant()
        Dim control = binding.Input.Replace("win_con_xi_", "").Replace("win_con_di_", "")
        control = control.Replace("analogLeftStick", "Left stick ").Replace("analogRightStick", "Right stick ").Replace("button", "Button ").Replace("axis", "Axis ")
        Dim direction = If(binding.Calibration = "biDirectionalLower", " (−)", If(binding.Calibration = "biDirectionalUpper", " (+)", If(binding.Calibration = "uniDirectionalNegative", " (inverted)", "")))
        Return If(binding.Device = "win_xinput", "Xbox", binding.Device) & " · " & control & direction
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        If handle <> IntPtr.Zero Then closer(handle) : handle = IntPtr.Zero
        If library <> IntPtr.Zero Then NativeLibrary.Free(library) : library = IntPtr.Zero
    End Sub
End Class

