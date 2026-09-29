Imports System.Windows.Forms

Partial Public Class MainForm
    Private ReadOnly seatCar As ComboBox = Choice("SeatCar")
    Private ReadOnly seatHeight As New ValueSlider("SeatHeight", -100, 100, 0, " cm", 2D)
    Private ReadOnly seatDepth As New ValueSlider("SeatDepth", -100, 100, 0, " cm", 2D)
    Private ReadOnly seatSide As New ValueSlider("SeatSideways", -100, 100, 0, " cm", 2D)
    Private ReadOnly universalSeat As New CheckBox With {.Text = "Use universal seat position", .Name = "UseUniversalSeat", .AutoSize = True}
    Private universalEdited As Boolean
    Private seatModeEdited As Boolean
    Private seatPositions As SeatPositions
    Private ReadOnly editedSeats As New HashSet(Of String)
    Private loadingSeat As Boolean
    Private seatWasBusy As Boolean
    Private ReadOnly seatKeyButtons As Button() = Enumerable.Range(0, 7).Select(Function(i) New Button With {.AutoSize = True}).ToArray()
    Private Function Shortcut(action As Integer) As SeatKey
        If action = 0 Then Return New SeatKey With {.Key = settings.ToggleKey, .Modifiers = settings.ToggleModifiers}
        If action = 1 Then Return New SeatKey With {.Key = settings.RecenterKey, .Modifiers = settings.RecenterModifiers}
        Return settings.SeatKeys(action - 2)
    End Function
    Private Sub AssignShortcut(action As Integer, key As Integer, modifiers As Integer)
        If action = 0 Then
            settings.ToggleKey = key : settings.ToggleModifiers = modifiers
        ElseIf action = 1 Then
            settings.RecenterKey = key : settings.RecenterModifiers = modifiers
        Else
            settings.SeatKeys(action - 2) = New SeatKey With {.Key = key, .Modifiers = modifiers}
        End If
    End Sub
    Private Function ShortcutButton(action As Integer) As Button
        Return If(action = 0, toggleButton, If(action = 1, recenterButton, seatKeyButtons(action - 2)))
    End Function
    Private Sub BuildSeatSettings(parent As Control)
        seatPositions = SeatPositions.Load(context)
        Dim section = LauncherLayout.Section(parent, "VR cockpit (Experimental)")
        universalSeat.Checked = seatPositions.UseUniversal
        section.Controls.Add(universalSeat)
        Tip(universalSeat, "Use one seat position for all cars. Your per-car positions are kept for when you turn this off. Save and relaunch to apply.")
        AddHandler universalSeat.CheckedChanged, Sub()
                                                    If loadingSeat OrElse busy Then Return
                                                    seatPositions.UseUniversal = universalSeat.Checked
                                                    seatModeEdited = True
                                                    LoadSeatSelection()
                                                End Sub
        seatCar.Items.AddRange(RaceCatalog.Current.Cars.Cast(Of Object).ToArray())
        Field(section, "Car", seatCar)
        Tip(seatCar, "Choose the car whose saved seat position you want to adjust. The game selects its actual car automatically while driving.")
        Field(section, "Height", seatHeight) : Field(section, "Forward / back", seatDepth) : Field(section, "Left / right", seatSide)
        Tip(seatHeight, "Raise or lower the active seat position. You can also adjust it in VR with Tab.")
        Tip(seatDepth, "Positive moves you toward the dashboard. This does not change world scale.")
        Tip(seatSide, "Positive moves your seat to the right. Edits the shared position when universal mode is on.")
        Dim reset As New Button With {.Text = "Reset seat position", .Name = "ResetSeat", .AutoSize = True}
        AddHandler reset.Click, Sub()
                                    If busy Then Return
                                    seatHeight.Value = 0 : seatDepth.Value = 0 : seatSide.Value = 0
                                    EditSeat()
                                End Sub
        Tip(reset, "Reset the active seat position to zero. In universal mode, individual car positions stay unchanged. Save settings to keep the reset.")
        section.Controls.Add(reset)
        AddHandler seatCar.SelectedIndexChanged, Sub() LoadSeatSelection()
        For Each slider In {seatHeight, seatDepth, seatSide}
            AddHandler slider.ValueChanged, Sub() EditSeat()
        Next
        seatCar.SelectedItem = RaceCatalog.Current.Cars.FirstOrDefault(Function(c) c.Code = settings.CarCode)
        If seatCar.SelectedIndex < 0 Then seatCar.SelectedIndex = 0
    End Sub
    Private Sub LoadSeatSelection()
        Dim car = TryCast(seatCar.SelectedItem, PracticeCar)
        seatCar.Enabled = Not busy AndAlso Not seatPositions.UseUniversal
        If car Is Nothing AndAlso Not seatPositions.UseUniversal Then Return
        Dim p = If(seatPositions.UseUniversal, seatPositions.Universal, seatPositions.GetPosition(car.Code))
        loadingSeat = True
        seatHeight.Value = CInt(Math.Round(p.Y * 200))
        seatDepth.Value = -CInt(Math.Round(p.Z * 200))
        seatSide.Value = CInt(Math.Round(p.X * 200))
        loadingSeat = False
    End Sub
    Private Sub EditSeat()
        If loadingSeat OrElse busy Then Return
        Dim p As New SeatPosition With {.X = seatSide.Value / 200.0F, .Y = seatHeight.Value / 200.0F, .Z = -seatDepth.Value / 200.0F}
        If seatPositions.UseUniversal Then
            seatPositions.Universal = p : universalEdited = True
        Else
            Dim car = TryCast(seatCar.SelectedItem, PracticeCar)
            If car Is Nothing Then Return
            seatPositions.Cars(car.Code) = p : editedSeats.Add(car.Code)
        End If
    End Sub
    Private Sub RefreshSeatAvailability()
        universalSeat.Enabled = Not busy
        seatCar.Enabled = Not busy AndAlso Not seatPositions.UseUniversal
        For Each slider In {seatHeight, seatDepth, seatSide}
            slider.Enabled = Not busy
        Next
        If seatWasBusy AndAlso Not busy AndAlso editedSeats.Count = 0 AndAlso Not universalEdited AndAlso Not seatModeEdited Then
            seatPositions = SeatPositions.Load(context)
            loadingSeat = True : universalSeat.Checked = seatPositions.UseUniversal : loadingSeat = False
            LoadSeatSelection()
        End If
        seatWasBusy = busy
    End Sub
    Private Sub SaveSeats()
        If editedSeats.Count = 0 AndAlso Not universalEdited AndAlso Not seatModeEdited Then Return
        Dim latest = SeatPositions.Load(context)
        For Each code In editedSeats
            latest.Cars(code) = seatPositions.GetPosition(code)
        Next
        If universalEdited Then latest.Universal = seatPositions.Universal.Copy()
        If seatModeEdited Then latest.UseUniversal = seatPositions.UseUniversal
        latest.Save(context) : seatPositions = latest : editedSeats.Clear()
        universalEdited = False : seatModeEdited = False
    End Sub
End Class
