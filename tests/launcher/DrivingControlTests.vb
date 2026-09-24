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
        CaptureTests(repo, check)
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
        Using wizard As New DrivingBindingWizard(context)
            wizard.StartPosition = FormStartPosition.Manual : wizard.Location = New Drawing.Point(-25000, -25000)
            wizard.ShowInTaskbar = False : wizard.Show() : Application.DoEvents() : wizard.PerformLayout()
            Using bitmap As New Drawing.Bitmap(wizard.Width, wizard.Height)
                wizard.DrawToBitmap(bitmap, New Drawing.Rectangle(0, 0, wizard.Width, wizard.Height))
                bitmap.Save(IO.Path.Combine(folder, "driving-wizard.png"))
            End Using
            Dim panel = wizard.Controls(0)
            Dim buttons = panel.Controls.OfType(Of FlowLayoutPanel)().Last().Controls.OfType(Of Button)().ToArray()
            Dim skip = buttons.Single(Function(b) b.Text = "Skip / keep current")
            For Each action In DrivingControls.Actions
                skip.PerformClick()
            Next
            check(wizard.Bindings.Count = 0 AndAlso buttons.Single(Function(b) b.Text = "Apply bindings").Visible, "wizard visits every action and skipping changes no assignments")
            buttons.Single(Function(b) b.Text = "Back").PerformClick()
            check(skip.Visible AndAlso Not buttons.Single(Function(b) b.Text = "Apply bindings").Visible, "wizard Back leaves review and allows rebinding")
            wizard.Close()
        End Using
    End Sub
    Private Sub CaptureTests(repo As String, check As Action(Of Boolean, String))
        Dim xbox As New DrivingInput.Device With {.Id = "xinput:0", .Name = "win_xinput"}
        Dim wheel As New DrivingInput.Device With {.Id = "wheel", .Name = "Wheel / pedals"}
        Dim rest = State(), moved = State()
        Dim preset = DrivingControls.XboxPreset()
        Dim stock = XDocument.Load(IO.Path.Combine(repo, "artifacts/game/actionmap/Windows XInput.xml"))
        For Each binding In preset
            Dim expected = stock.Root.Elements("Action").Single(Function(a) a.Attribute("actionName").Value = binding.Action).Element("Axis")
            check(binding.Input = expected.Attribute("axisName").Value AndAlso binding.Calibration = expected.Attribute("baseCalibration").Value AndAlso binding.DeadZone = Decimal.Parse(If(expected.Attribute("deadZone")?.Value, "0"), Globalization.CultureInfo.InvariantCulture), "Xbox preset matches shipped game mapping: " & binding.Action)
        Next
        Dim broken As New DrivingControls With {.Enabled = True, .Bindings = preset}
        preset(0).Calibration = preset(1).Calibration : preset(2).Calibration = "uniDirectionalNegative"
        check(broken.Problems().Count = 2, "detects user's duplicate steering halves and inverted Xbox trigger")
        moved.Values(0) = -0.8F
        check(DrivingInput.Detect("Steer Left", xbox, rest, moved).Calibration = "biDirectionalLower", "Xbox left stick selects negative half with stock dead zone")
        rest.Values(0) = -0.8F : moved.Values(0) = 0
        check(DrivingInput.Detect("Steer Left", xbox, rest, moved) Is Nothing, "Xbox stick return cannot bind the opposite half")
        rest = State() : moved = State() : rest.Values(5) = 0.9F
        check(DrivingInput.Detect("Accelerate", xbox, rest, moved) Is Nothing, "Xbox trigger release cannot produce an inverted accelerator")
        rest = State() : rest.Values(5) = 1 : rest.Buttons(20) = 1
        Dim capture As New DrivingCapture(wheel, "Hand Brake")
        capture.Update(rest, 0) : capture.Update(rest, 640)
        check(capture.Ready AndAlso Not capture.WaitingForRelease, "high resting handbrake axis and held switch do not block readiness")
        capture.Update(rest, 1000)
        check(capture.Detected Is Nothing, "stationary high handbrake never binds itself")
        moved = State() : moved.Values(5) = -0.9F : moved.Buttons(20) = 1
        capture.Update(moved, 1040) : capture.Update(moved, 1160)
        check(capture.WaitingForRelease AndAlso capture.Detected.Input = "win_con_di_axisRz" AndAlso capture.Detected.Calibration = "uniDirectionalNegative", "inverted handbrake requires deliberate travel")
        capture.Update(rest, 1200) : capture.Update(rest, 1400)
        check(capture.Completed IsNot Nothing AndAlso capture.Travel = 0, "handbrake capture completes at original rest despite unrelated held switch")
        capture = New DrivingCapture(wheel, "Brake")
        rest = State() : rest.Values(2) = -1
        capture.Update(rest, 0) : capture.Update(rest, 640)
        moved = State() : moved.Values(2) = 0.9F
        capture.Update(moved, 700) : capture.Update(moved, 820)
        check(capture.Detected.Calibration = "uniDirectionalPositive", "low resting pedal captures increasing travel")
        moved.Connected = 0 : capture.Update(moved, 900)
        check(Not capture.Ready AndAlso capture.Detected Is Nothing AndAlso capture.Completed Is Nothing, "disconnect discards incomplete capture")
        moved.Connected = 1 : capture.Update(moved, 1000)
        check(Not capture.Ready, "reconnection must learn rest again")
        capture = New DrivingCapture(xbox, "Steer Right")
        rest = State() : rest.Values(0) = 0.8F
        capture.Update(rest, 0) : capture.Update(rest, 800)
        check(capture.Detected Is Nothing, "held Xbox stick does not become neutral or capture itself")
        rest.Values(0) = 0 : capture.Update(rest, 840) : capture.Update(rest, 1480)
        check(capture.Ready, "Xbox stick arms only after stable center")
        moved = State() : moved.Values(0) = 0.9F
        capture.Update(moved, 1520) : capture.Update(rest, 1560)
        check(Not capture.WaitingForRelease, "single-frame spike cannot bind")
        capture.Update(moved, 1600) : capture.Update(moved, 1720)
        check(capture.WaitingForRelease AndAlso capture.Completed Is Nothing, "axis assignment waits for release before advancing")
        capture.Update(rest, 1760) : capture.Update(rest, 1920)
        check(capture.Completed.Calibration = "biDirectionalUpper", "Xbox right completes with correct half after release")
        capture = New DrivingCapture(wheel, "Gear 1") With {.ButtonsOnly = True}
        rest = State() : rest.Buttons(10) = 1
        capture.Update(rest, 0) : capture.Update(rest, 640)
        moved = State() : moved.Buttons(10) = 1 : moved.Buttons(5) = 1
        capture.Update(moved, 680) : capture.Update(moved, 800)
        moved.Buttons(0) = 1 : capture.Update(moved, 840) : capture.Update(moved, 1040)
        check(capture.Completed Is Nothing, "another button cannot falsely release the selected gear")
        moved.Buttons(5) = 0 : capture.Update(moved, 1080) : capture.Update(moved, 1240)
        check(capture.Completed.Input = "win_con_di_button5", "selected gear releases despite other held buttons")
        rest = State() : moved = State() : moved.Buttons(4) = 1 : moved.Values(3) = 0.8F
        check(DrivingInput.Detect("Hand Brake", wheel, rest, moved).Input = "win_con_di_axisRx", "analog travel wins over device's simultaneous threshold button")
        check(DrivingInput.Detect("Hand Brake", wheel, rest, moved, False, True).Input = "win_con_di_button4", "button-only filter ignores unrelated axis movement")
        check(DrivingInput.Detect("Hand Brake", wheel, rest, moved, True).Input = "win_con_di_axisRx", "axis-only filter ignores digital switches")
        capture = New DrivingCapture(wheel, "Gear Up") With {.ButtonsOnly = True}
        rest = State() : moved = State() : moved.Values(2) = 1
        capture.Update(rest, 0) : capture.Update(moved, 640)
        check(capture.Ready, "button-only readiness ignores a noisy unrelated analog axis")
        moved.Buttons(2) = 1 : capture.Update(moved, 680)
        moved.Buttons(2) = 0 : capture.Update(moved, 720) : capture.Update(moved, 880)
        check(capture.Completed IsNot Nothing, "ordinary short button press completes without requiring a long hold")
        rest = State() : moved = State() : rest.Values(2) = -0.9F : moved.Values(2) = -0.3F
        check(DrivingInput.Detect("Accelerate", wheel, rest, moved) Is Nothing, "partial pedal movement below 45 percent travel is ignored")
        moved.Values(2) = 0.5F
        check(DrivingInput.Detect("Accelerate", wheel, rest, moved)?.Input = "win_con_di_axisZ", "deliberate larger pedal movement binds")
        rest = State() : moved = State() : moved.Values(0) = -0.2F
        check(DrivingInput.Detect("Steer Left", wheel, rest, moved)?.Calibration = "biDirectionalLower", "wheel steering needs only modest centered rotation")
        Dim displaced = State() : displaced.Values(0) = -0.25F
        check(DrivingInput.Detect("Steer Left", wheel, displaced, rest) Is Nothing, "returning a wheel to center cannot bind the opposite direction")
        rest.Values(2) = -1 : moved.Values(2) = 0.8F
        check(DrivingInput.Detect("Steer Left", wheel, rest, moved)?.Input = "win_con_di_axisX", "endpoint-resting pedal cannot steal a steering assignment")
        capture = New DrivingCapture(wheel, "Steer Left")
        capture.Update(rest, 0)
        Dim noisy = State() : noisy.Values(2) = -0.75F
        capture.Update(noisy, 300) : capture.Update(rest, 600) : capture.Update(noisy, 640)
        noisy.Values(0) = -0.2F
        capture.Update(noisy, 680) : capture.Update(noisy, 800)
        check(capture.WaitingForRelease AndAlso capture.Detected.Input = "win_con_di_axisX", "pedal rest noise cannot keep a stable steering axis unarmed")
        noisy.Values(0) = 0 : capture.Update(noisy, 840) : capture.Update(noisy, 1000)
        check(capture.Completed IsNot Nothing, "wheel completes independently of noisy pedal")
    End Sub
End Module
