Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Windows.Forms
Imports System.Xml.Linq

Public Class DrivingBinding
    Public Property Action As String = ""
    Public Property DeviceId As String = ""
    Public Property Device As String = ""
    Public Property Input As String = ""
    Public Property Calibration As String = "uniDirectionalPositive"
    Public Property DeadZone As Decimal = 0D
    Public Property Saturation As Decimal = 1D
    Public ReadOnly Property Keyboard As Boolean
        Get
            Return Device = "Keyboard"
        End Get
    End Property
End Class

Public Class DrivingControls
    Public Property Version As Integer = 1
    Public Property Enabled As Boolean
    Public Property Bindings As New List(Of DrivingBinding)
    Public Shared ReadOnly Actions As String() = {"Steer Left", "Steer Right", "Accelerate", "Brake", "Clutch", "Hand Brake", "Gear Up", "Gear Down", "Gear 1", "Gear 2", "Gear 3", "Gear 4", "Gear 5", "Gear 6", "Gear Reverse", "Change View", "Look Back", "Horn"}
    Public Shared ReadOnly Calibrations As String() = {"uniDirectionalPositive", "uniDirectionalNegative", "biDirectionalLower", "biDirectionalUpper"}
    Public Sub Validate()
        If Version <> 1 OrElse Bindings Is Nothing OrElse Bindings.Count > Actions.Length * 2 Then Throw New IOException("Invalid driving-controls configuration.")
        Dim slots As New HashSet(Of String)
        For Each binding In Bindings
            If binding Is Nothing OrElse Not Actions.Contains(binding.Action) OrElse Not Calibrations.Contains(binding.Calibration) OrElse
                String.IsNullOrWhiteSpace(binding.Device) OrElse binding.Device.Length > 260 OrElse binding.Device.Any(Function(c) Char.IsControl(c)) OrElse
                String.IsNullOrWhiteSpace(binding.DeviceId) OrElse binding.DeviceId.Length > 128 OrElse
                binding.Input Is Nothing OrElse Not Regex.IsMatch(binding.Input, "\Awin_(key|con_di|con_xi)_[A-Za-z0-9]+\z") OrElse
                binding.DeadZone < 0 OrElse binding.DeadZone > 0.9D OrElse binding.Saturation < 0.1D OrElse binding.Saturation > 1D OrElse binding.DeadZone >= binding.Saturation Then
                Throw New IOException("Invalid driving binding. Check device, input and calibration.")
            End If
            If binding.Keyboard <> binding.Input.StartsWith("win_key_", StringComparison.Ordinal) OrElse
                (binding.Device = "win_xinput") <> binding.Input.StartsWith("win_con_xi_", StringComparison.Ordinal) Then Throw New IOException("Driving input does not match its device.")
            If Not slots.Add(binding.Action & ":" & binding.Keyboard.ToString()) Then Throw New IOException("Duplicate driving binding for " & binding.Action & ".")
        Next
    End Sub
    Public Shared Function Load(context As InstallContext) As DrivingControls
        Dim path = IO.Path.Combine(context.UserRoot, "driving-controls.json")
        Dim result = If(File.Exists(path), Files.ReadJson(Of DrivingControls)(path), New DrivingControls())
        If result Is Nothing Then Throw New IOException("Invalid driving-controls file.")
        result.Validate() : Return result
    End Function
    Public Sub Save(context As InstallContext)
        Validate() : Files.SaveJson(IO.Path.Combine(context.UserRoot, "driving-controls.json"), Me)
    End Sub
    Public Function ToXml() As Byte()
        Validate()
        Dim root As New XElement("ActionMap")
        For Each group In Bindings.GroupBy(Function(b) b.Action)
            Dim action As New XElement("Action", New XAttribute("actionName", group.Key))
            For Each binding In group
                action.Add(New XElement("Axis", New XAttribute("deviceName", binding.Device), New XAttribute("axisName", binding.Input),
                    New XAttribute("baseCalibration", binding.Calibration), New XAttribute("deadZone", binding.DeadZone.ToString(CultureInfo.InvariantCulture)),
                    New XAttribute("saturation", binding.Saturation.ToString(CultureInfo.InvariantCulture))))
            Next
            root.Add(action)
        Next
        Return New UTF8Encoding(False).GetBytes(root.ToString())
    End Function
    Public Sub ConfigureProcess(context As InstallContext, start As ProcessStartInfo, vr As Boolean)
        start.Environment.Remove("DIRT2VR_DRIVING_CONTROLS")
        If Not Enabled OrElse Bindings.Count = 0 Then Return
        Validate()
        If Not vr Then
            If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before using launcher driving bindings.")
            Dim doc = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
            If doc.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value <> "false" Then Throw New IOException("Launcher driving bindings require DiRT 2's DX11 renderer. Disable launcher bindings to use the game's saved controls in DX9.")
            start.Environment("DIRT2VR_ACTIVE") = "1"
            start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
        End If
        Dim path = IO.Path.Combine(context.UserRoot, "driving-controls.xml")
        Files.AtomicWrite(path, ToXml())
        start.Environment("DIRT2VR_DRIVING_CONTROLS") = path
    End Sub
    Public Shared Function KeyInput(key As Keys) As String
        If key >= Keys.A AndAlso key <= Keys.Z Then Return "win_key_" & key.ToString().ToLowerInvariant()
        If key >= Keys.D0 AndAlso key <= Keys.D9 Then Return "win_key_" & (CInt(key) - CInt(Keys.D0)).ToString()
        If key >= Keys.F1 AndAlso key <= Keys.F12 Then Return "win_key_" & key.ToString().ToLowerInvariant()
        If key >= Keys.NumPad0 AndAlso key <= Keys.NumPad9 Then Return "win_key_numpad" & (CInt(key) - CInt(Keys.NumPad0)).ToString()
        Dim names As New Dictionary(Of Keys, String) From {{Keys.Space, "space"}, {Keys.Up, "up"}, {Keys.Down, "down"}, {Keys.Left, "left"}, {Keys.Right, "right"}, {Keys.Return, "return"}, {Keys.Tab, "tab"}, {Keys.Home, "home"}, {Keys.End, "end"}, {Keys.Delete, "delete"}, {Keys.Insert, "insert"}, {Keys.PageUp, "prior"}, {Keys.PageDown, "next"}, {Keys.Back, "back"}, {Keys.LShiftKey, "lShift"}, {Keys.RShiftKey, "rShift"}, {Keys.LControlKey, "lControl"}, {Keys.RControlKey, "rControl"}}
        Return If(names.ContainsKey(key), "win_key_" & names.GetValueOrDefault(key), Nothing)
    End Function
End Class
