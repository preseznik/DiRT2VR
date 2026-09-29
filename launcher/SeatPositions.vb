Imports System.IO.MemoryMappedFiles
Imports System.Text

Public Class SeatKey
    Public Property Key As Integer
    Public Property Modifiers As Integer
End Class
Public Module SeatActions
    Public ReadOnly Names As String() = {"Toggle VR", "Recenter", "Open seat adjustment", "Seat left", "Seat right", "Seat up", "Seat down", "Seat back", "Seat forward", "Panel sideways modifier", "Panel save", "Panel cancel"}
    Public Function DefaultKeys() As List(Of SeatKey)
        Return Enumerable.Range(0, 7).Select(Function(i) New SeatKey With {.Key = If(i = 0, 9, 0)}).ToList()
    End Function
    Public Sub Validate(settings As VrSettings)
        If settings.SeatKeys Is Nothing OrElse settings.SeatKeys.Count <> 7 Then Throw New IOException("Invalid seat keyboard bindings.")
        Dim all As New List(Of SeatKey) From {New SeatKey With {.Key = settings.ToggleKey, .Modifiers = settings.ToggleModifiers}, New SeatKey With {.Key = settings.RecenterKey, .Modifiers = settings.RecenterModifiers}}
        all.AddRange(settings.SeatKeys)
        If settings.Bindings IsNot Nothing Then
            For Each binding In settings.Bindings.Where(Function(b) b IsNot Nothing AndAlso (b.Action = 2 OrElse b.Action >= 9))
                If binding.Buttons Is Nothing Then Continue For
                Dim navigation = If(binding.Source = "xinput", {1, 2, 4, 8}, {1001, 1002, 1003, 1004})
                If binding.Buttons.Any(Function(b) navigation.Contains(b)) Then Throw New IOException("The D-pad/POV directions are reserved for panel navigation. Choose a different panel button.")
                If binding.Action = 2 AndAlso binding.Source = "xinput" AndAlso binding.Buttons.Any(Function(b) {4096, 8192, 16384}.Contains(b)) Then Throw New IOException("A, B and X are panel controls. Choose another Xbox button to open the panel.")
            Next
        End If
        For i = 0 To all.Count - 1
            Dim key = all(i)
            If key Is Nothing OrElse (key.Key <> 0 AndAlso Not VrSettings.ValidKey(key.Key)) OrElse key.Modifiers < 0 OrElse key.Modifiers > 7 Then Throw New IOException("Invalid seat keyboard shortcut.")
            If key.Key = 0 Then Continue For
            If (key.Modifiers And 2) <> 0 AndAlso {9, 115}.Contains(key.Key) Then Throw New IOException("Alt+Tab and Alt+F4 are reserved by Windows.")
            If all.Take(i).Any(Function(k) k.Key = key.Key AndAlso k.Modifiers = key.Modifiers) Then Throw New IOException("VR keyboard shortcuts must be different.")
            If i >= 2 AndAlso {13, 37, 38, 39, 40}.Contains(key.Key) Then Throw New IOException("Arrow keys and Enter are reserved for seat-panel navigation.")
        Next
    End Sub
End Module
Public Class SeatPosition
    Public Property X As Single
    Public Property Y As Single
    Public Property Z As Single
    Public Function Copy() As SeatPosition
        Return New SeatPosition With {.X = X, .Y = Y, .Z = Z}
    End Function
    Public Sub Validate()
        If {X, Y, Z}.Any(Function(v) Not Single.IsFinite(v) OrElse Math.Abs(v) > 0.5F) Then Throw New IOException("Seat position must be within 50 cm of the original cockpit.")
    End Sub
