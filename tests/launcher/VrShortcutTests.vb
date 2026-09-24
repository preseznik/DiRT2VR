Imports DiRT2VR

Module VrShortcutTests
    Public Sub Run(check As Action(Of Boolean, String))
        Dim id = Guid.NewGuid().ToString()
        Dim sample As New ControllerSample With {.Source = "dinput", .Device = id, .Label = "Wheel", .Buttons = New HashSet(Of Integer) From {90}}
        Dim capture As New ControllerCapture({sample})
        check(capture.Update(sample, 0) Is Nothing, "VR capture ignores switches held before binding")
        sample.Buttons.Add(12)
        check(capture.Update(sample, 0) Is Nothing, "VR capture waits for wheel button release")
        sample.Buttons.Remove(12)
        Dim binding = capture.Update(sample, 0)
        check(binding IsNot Nothing AndAlso binding.Source = "dinput" AndAlso binding.Device = id AndAlso binding.Buttons.SequenceEqual({12}), "VR capture binds fresh DirectInput button despite held switch")
        Dim settings As New VrSettings
        settings.Bindings.Add(binding) : settings.Validate()
        check(True, "DirectInput shortcut passes saved-settings validation")
        Using machine As New BindingMachine(settings.Bindings)
            sample.Buttons.Add(12)
            check(Not machine.Update(sample).Any(), "wheel held at session start cannot toggle VR")
            sample.Buttons.Remove(12) : machine.Update(sample).ToArray()
            sample.Buttons.Add(12)
            check(machine.Update(sample).SequenceEqual({0}), "wheel button triggers the session Toggle VR action")
            check(Not machine.Update(sample).Any(), "wheel shortcut cannot repeat while held")
            sample.Connected = False : machine.Update(sample).ToArray()
            sample.Connected = True
            check(Not machine.Update(sample).Any(), "wheel reconnect cannot trigger a held shortcut")
            sample.Buttons.Remove(12) : machine.Update(sample).ToArray()
            sample.Buttons.Add(12)
            check(machine.Update(sample).SequenceEqual({0}), "wheel release rearms after reconnect")
        End Using
        sample.Buttons.Clear()
        capture = New ControllerCapture({sample})
        sample.Buttons.UnionWith({2, 128}) : capture.Update(sample, 1)
        sample.Buttons.Remove(2)
        check(capture.Update(sample, 1) Is Nothing, "VR pair capture waits for both buttons to release")
        sample.Buttons.Clear() : binding = capture.Update(sample, 1)
        check(binding.Buttons.SequenceEqual({2, 128}) AndAlso binding.Action = 1, "wheel pair captures Recenter including button 128")
        capture = New ControllerCapture(Array.Empty(Of ControllerSample)())
        sample.Buttons.Add(12)
        check(capture.Update(sample, 1) Is Nothing, "newly connected wheel ignores held button during capture")
        sample.Buttons.Clear() : capture.Update(sample, 1)
        sample.Buttons.Add(12) : capture.Update(sample, 1)
        sample.Connected = False
        check(capture.Update(sample, 1) Is Nothing, "disconnect cancels incomplete capture")
        sample.Connected = True
        check(capture.Update(sample, 1) Is Nothing, "capture reconnect requires fresh release")
        sample.Buttons.Clear() : capture.Update(sample, 1)
        sample.Buttons.Add(12) : capture.Update(sample, 1)
        sample.Buttons.Clear()
        check(capture.Update(sample, 1).Buttons.SequenceEqual({12}), "capture can resume after reconnection")
        settings.Bindings.Add(New ControllerBinding With {.Action = 1, .Source = "dinput", .Device = id, .Buttons = New List(Of Integer) From {12, 13}})
        Dim rejected As Boolean
        Try
            settings.Validate()
        Catch ex As IO.IOException
            rejected = True
        End Try
        check(rejected, "wheel shortcut overlap is rejected across actions")
    End Sub
End Module
