Imports System.Windows.Forms

Public Module ControllerNames
    Public ReadOnly XButtons As Integer() = {1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 4096, 8192, 16384, 32768}
    Private ReadOnly Labels As String() = {"D-pad up", "D-pad down", "D-pad left", "D-pad right", "Start", "Back", "Left stick", "Right stick", "LB", "RB", "A", "B", "X", "Y"}
    Public Function ButtonName(source As String, button As Integer) As String
        Dim index = Array.IndexOf(XButtons, button)
        Return If(source = "xinput" AndAlso index >= 0, If(index >= 0, Labels(Math.Max(0, index)), ""), "Button " & button.ToString())
    End Function
End Module
Public Class ControllerSample
    Public Property Source As String = ""
    Public Property Device As String = ""
    Public Property Label As String = ""
    Public Property Connected As Boolean = True
    Public Property Buttons As New HashSet(Of Integer)
End Class
Public Class ControllerCapture
    Private ReadOnly blocked As New Dictionary(Of String, HashSet(Of Integer))
    Private ReadOnly preferWheels As Boolean
    Private selected As String
    Private buttons As New HashSet(Of Integer)
    Public Sub New(snapshot As IEnumerable(Of ControllerSample))
        preferWheels = snapshot.Any(Function(s) s.Source = "dinput" AndAlso s.Connected)
        For Each sample In snapshot.Where(Function(s) s.Connected)
            blocked(sample.Source & ":" & sample.Device) = New HashSet(Of Integer)(sample.Buttons)
        Next
    End Sub
    Public Function Update(sample As ControllerSample, action As Integer) As ControllerBinding
        If preferWheels AndAlso sample.Source = "hid" Then Return Nothing
        Dim key = sample.Source & ":" & sample.Device
        If Not sample.Connected Then
            blocked.Remove(key)
            If selected = key Then selected = Nothing : buttons.Clear()
            Return Nothing
        End If
        If Not blocked.ContainsKey(key) Then
            blocked(key) = New HashSet(Of Integer)(sample.Buttons)
            Return Nothing ' A newly connected device must release first.
        End If
        blocked(key).IntersectWith(sample.Buttons)
        Dim pressed = sample.Buttons.Except(blocked(key)).ToHashSet()
        If selected Is Nothing AndAlso pressed.Count > 0 Then selected = key
        If selected <> key Then Return Nothing
        buttons.UnionWith(pressed)
        If buttons.Any(Function(b) sample.Buttons.Contains(b)) Then Return Nothing
        Dim result As New ControllerBinding With {.Action = action, .Source = sample.Source, .Device = sample.Device, .Label = sample.Label, .Buttons = buttons.Order().ToList()}
        selected = Nothing : buttons.Clear()
        Return result
    End Function
End Class
Public Class BindingMachine
    Implements IDisposable
    Private ReadOnly bindings As List(Of ControllerBinding)
    Private ReadOnly armed As New Dictionary(Of Integer, Boolean)
    Public Sub New(value As List(Of ControllerBinding))
        bindings = value
    End Sub
    Public Function Update(sample As ControllerSample) As IEnumerable(Of Integer)
        Dim actions As New HashSet(Of Integer)
        For i = 0 To bindings.Count - 1
            Dim binding = bindings(i)
            If binding.Source <> sample.Source OrElse binding.Device <> sample.Device Then Continue For
            If Not sample.Connected Then
                armed.Remove(i) : Continue For
            End If
            Dim neutral = Not binding.Buttons.Any(Function(b) sample.Buttons.Contains(b))
            If Not armed.ContainsKey(i) Then
                armed(i) = neutral : Continue For
            End If
            If neutral Then armed(i) = True
            If armed(i) AndAlso binding.Buttons.All(Function(b) sample.Buttons.Contains(b)) Then
                armed(i) = False : actions.Add(binding.Action)
            End If
        Next
        Return actions
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        armed.Clear()
    End Sub
End Class