End Class
Public Class SeatPositions
    Public Property Version As Integer = 1
    Public Property Cars As New Dictionary(Of String, SeatPosition)(StringComparer.Ordinal)
    Public Property UseUniversal As Boolean = False
    Public Property Universal As New SeatPosition

    Public Shared Function Load(context As InstallContext) As SeatPositions
        Dim path = IO.Path.Combine(context.UserRoot, "seat-positions.json")
        Dim result = If(File.Exists(path), Files.ReadJson(Of SeatPositions)(path), New SeatPositions)
        If result Is Nothing OrElse result.Version <> 1 OrElse result.Cars Is Nothing OrElse result.Cars.Count > 128 Then Throw New IOException("Unsupported seat positions file.")
        If result.Universal Is Nothing Then Throw New IOException("Invalid universal seat position.")
        result.Universal.Validate()
        For Each item In result.Cars
            If Not Text.RegularExpressions.Regex.IsMatch(item.Key, "^[a-z0-9_]{1,15}$") OrElse item.Value Is Nothing Then Throw New IOException("Invalid saved seat position.")
            item.Value.Validate()
        Next
        Return result
    End Function
    Public Function GetPosition(car As String) As SeatPosition
        Dim value As SeatPosition = Nothing
        Return If(Cars.TryGetValue(car, value), value.Copy(), New SeatPosition)
    End Function
    Public Sub Save(context As InstallContext)
        Files.SaveJson(IO.Path.Combine(context.UserRoot, "seat-positions.json"), Me)
    End Sub
End Class

