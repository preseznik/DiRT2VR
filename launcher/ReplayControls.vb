Imports System.Windows.Forms
Imports System.Globalization

' Native replay actions use the existing game action-map override. Custom zoom
' stays in the chase-camera path and is never evaluated while driving.
Public Class ReplayControls
    Inherits DrivingControls
    Public Shared ReadOnly NativeActions As String() = {"Replay Rewind", "Replay Fast Forward", "Replay Pause", "Replay Next Camera", "Replay Prev Camera", "Replay Jump In", "Replay Next Lap", "Replay Previous Lap", "Replay Exit", "Replay On Off"}
    Public Shared ReadOnly ZoomActions As String() = {"Replay Zoom In", "Replay Zoom Out"}
    Public Shared ReadOnly ReplayActions As String() = NativeActions.Concat(ZoomActions).ToArray()
    Public Shared ReadOnly XboxInputs As String() = {"analogLeftStickX", "analogLeftStickY", "analogRightStickX", "analogRightStickY", "buttonLeftTrigger", "buttonRightTrigger", "buttonDPadUp", "buttonDPadDown", "buttonDPadLeft", "buttonDPadRight", "buttonStart", "buttonBack", "buttonLeftStick", "buttonRightStick", "buttonLeftShoulder", "buttonRightShoulder", "buttonA", "buttonB", "buttonX", "buttonY"}
    Public Sub New()
        Enabled = True
        Bindings = ZoomDefaults()
    End Sub
    Public Shared Function ZoomDefaults() As List(Of DrivingBinding)
        Return New List(Of DrivingBinding) From {
            New DrivingBinding With {.Action = ZoomActions(0), .Device = "Keyboard", .DeviceId = "Keyboard", .Input = "win_key_prior"},
            New DrivingBinding With {.Action = ZoomActions(1), .Device = "Keyboard", .DeviceId = "Keyboard", .Input = "win_key_next"},
            New DrivingBinding With {.Action = ZoomActions(0), .Device = "win_xinput", .DeviceId = "xinput:0", .Input = "win_con_xi_analogLeftStickY", .Calibration = "biDirectionalLower", .DeadZone = 0.2D},
            New DrivingBinding With {.Action = ZoomActions(1), .Device = "win_xinput", .DeviceId = "xinput:0", .Input = "win_con_xi_analogLeftStickY", .Calibration = "biDirectionalUpper", .DeadZone = 0.2D}}
    End Function
    Public Shared Function KeyCode(binding As DrivingBinding) As Integer
        For code = 1 To 254
            If DrivingControls.KeyInput(CType(code, Keys)) = binding.Input Then Return code
        Next
        Throw New IOException("Unsupported replay zoom key.")
    End Function
    Public Shadows Sub Validate()
        MyBase.Validate()
        If Bindings.Any(Function(b) Not ReplayActions.Contains(b.Action)) Then Throw New IOException("Invalid replay action.")
        For Each binding In Bindings.Where(Function(b) ZoomActions.Contains(b.Action))
            If binding.Keyboard Then
                KeyCode(binding)
                If binding.Calibration <> "uniDirectionalPositive" Then Throw New IOException("Replay zoom keys must use normal button calibration.")
            ElseIf binding.Device <> "win_xinput" OrElse Not XboxInputs.Contains(binding.Input.Replace("win_con_xi_", "")) Then
                Throw New IOException("Chase zoom supports keyboard and Xbox inputs. Other replay actions also support wheels and pedals.")
            End If
        Next
    End Sub
    Public Shared Shadows Function Load(context As InstallContext) As ReplayControls
        Dim path = IO.Path.Combine(context.UserRoot, "replay-controls.json")
        Dim result = If(File.Exists(path), Files.ReadJson(Of ReplayControls)(path), New ReplayControls())
        If result Is Nothing Then Throw New IOException("Invalid replay-controls file.")
        result.Validate() : Return result
    End Function
    Public Shadows Sub Save(context As InstallContext)
        Validate() : Files.SaveJson(IO.Path.Combine(context.UserRoot, "replay-controls.json"), Me)
    End Sub
    Public Sub ApplyReplayPreset()
        Bindings = ZoomDefaults()
        Dim keys = {"f2", "f3", "f1", "f8", "f7", "f12", "f5", "f4", "escape", "f4"}
        Dim xbox = {"buttonLeftTrigger", "buttonRightTrigger", "buttonA", "buttonRightShoulder", "buttonLeftShoulder", "buttonX", "", "", "buttonStart", "buttonB"}
        For i = 0 To NativeActions.Length - 1
            Bindings.Add(New DrivingBinding With {.Action = NativeActions(i), .Device = "Keyboard", .DeviceId = "Keyboard", .Input = "win_key_" & keys(i)})
            If xbox(i) <> "" Then Bindings.Add(New DrivingBinding With {.Action = NativeActions(i), .Device = "win_xinput", .DeviceId = "xinput:0", .Input = "win_con_xi_" & xbox(i)})
        Next
        Enabled = True
    End Sub
    Public Function Combined(driving As DrivingControls) As DrivingControls
        Validate() : driving.Validate()
        Dim result As New DrivingControls
        If driving.Enabled Then result.Bindings.AddRange(driving.Bindings)
        If Enabled Then result.Bindings.AddRange(Bindings.Where(Function(b) NativeActions.Contains(b.Action)))
        result.Enabled = result.Bindings.Count > 0
        result.Validate() : Return result
    End Function
    Public Sub ConfigureZoom(start As ProcessStartInfo)
        Validate()
        For index = 0 To 1
            Dim action = ZoomActions(index)
            Dim fields As New List(Of String)
            If Enabled Then
                For Each b In Bindings.Where(Function(item) item.Action = action)
                    Dim kind = If(b.Keyboard, 0, 1)
                    Dim input = If(b.Keyboard, KeyCode(b), Array.IndexOf(XboxInputs, b.Input.Replace("win_con_xi_", "")))
                    fields.Add(String.Join(",", {kind.ToString(), input.ToString(), Array.IndexOf(Calibrations, b.Calibration).ToString(), b.DeadZone.ToString(CultureInfo.InvariantCulture), b.Saturation.ToString(CultureInfo.InvariantCulture)}))
                Next
            End If
            start.Environment("DIRT2VR_REPLAY_ZOOM_" & index.ToString()) = String.Join(";", fields)
        Next
    End Sub
End Class
