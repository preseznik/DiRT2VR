Imports DiRT2VR
Imports System.IO
Imports System.Windows.Forms
Imports System.Xml.Linq

Module DrivingControlTests
    Private Function State() As DrivingInput.Sample
        Return New DrivingInput.Sample With {.Connected = 1, .Axes = 255, .Values = New Single(7) {}, .Buttons = New Byte(127) {}, .Pov = New UInteger(3) {}}
    End Function
    Public Sub Run(repo As String, folder As String, check As Action(Of Boolean, String))
        Dim context As New InstallContext(IO.Path.Combine(folder, "driving game"), IO.Path.Combine(folder, "driving user"), IO.Path.Combine(folder, "driving graphics.xml"))
        Dim config = DrivingControls.Load(context)
        check(Not config.Enabled AndAlso config.Bindings.Count = 0, "driving overrides default off and preserve game controls")
        config.Bindings.Add(New DrivingBinding With {.Action = "Accelerate", .Device = "Keyboard", .DeviceId = "Keyboard", .Input = DrivingControls.KeyInput(Keys.I)})
        config.Bindings.Add(New DrivingBinding With {.Action = "Brake", .Device = "Pedals & <test>", .DeviceId = "test-id", .Input = "win_con_di_axisZ", .Calibration = "uniDirectionalNegative", .DeadZone = 0.05D, .Saturation = 0.9D})
        config.Enabled = True : config.Save(context)
        Dim restored = DrivingControls.Load(context), xml = XElement.Parse(Text.Encoding.UTF8.GetString(restored.ToXml()))
        check(xml.Elements("Action").Count() = 2 AndAlso xml.Descendants("Axis").Last().Attribute("deviceName").Value = "Pedals & <test>", "driving XML safely round-trips device names")
        check(xml.Descendants("Axis").Last().Attribute("deadZone").Value = "0.05", "calibration XML uses invariant decimals")
        check(restored.Bindings.Last().Calibration = "uniDirectionalNegative", "inversion and separate pedal assignment survive reload")
        Dim start As New ProcessStartInfo()
        restored.ConfigureProcess(context, start, True)
        check(File.Exists(start.Environment("DIRT2VR_DRIVING_CONTROLS")) AndAlso Not start.Environment.ContainsKey("DIRT2VR_DESKTOP_CONTROLS"), "VR gets binding file without desktop activation")
        Dim rejected As Boolean
        Try
            restored.ConfigureProcess(context, New ProcessStartInfo(), False)
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected, "desktop bindings require existing DX11 configuration")
        File.WriteAllText(context.GraphicsPath, "<hardware_settings_config><graphics_card><directx forcedx9=""false"" /></graphics_card></hardware_settings_config>")
        start = New ProcessStartInfo()
        restored.ConfigureProcess(context, start, False)
        check(start.Environment("DIRT2VR_ACTIVE") = "1" AndAlso start.Environment("DIRT2VR_DESKTOP_CONTROLS") = "1" AndAlso Not start.Environment.ContainsKey("DIRT2VR_HEADSET"), "desktop binding path enables no VR session")
        restored.Enabled = False : start = New ProcessStartInfo()
        restored.ConfigureProcess(context, start, False)
        check(Not start.Environment.ContainsKey("DIRT2VR_ACTIVE"), "disabled bindings leave ordinary launch inactive")
        restored.Bindings.Add(restored.Bindings(0)) : rejected = False
        Try
            restored.Validate()
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected, "duplicate action/channel assignments rejected")
        check(DrivingControls.KeyInput(Keys.F10) = "win_key_f10" AndAlso DrivingControls.KeyInput(Keys.NumPad3) = "win_key_numpad3" AndAlso DrivingControls.KeyInput(Keys.None) Is Nothing, "driving keyboard conversion is bounded and supports function and numpad keys")
        Dim device As New DrivingInput.Device With {.Id = "wheel-id", .Name = "Test wheel"}
        Dim baseline = State(), current = State()
        current.Values(0) = -0.7F
        Dim binding = DrivingInput.Detect("Steer Left", device, baseline, current)
        check(binding.Input = "win_con_di_axisX" AndAlso binding.Calibration = "biDirectionalLower", "wheel movement captures steering direction")
        baseline.Values(2) = 1 : current.Values(0) = 0 : current.Values(2) = -0.9F
        binding = DrivingInput.Detect("Brake", device, baseline, current)
        check(binding.Input = "win_con_di_axisZ" AndAlso binding.Calibration = "uniDirectionalNegative", "reversed pedal travel detects inversion")
        baseline = State() : current = State() : baseline.Buttons(4) = 1 : current.Buttons(4) = 1
        check(DrivingInput.Detect("Gear 1", device, baseline, current) Is Nothing, "held button does not bind on opening capture")
        baseline.Buttons(4) = 0
        check(DrivingInput.Detect("Gear 1", device, baseline, current).Input = "win_con_di_button4", "H-pattern button preserves native zero-based index")
        current.Connected = 0
        check(DrivingInput.Detect("Gear 1", device, baseline, current) Is Nothing, "disconnected input cannot create a binding")
        device = New DrivingInput.Device With {.Id = "xinput:0", .Name = "win_xinput"}
        baseline = State() : current = State() : current.Values(5) = 0.8F
        binding = DrivingInput.Detect("Accelerate", device, baseline, current)
        check(binding.Input = "win_con_xi_buttonRightTrigger" AndAlso binding.Calibration = "uniDirectionalPositive", "Xbox triggers retain independent analog input")
        Dim payload = IO.Path.Combine(context.ModRoot, "payload", "driving_input.dll")
        Directory.CreateDirectory(IO.Path.GetDirectoryName(payload))
        File.Copy(IO.Path.Combine(repo, "build/driving-input/driving_input.dll"), payload)
        Files.SaveJson(IO.Path.Combine(context.ModRoot, "package.json"), New PackageManifest With {.Files = New Dictionary(Of String, String) From {{"DiRT2VR/payload/driving_input.dll", Files.Hash(payload)}}})
        Using window As New Form
            window.CreateControl()
            Using input As New DrivingInput(context, window.Handle)
                check(input.Devices.Count <= 256, "actual x64 DirectInput helper loads and enumerates")
                For Each found In input.Devices
                    Dim sample = input.Read(found)
                    check(sample.Values.Length = 8 AndAlso sample.Buttons.Length = 128, "native controller sample marshals completely")
                Next
            End Using
        End Using
        Using form As New DrivingControlsForm(context)
            form.StartPosition = FormStartPosition.Manual : form.Location = New Drawing.Point(-25000, -25000)
            form.ShowInTaskbar = False : form.Show() : Application.DoEvents() : form.PerformLayout()
            Using bitmap As New Drawing.Bitmap(form.Width, form.Height)
                form.DrawToBitmap(bitmap, New Drawing.Rectangle(0, 0, form.Width, form.Height))
                bitmap.Save(IO.Path.Combine(folder, "driving-controls.png"))
            End Using
            check(form.Controls.Count > 0, "driving editor constructs with saved settings")
            form.Close()
        End Using
    End Sub
End Module