' Separate versioned channel keeps the existing two-action ABI compatible.
' Launcher owns inputs and disk writes; render thread owns preview/commit state.
Public Class SeatChannel
    Implements IDisposable
    Public Const Size As Integer = 16384
    Public ReadOnly Name As String = "Local\DiRT2VR.Seat." & Guid.NewGuid().ToString("N")
    Private ReadOnly mapping As MemoryMappedFile
    Private ReadOnly view As MemoryMappedViewAccessor
    Private ReadOnly context As InstallContext
    Private ReadOnly settings As VrSettings
    Private ReadOnly positions As SeatPositions
    Private ReadOnly cars As PracticeCar()
    Private ReadOnly blocked As New Dictionary(Of String, HashSet(Of Integer))
    Private selected As String
    Private wasPanel As Boolean
    Private sequence As Integer
    Private generation As UInteger
    Private lastHints As String
    Public Sub New(installation As InstallContext, preferences As VrSettings)
        context = installation : settings = preferences : positions = SeatPositions.Load(context)
        cars = RaceCatalog.Current.Cars.Take(128).ToArray()
        mapping = MemoryMappedFile.CreateNew(Name, Size) : view = mapping.CreateViewAccessor()
        view.Write(0, &H54414553) : view.Write(4, 2) : view.Write(8, Size)
        view.Write(12, CUInt(Environment.TickCount64 And &HFFFFFFFFL)) : view.Write(88, -1) : view.Write(120, -1)
        view.Write(184, cars.Length)
        view.Write(188, If(positions.UseUniversal, 1, 0))
        view.Write(192, positions.Universal.X) : view.Write(196, positions.Universal.Y) : view.Write(200, positions.Universal.Z)
        For i = 0 To 6
            view.Write(128 + i * 8, settings.SeatKeys(i).Key) : view.Write(132 + i * 8, settings.SeatKeys(i).Modifiers)
        Next
        For i = 0 To cars.Length - 1
            WriteText(256 + i * 80, 16, cars(i).Code)
            WriteText(272 + i * 80, 48, cars(i).Label)
            Dim p = positions.GetPosition(cars(i).Code)
            view.Write(320 + i * 80, p.X) : view.Write(324 + i * 80, p.Y) : view.Write(328 + i * 80, p.Z)
        Next
        Dim bindings = settings.Bindings.Where(Function(b) b.Action >= 2 AndAlso b.Source <> "hid").ToArray()
        view.Write(11300, bindings.Length)
        For i = 0 To bindings.Length - 1
            Dim binding = bindings(i), offset = 11304 + i * 48
            view.Write(offset, If(binding.Source = "xinput", 1, 2))
            view.Write(offset + 4, If(binding.Source = "xinput", Integer.Parse(binding.Device), 0))
            If binding.Source = "dinput" Then view.WriteArray(offset + 8, Guid.Parse(binding.Device).ToByteArray(), 0, 16)
            Dim masks(3) As UInteger, pov As UInteger
            For Each button In binding.Buttons
                If binding.Source = "xinput" Then
                    masks(0) = masks(0) Or CUInt(button)
                ElseIf button >= 1001 Then
                    pov = 1
                Else
                    masks((button - 1) \ 32) = masks((button - 1) \ 32) Or (1UI << ((button - 1) Mod 32))
                End If
            Next
            view.WriteArray(offset + 24, masks, 0, 4) : view.Write(offset + 40, pov) : view.Write(offset + 44, binding.Action)
        Next
        WriteHints(Nothing)
    End Sub
    Private Sub WriteText(offset As Integer, length As Integer, text As String)
        Dim value(length - 1) As Byte
        Encoding.UTF8.GetEncoder().Convert(text.ToCharArray(), 0, text.Length, value, 0, length - 1, True, Nothing, Nothing, Nothing)
        view.WriteArray(offset, value, 0, value.Length)
    End Sub
    Private Sub WriteHints(device As ControllerSample)
        Dim modifier = "Shift", save = "Enter", cancel = "Esc"
        If device IsNot Nothing Then
            Dim defaults = If(device.Source = "xinput", {"X", "A", "B"}, {"unbound", "unbound", "unbound"})
            Dim names As New List(Of String)
            For action = 9 To 11
                Dim a = action
                Dim binding = settings.Bindings.FirstOrDefault(Function(b) b.Action = a AndAlso b.Source = device.Source AndAlso b.Device = device.Device)
                names.Add(If(binding Is Nothing, defaults(action - 9), String.Join("+", binding.Buttons.Select(Function(b) ControllerNames.ButtonName(device.Source, b)))))
            Next
            modifier &= " / " & names(0) : save &= " / " & names(1) : cancel &= " / " & names(2)
        End If
        Dim text = $"Hold {modifier}: sideways{vbLf}{save}: save    {cancel}: cancel"
        If text = lastHints Then Return
        lastHints = text
        Dim bytes(799) As Byte
        Encoding.Unicode.GetBytes(text.Substring(0, Math.Min(399, text.Length))).CopyTo(bytes, 0)
        view.WriteArray(10500, bytes, 0, bytes.Length)
    End Sub
    Private Shared Function DeviceKey(sample As ControllerSample) As String
        Return sample.Source & ":" & sample.Device
    End Function
    Public Function ConsumesShortcut(sample As ControllerSample, action As Integer) As Boolean
        If view.ReadInt32(80) = 0 Then Return False
        If selected IsNot Nothing AndAlso selected <> DeviceKey(sample) Then Return False
        Dim reserved As New HashSet(Of Integer)(If(sample.Source = "xinput", {1, 2, 4, 8, 4096, 8192, 16384}, {1001, 1002, 1003, 1004}))
        reserved.UnionWith(settings.Bindings.Where(Function(b) b.Action >= 2 AndAlso b.Source = sample.Source AndAlso b.Device = sample.Device).SelectMany(Function(b) b.Buttons))
        Return settings.Bindings.Where(Function(b) b.Action = action AndAlso b.Source = sample.Source AndAlso b.Device = sample.Device).Any(Function(b) b.Buttons.Any(Function(button) reserved.Contains(button)))
    End Function
    Public Sub Poll(samples As IEnumerable(Of ControllerSample), focused As Boolean)
        ReadCommit()
        Dim panel = view.ReadInt32(80) <> 0
        Dim snapshot = samples.Where(Function(s) s.Source <> "hid").ToArray()
        If Not panel AndAlso wasPanel Then selected = Nothing
        Dim held As UInteger = 0
        Dim used As ControllerSample = Nothing
        Dim consumed As New HashSet(Of Integer)
        For Each sample In snapshot
            Dim key = DeviceKey(sample)
            If Not sample.Connected Then
                blocked.Remove(key)
                If selected = key Then selected = Nothing
                Continue For
            End If
            If Not blocked.ContainsKey(key) OrElse Not focused OrElse panel <> wasPanel Then blocked(key) = New HashSet(Of Integer)(sample.Buttons)
            blocked(key).IntersectWith(sample.Buttons)
            Dim buttons = sample.Buttons.Except(blocked(key)).ToHashSet()
            If Not focused Then Continue For
            If selected IsNot Nothing AndAlso selected <> key Then Continue For
            Dim mask As UInteger = 0
            Dim localConsumed As New HashSet(Of Integer)
            For Each binding In settings.Bindings.Where(Function(b) b.Action >= 2 AndAlso b.Source = sample.Source AndAlso b.Device = sample.Device)
                If binding.Action >= 9 AndAlso Not panel Then Continue For
                If panel AndAlso binding.Action >= 3 AndAlso binding.Action <= 8 Then Continue For
                If Not binding.Buttons.All(Function(b) buttons.Contains(b)) Then Continue For
                Dim bit = If(binding.Action = 2, 13, If(binding.Action <= 8, binding.Action - 3, If(binding.Action = 9, 6, binding.Action + 1)))
                mask = mask Or (1UI << bit) : localConsumed.UnionWith(binding.Buttons)
            Next
            If panel Then
                Dim nav = If(sample.Source = "xinput", {1, 2, 4, 8}, {1001, 1002, 1003, 1004})
                For i = 0 To 3
                    If buttons.Contains(nav(i)) Then mask = mask Or (1UI << (7 + i))
                    localConsumed.Add(nav(i))
                Next
                If sample.Source = "xinput" Then
                    For i = 0 To 2
                        Dim action = 9 + i, button = {16384, 4096, 8192}(i)
                        If settings.Bindings.Any(Function(b) b.Action = action AndAlso b.Source = sample.Source AndAlso b.Device = sample.Device) Then Continue For
                        If buttons.Contains(button) Then mask = mask Or (1UI << If(i = 0, 6, 10 + i))
                        localConsumed.Add(button)
                    Next
                End If
            End If
            If mask = 0 AndAlso selected <> key Then Continue For
            If used IsNot Nothing Then Continue For
            used = sample : held = mask : consumed = localConsumed
            If (mask And (1UI << 13)) <> 0 OrElse (panel AndAlso (mask And &H780UI) <> 0) Then selected = key
        Next
        sequence += 1 : view.Write(16, sequence) : Thread.MemoryBarrier()
        view.Write(12, CUInt(Environment.TickCount64 And &HFFFFFFFFL)) : view.Write(20, held)
        view.Write(36, If(used Is Nothing, 0, If(used.Source = "xinput", 1, 2)))
        view.Write(40, If(used IsNot Nothing AndAlso used.Source = "xinput", Integer.Parse(used.Device), 0))
        Dim guidBytes(15) As Byte
        If used IsNot Nothing AndAlso used.Source = "dinput" Then guidBytes = Guid.Parse(used.Device).ToByteArray()
        view.WriteArray(44, guidBytes, 0, 16)
        Dim masks(3) As UInteger, pov As UInteger = 0
        If used IsNot Nothing Then
            For Each button In consumed
                If used.Source = "xinput" Then
                    masks(0) = masks(0) Or CUInt(button)
                ElseIf button >= 1001 AndAlso button <= 1004 Then
                    pov = 1
                ElseIf button >= 1 AndAlso button <= 128 Then
                    masks((button - 1) \ 32) = masks((button - 1) \ 32) Or (1UI << ((button - 1) Mod 32))
                End If
            Next
        End If
        view.WriteArray(60, masks, 0, 4) : view.Write(76, pov) : WriteHints(used)
        Thread.MemoryBarrier() : sequence += 1 : view.Write(16, sequence)
        wasPanel = panel
    End Sub
    Private Sub ReadCommit()
        Dim before = view.ReadInt32(92)
        If (before And 1) <> 0 Then Return
        Dim nextGeneration = view.ReadUInt32(124), index = view.ReadInt32(120)
        Dim p As New SeatPosition With {.X = view.ReadSingle(108), .Y = view.ReadSingle(112), .Z = view.ReadSingle(116)}
        Thread.MemoryBarrier()
        If before <> view.ReadInt32(92) OrElse nextGeneration = generation Then Return
        If positions.UseUniversal Then
            If index <> -2 Then Return
            p.Validate() : positions.Universal = p
        Else
            If index < 0 OrElse index >= cars.Length Then Return
            p.Validate() : positions.Cars(cars(index).Code) = p
        End If
        positions.Save(context)
        generation = nextGeneration
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        ReadCommit() : view.Dispose() : mapping.Dispose()
    End Sub
End Class