Public Class ControllerInput
    Inherits NativeWindow
    Implements IDisposable
    <StructLayout(LayoutKind.Sequential)> Private Structure XGamepad
        Public Buttons As UShort
        Public LeftTrigger As Byte
        Public RightTrigger As Byte
        Public LX As Short
        Public LY As Short
        Public RX As Short
        Public RY As Short
    End Structure
    <StructLayout(LayoutKind.Sequential)> Private Structure XState
        Public Packet As UInteger
        Public Pad As XGamepad
    End Structure
    <StructLayout(LayoutKind.Sequential)> Private Structure RawDevice
        Public Page As UShort
        Public Usage As UShort
        Public Flags As UInteger
        Public Window As IntPtr
    End Structure
    <DllImport("xinput1_4.dll")> Private Shared Function XInputGetState(index As UInteger, ByRef state As XState) As UInteger
    End Function
    <DllImport("user32.dll", SetLastError:=True)> Private Shared Function RegisterRawInputDevices(devices As RawDevice(), count As UInteger, size As UInteger) As Boolean
    End Function
    <DllImport("user32.dll", SetLastError:=True)> Private Shared Function GetRawInputData(input As IntPtr, command As UInteger, data As IntPtr, ByRef size As UInteger, headerSize As UInteger) As UInteger
    End Function
    <DllImport("user32.dll", CharSet:=CharSet.Unicode)> Private Shared Function GetRawInputDeviceInfo(device As IntPtr, command As UInteger, data As IntPtr, ByRef size As UInteger) As UInteger
    End Function
    <DllImport("hid.dll")> Private Shared Function HidP_MaxUsageListLength(reportType As Integer, page As UShort, preparsed As IntPtr) As UInteger
    End Function
    <DllImport("hid.dll")> Private Shared Function HidP_GetUsagesEx(reportType As Integer, link As UShort, usages As IntPtr, ByRef count As UInteger, preparsed As IntPtr, report As IntPtr, length As UInteger) As Integer
    End Function
    <DllImport("user32.dll")> Private Shared Function GetForegroundWindow() As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function GetWindowThreadProcessId(window As IntPtr, ByRef processId As UInteger) As UInteger
    End Function
    Private Class HidDevice
        Public Path As String = ""
        Public Preparsed As IntPtr
        Public Reports As New Dictionary(Of Byte, HashSet(Of Integer))
    End Class
    Private ReadOnly hidDevices As New Dictionary(Of IntPtr, HidDevice)
    Private ReadOnly lastX As New Dictionary(Of Integer, String)
    Private ReadOnly samples As New Dictionary(Of String, ControllerSample)
    Private ReadOnly context As InstallContext
    Private driving As DrivingInput
    Private refreshAt As Long
    Public Event StateChanged(sample As ControllerSample)
    Public Property LastError As String = ""
    Public Sub New(Optional installation As InstallContext = Nothing)
        context = installation
        ' DirectInput requires a top-level window; keep this helper window hidden.
        CreateHandle(New CreateParams With {.Caption = "DiRT2VR controller input"})
        Dim devices = {New RawDevice With {.Page = 1, .Usage = 4, .Flags = &H2100UI, .Window = Handle}, New RawDevice With {.Page = 1, .Usage = 5, .Flags = &H2100UI, .Window = Handle}, New RawDevice With {.Page = 1, .Usage = 8, .Flags = &H2100UI, .Window = Handle}}
        If Not RegisterRawInputDevices(devices, CUInt(devices.Length), CUInt(Marshal.SizeOf(Of RawDevice)())) Then LastError = "HID registration failed: " & Marshal.GetLastWin32Error().ToString()
    End Sub
    Public Function Snapshot() As IEnumerable(Of ControllerSample)
        Return samples.Values.ToArray()
    End Function
    Public Shared Function GameFocused() As Boolean
        Dim id As UInteger
        GetWindowThreadProcessId(GetForegroundWindow(), id)
        Try
            Using owner = Process.GetProcessById(CInt(id))
                Return owner.ProcessName.Equals("dirt2_game", StringComparison.OrdinalIgnoreCase)
            End Using
        Catch
            Return False
        End Try
    End Function
    Private Sub Emit(sample As ControllerSample)
        samples(sample.Source & ":" & sample.Device) = sample
        RaiseEvent StateChanged(sample)
    End Sub
    Public Sub Poll()
        If context IsNot Nothing Then PollWheels()
        For index = 0 To 3
            Dim state As New XState
            Dim connected = XInputGetState(CUInt(index), state) = 0
            Dim signature = connected.ToString() & ":" & state.Pad.Buttons.ToString()
            If lastX.ContainsKey(index) AndAlso lastX(index) = signature Then Continue For
            lastX(index) = signature
            Emit(New ControllerSample With {.Source = "xinput", .Device = index.ToString(), .Label = "Xbox controller " & (index + 1).ToString(), .Connected = connected, .Buttons = New HashSet(Of Integer)(ControllerNames.XButtons.Where(Function(b) (CInt(state.Pad.Buttons) And b) <> 0))})
        Next
    End Sub
    Private Sub PollWheels()
        Try
            If refreshAt <> 0 AndAlso Environment.TickCount64 < refreshAt Then Return
            If driving Is Nothing OrElse refreshAt <> 0 Then
                refreshAt = 0
                For Each old In Snapshot().Where(Function(s) s.Source = "dinput" AndAlso s.Connected)
                    Emit(New ControllerSample With {.Source = old.Source, .Device = old.Device, .Label = old.Label, .Connected = False})
                Next
                driving?.Dispose()
                driving = Nothing
                driving = New DrivingInput(context, Handle)
            End If
            For Each device In driving.Devices.Where(Function(d) d.Name <> "win_xinput")
                Dim state = driving.Read(device)
                Dim sample As New ControllerSample With {.Source = "dinput", .Device = device.Id, .Label = device.ToString(), .Connected = state.Connected <> 0}
                If sample.Connected Then
                    For i = 0 To state.Buttons.Length - 1
                        If state.Buttons(i) <> 0 Then sample.Buttons.Add(i + 1)
                    Next
                End If
                Dim previous As ControllerSample = Nothing
                If Not samples.TryGetValue("dinput:" & device.Id, previous) OrElse previous.Connected <> sample.Connected OrElse Not previous.Buttons.SetEquals(sample.Buttons) Then Emit(sample)
            Next
        Catch ex As Exception
            LastError = ex.Message
            driving?.Dispose() : driving = Nothing
            refreshAt = Environment.TickCount64 + 3000
            For Each old In Snapshot().Where(Function(s) s.Source = "dinput" AndAlso s.Connected)
                Emit(New ControllerSample With {.Source = old.Source, .Device = old.Device, .Label = old.Label, .Connected = False})
            Next
        End Try
    End Sub
    Protected Overrides Sub WndProc(ByRef message As Message)
        Try
            If message.Msg = &HFE Then refreshAt = Environment.TickCount64 + 500
            If message.Msg = &H219 AndAlso {7L, &H8000L, &H8004L}.Contains(message.WParam.ToInt64()) Then refreshAt = Environment.TickCount64 + 500
            If message.Msg = &HFF Then ReadHid(message.LParam)
            If message.Msg = &HFE AndAlso message.WParam.ToInt64() = 2 Then
                Dim device As HidDevice = Nothing
                If hidDevices.TryGetValue(message.LParam, device) Then
                    Emit(New ControllerSample With {.Source = "hid", .Device = device.Path, .Label = HidLabel(device.Path), .Connected = False})
                    Marshal.FreeHGlobal(device.Preparsed) : hidDevices.Remove(message.LParam)
                End If
            End If
        Catch ex As Exception
            LastError = ex.Message
        End Try
        MyBase.WndProc(message)
    End Sub
    Private Shared Function HidLabel(path As String) As String
        Dim match = Text.RegularExpressions.Regex.Match(path, "VID_[0-9A-F]{4}&PID_[0-9A-F]{4}", Text.RegularExpressions.RegexOptions.IgnoreCase)
        Return "USB controller " & If(match.Success, match.Value, "HID")
    End Function
    Private Function DeviceInfo(handle As IntPtr) As HidDevice
        If hidDevices.ContainsKey(handle) Then Return hidDevices(handle)
        Dim size As UInteger
        GetRawInputDeviceInfo(handle, &H20000007UI, IntPtr.Zero, size)
        If size = 0 OrElse size > 32768 Then Return Nothing
        Dim buffer = Marshal.AllocHGlobal(CInt(size + 1) * 2)
        Dim path As String
        Try
            If GetRawInputDeviceInfo(handle, &H20000007UI, buffer, size) = UInteger.MaxValue Then Return Nothing
            path = Marshal.PtrToStringUni(buffer).ToUpperInvariant()
        Finally
            Marshal.FreeHGlobal(buffer)
        End Try
        If path.Contains("IG_") Then Return Nothing ' Xbox is handled once, through XInput.
        size = 0 : GetRawInputDeviceInfo(handle, &H20000005UI, IntPtr.Zero, size)
        If size = 0 OrElse size > 1048576 Then Return Nothing
        buffer = Marshal.AllocHGlobal(CInt(size))
        If GetRawInputDeviceInfo(handle, &H20000005UI, buffer, size) = UInteger.MaxValue Then
            Marshal.FreeHGlobal(buffer) : Return Nothing
        End If
        Dim device As New HidDevice With {.Path = path, .Preparsed = buffer}
        hidDevices.Add(handle, device)
        Return device
    End Function
    Private Sub ReadHid(raw As IntPtr)
        Dim headerSize = CUInt(8 + 2 * IntPtr.Size)
        Dim size As UInteger
        GetRawInputData(raw, &H10000003UI, IntPtr.Zero, size, headerSize)
        If size < headerSize + 8 OrElse size > 1048576 Then Return
        Dim data = Marshal.AllocHGlobal(CInt(size))
        Try
            If GetRawInputData(raw, &H10000003UI, data, size, headerSize) = UInteger.MaxValue OrElse Marshal.ReadInt32(data) <> 2 Then Return
            Dim device = DeviceInfo(Marshal.ReadIntPtr(data, 8))
            If device Is Nothing Then Return
            Dim reportSize = Marshal.ReadInt32(data, CInt(headerSize))
            Dim reportCount = Marshal.ReadInt32(data, CInt(headerSize) + 4)
            If reportSize < 1 OrElse reportCount < 1 OrElse CLng(reportSize) * reportCount > size - headerSize - 8 Then Return
            Dim capacity = HidP_MaxUsageListLength(0, 0, device.Preparsed)
            If capacity = 0 OrElse capacity > 4096 Then Return
            Dim usages = Marshal.AllocHGlobal(CInt(capacity) * 4)
            Try
                For index = 0 To reportCount - 1
                    Dim report = IntPtr.Add(data, CInt(headerSize) + 8 + index * reportSize)
                    Dim count = capacity
                    If HidP_GetUsagesEx(0, 0, usages, count, device.Preparsed, report, CUInt(reportSize)) <> &H110000 Then Continue For
                    Dim buttons As New HashSet(Of Integer)
                    For i = 0 To CInt(count) - 1
                        If (CInt(Marshal.ReadInt16(usages, i * 4 + 2)) And &HFFFF) = 9 Then buttons.Add(CInt(Marshal.ReadInt16(usages, i * 4)) And &HFFFF)
                    Next
                    device.Reports(Marshal.ReadByte(report)) = buttons
                    Emit(New ControllerSample With {.Source = "hid", .Device = device.Path, .Label = HidLabel(device.Path), .Buttons = New HashSet(Of Integer)(device.Reports.Values.SelectMany(Function(b) b))})
                Next
            Finally
                Marshal.FreeHGlobal(usages)
            End Try
        Finally
            Marshal.FreeHGlobal(data)
        End Try
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        driving?.Dispose() : driving = Nothing
        If Handle <> IntPtr.Zero Then DestroyHandle()
        For Each device In hidDevices.Values
            Marshal.FreeHGlobal(device.Preparsed)
        Next
        hidDevices.Clear()
    End Sub
End Class
