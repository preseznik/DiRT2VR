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
    Public Property CombinedManualGears As Boolean
    ' Session-only transmission for your car; "game" keeps the career's saved choice.
    Public Property Transmission As String = "game"
    ' The steering wheel's own rotation lock to lock, as set in its software;
    ' 0 until set with Wheel rotation…. Only used to show and apply saturation.
    Public Property WheelRotation As Integer
    Public Property Bindings As New List(Of DrivingBinding)
    <System.Text.Json.Serialization.JsonIgnore>
    Public ReadOnly Property RequiresNativeHooks As Boolean
        Get
            Return CombinedManualGears OrElse Transmission <> "game" OrElse (Enabled AndAlso Bindings.Count > 0)
        End Get
    End Property
    Public Shared ReadOnly Transmissions As String() = {"game", "automatic", "sequential", "hpattern", "hpattern_clutch"}
    Public Shared ReadOnly TransmissionNames As String() = {"Use game setting", "Automatic", "Manual sequential", "Manual H-pattern", "Manual H-pattern + clutch"}
    Public Shared ReadOnly Actions As String() = {"Steer Left", "Steer Right", "Accelerate", "Brake", "Clutch", "Hand Brake", "Gear Up", "Gear Down", "Gear 1", "Gear 2", "Gear 3", "Gear 4", "Gear 5", "Gear 6", "Gear Reverse", "Change View", "Look Back", "Horn", "Pause", "Menu Start Button", "Menu Select", "Menu Back", "Menu Up", "Menu Down", "Menu Left", "Menu Right"}
    Public Shared ReadOnly Calibrations As String() = {"uniDirectionalPositive", "uniDirectionalNegative", "biDirectionalLower", "biDirectionalUpper"}
    Public Sub Validate()
        If Version <> 1 OrElse Bindings Is Nothing OrElse Bindings.Count > (Actions.Length + ReplayControls.ReplayActions.Length) * 2 Then Throw New IOException("Invalid driving-controls configuration.")
        If Not Transmissions.Contains(Transmission) Then Throw New IOException("Invalid transmission setting.")
        If WheelRotation <> 0 AndAlso (WheelRotation < MinimumWheelRotation OrElse WheelRotation > MaximumWheelRotation) Then Throw New IOException("Invalid wheel rotation.")
        Dim slots As New HashSet(Of String)
        For Each binding In Bindings
            If binding Is Nothing OrElse Not Actions.Concat(ReplayControls.ReplayActions).Contains(binding.Action) OrElse Not Calibrations.Contains(binding.Calibration) OrElse
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
        Validate()
        If Enabled AndAlso Problems().Count > 0 Then Throw New IOException(String.Join(Environment.NewLine, Problems()))
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "driving-controls.json"), Me)
    End Sub
    Public Const MinimumWheelRotation = 180, MaximumWheelRotation = 2520
    ' Steer Left and Steer Right on one DirectInput wheel, or Nothing.
    Public Function SteeringWheel() As DrivingBinding()
        Dim steering = Bindings.Where(Function(b) (b.Action = "Steer Left" OrElse b.Action = "Steer Right") AndAlso Not b.Keyboard AndAlso b.Device <> "win_xinput").ToArray()
        Return If(steering.Length = 2 AndAlso steering(0).DeviceId = steering(1).DeviceId, steering, Nothing)
    End Function
    ' The hardware maximum of wheels with a fixed range, from the DirectInput
    ' product name, or 0 when the rotation depends on the wheel's own settings.
    Public Shared Function SuggestedWheelRotation(device As String) As Integer
        Dim name = If(device, "").ToUpperInvariant()
        If name.Contains("LOGITECH") AndAlso name.Contains("G PRO") Then Return 1080
        For Each model In {"G25", "G27", "G29", "G920", "G923", "DRIVING FORCE GT"}
            If name.Contains(model) Then Return 900
        Next
        If name.Contains("THRUSTMASTER") OrElse name.StartsWith("T", StringComparison.Ordinal) Then
            For Each model In {"T248", "T128", "TMX", "TX RACING", "TX GIP"}
                If name.Contains(model) Then Return 900
            Next
            For Each model In {"T300", "T150", "T500", "T-GT", "TS-PC", "TS-XW"}
                If name.Contains(model) Then Return 1080
            Next
        End If
        Return 0
    End Function
    ' DiRT 2 reaches full steering lock at the binding's saturation: the share
    ' of each half of the wheel's travel. Full lock at gameRotation therefore
    ' needs saturation = gameRotation / wheelRotation.
    Public Sub ApplyWheelRotation(wheelRotation As Integer, gameRotation As Integer)
        Dim steering = SteeringWheel()
        If steering Is Nothing Then Throw New IOException("Bind Steer Left and Steer Right to your wheel first, with the Binding wizard or Bind device….")
        If wheelRotation < MinimumWheelRotation OrElse wheelRotation > MaximumWheelRotation Then Throw New IOException($"Wheel rotation must be {MinimumWheelRotation}–{MaximumWheelRotation}°.")
        If gameRotation > wheelRotation Then Throw New IOException("DiRT 2's rotation can't be more than your wheel's rotation.")
        Dim saturation = Math.Round(CDec(gameRotation) / wheelRotation, 2)
        If saturation < 0.1D Then Throw New IOException("DiRT 2's rotation must be at least a tenth of your wheel's rotation.")
        If steering.Any(Function(b) b.DeadZone >= saturation) Then Throw New IOException("Lower the steering dead zone below " & saturation.ToString("P0") & " first.")
        For Each binding In steering
            binding.Saturation = saturation
        Next
        Me.WheelRotation = wheelRotation
    End Sub
    ' Presets are shared by installations on this Windows account, like filter presets.
    Public Shared Function PresetFolder(context As InstallContext) As String
        Return CustomTracks.SafeFiles.Inside(IO.Path.GetDirectoryName(context.UserRoot), "driving-presets")
    End Function
    Public Shared Function LoadPreset(path As String) As DrivingControls
        If New FileInfo(path).Length > 1024 * 1024 Then Throw New IOException("This driving controls preset is too large.")
        Dim result = Files.ReadJson(Of DrivingControls)(path)
        If result Is Nothing Then Throw New IOException("Invalid driving controls preset.")
        result.Validate() : Return result
    End Function
    Public Sub SavePreset(path As String)
        Validate()
        If Enabled AndAlso Problems().Count > 0 Then Throw New IOException(String.Join(Environment.NewLine, Problems()))
        Directory.CreateDirectory(IO.Path.GetDirectoryName(IO.Path.GetFullPath(path)))
        Files.SaveJson(path, Me)
    End Sub
    Public Function Problems() As List(Of String)
        Dim result As New List(Of String)
        Dim left = Bindings.FirstOrDefault(Function(b) b.Action = "Steer Left" AndAlso Not b.Keyboard)
        Dim right = Bindings.FirstOrDefault(Function(b) b.Action = "Steer Right" AndAlso Not b.Keyboard)
        If left IsNot Nothing AndAlso right IsNot Nothing AndAlso left.Device = right.Device AndAlso left.DeviceId = right.DeviceId AndAlso left.Input = right.Input AndAlso left.Calibration = right.Calibration Then
            result.Add("Left and right steering use the same direction. Rebind steering or use the Xbox preset.")
        End If
        For Each binding In Bindings
            If binding.Device = "win_xinput" AndAlso binding.Input.EndsWith("Trigger", StringComparison.Ordinal) AndAlso binding.Calibration <> "uniDirectionalPositive" Then
                result.Add(binding.Action & ": Xbox triggers must increase when pressed. Rebind this input or use the Xbox preset.")
            End If
        Next
        Return result
    End Function
    Public Shared Function XboxPreset() As List(Of DrivingBinding)
        ' Match the game's shipped Windows XInput action map (one device input per action).
        Dim result As New List(Of DrivingBinding)
        Dim actions = {"Steer Left", "Steer Right", "Accelerate", "Brake", "Hand Brake", "Gear Up", "Gear Down", "Change View", "Look Back", "Horn", "Pause", "Menu Start Button", "Menu Select", "Menu Back", "Menu Up", "Menu Down", "Menu Left", "Menu Right"}
        Dim inputs = {"analogLeftStickX", "analogLeftStickX", "buttonRightTrigger", "buttonLeftTrigger", "buttonA", "buttonB", "buttonX", "buttonLeftShoulder", "buttonY", "buttonRightStick", "buttonStart", "buttonStart", "buttonA", "buttonB", "buttonDPadUp", "buttonDPadDown", "buttonDPadLeft", "buttonDPadRight"}
        For i = 0 To actions.Length - 1
            result.Add(New DrivingBinding With {.Action = actions(i), .DeviceId = "xinput:0", .Device = "win_xinput", .Input = "win_con_xi_" & inputs(i),
                .Calibration = If(i = 0, "biDirectionalLower", If(i = 1, "biDirectionalUpper", "uniDirectionalPositive")), .DeadZone = If(i < 2, 0.2D, 0D)})
        Next
        Return result
    End Function
    Public Shared Function KeyboardPreset() As List(Of DrivingBinding)
        ' Primary keys from the shipped Keyboard action map, for the actions
        ' replaced by XboxPreset. Other actions retain their native bindings.
        Dim actions = {"Steer Left", "Steer Right", "Accelerate", "Brake", "Hand Brake", "Gear Up", "Gear Down", "Change View", "Look Back", "Horn", "Pause", "Menu Start Button", "Menu Select", "Menu Back", "Menu Up", "Menu Down", "Menu Left", "Menu Right"}
        Dim keys = {"left", "right", "up", "down", "space", "a", "z", "c", "end", "x", "escape", "return", "return", "escape", "up", "down", "left", "right"}
        Dim result As New List(Of DrivingBinding)
        For i = 0 To actions.Length - 1
            result.Add(New DrivingBinding With {.Action = actions(i), .DeviceId = "Keyboard", .Device = "Keyboard", .Input = "win_key_" & keys(i), .Saturation = If(i < 2, 0.5D, If(i < 4, 0.25D, 1D))})
        Next
        Return result
    End Function
    Public Sub ApplyXboxPreset()
        Bindings.RemoveAll(Function(b) Not b.Keyboard)
        Bindings.AddRange(XboxPreset())
        For Each binding In KeyboardPreset()
            If Not Bindings.Any(Function(b) b.Keyboard AndAlso b.Action = binding.Action) Then Bindings.Add(binding)
        Next
        Enabled = True
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
        start.Environment.Remove("DIRT2VR_COMBINED_MANUAL_GEARS")
        start.Environment.Remove("DIRT2VR_TRANSMISSION")
        Dim overrideBindings = Enabled AndAlso Bindings.Count > 0
        If Not RequiresNativeHooks Then Return
        Validate()
        If overrideBindings AndAlso Problems().Count > 0 Then Throw New IOException(String.Join(Environment.NewLine, Problems()) & Environment.NewLine & "Open Controls → Configure driving controls to fix these bindings.")
        If Not vr Then
            If Not File.Exists(context.GraphicsPath) Then Throw New IOException("Run DiRT 2 normally once before using launcher driving controls.")
            Dim doc = XmlPatches.Read(File.ReadAllBytes(context.GraphicsPath))
            If doc.SelectSingleNode("/hardware_settings_config/graphics_card/directx/@forcedx9")?.Value <> "false" Then Throw New IOException("Launcher driving bindings, transmission and combined shifts require DiRT 2's DX11 renderer. Turn them off and set Transmission to Use game setting to use the game's saved controls in DX9.")
            start.Environment("DIRT2VR_ACTIVE") = "1"
            start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1"
        End If
        If CombinedManualGears Then start.Environment("DIRT2VR_COMBINED_MANUAL_GEARS") = "1"
        If Transmission <> "game" Then start.Environment("DIRT2VR_TRANSMISSION") = Transmission
        If overrideBindings Then
            Dim path = IO.Path.Combine(context.UserRoot, "driving-controls.xml")
            Files.AtomicWrite(path, ToXml())
            start.Environment("DIRT2VR_DRIVING_CONTROLS") = path
        End If
    End Sub
    Public Shared Function KeyInput(key As Keys) As String
        If key >= Keys.A AndAlso key <= Keys.Z Then Return "win_key_" & key.ToString().ToLowerInvariant()
        If key >= Keys.D0 AndAlso key <= Keys.D9 Then Return "win_key_" & (CInt(key) - CInt(Keys.D0)).ToString()
        If key >= Keys.F1 AndAlso key <= Keys.F12 Then Return "win_key_" & key.ToString().ToLowerInvariant()
        If key >= Keys.NumPad0 AndAlso key <= Keys.NumPad9 Then Return "win_key_numpad" & (CInt(key) - CInt(Keys.NumPad0)).ToString()
        Dim names As New Dictionary(Of Keys, String) From {{Keys.Escape, "escape"}, {Keys.Space, "space"}, {Keys.Up, "up"}, {Keys.Down, "down"}, {Keys.Left, "left"}, {Keys.Right, "right"}, {Keys.Return, "return"}, {Keys.Tab, "tab"}, {Keys.Home, "home"}, {Keys.End, "end"}, {Keys.Delete, "delete"}, {Keys.Insert, "insert"}, {Keys.PageUp, "prior"}, {Keys.PageDown, "next"}, {Keys.Back, "back"}, {Keys.LShiftKey, "lShift"}, {Keys.RShiftKey, "rShift"}, {Keys.LControlKey, "lControl"}, {Keys.RControlKey, "rControl"}}
        Return If(names.ContainsKey(key), "win_key_" & names.GetValueOrDefault(key), Nothing)
    End Function
End Class
