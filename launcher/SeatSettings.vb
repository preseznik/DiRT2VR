Imports System.Windows.Forms

Partial Public Class MainForm
    Private ReadOnly seatCar As ComboBox = Choice("SeatCar")
    Private ReadOnly seatHeight As New ValueSlider("SeatHeight", -100, 100, 0, " cm", 2D)
    Private ReadOnly seatDepth As New ValueSlider("SeatDepth", -100, 100, 0, " cm", 2D)
    Private ReadOnly seatSide As New ValueSlider("SeatSideways", -100, 100, 0, " cm", 2D)
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
        seatCar.Items.AddRange(RaceCatalog.Current.Cars.Cast(Of Object).ToArray())
        Field(section, "Car", seatCar)
        Tip(seatCar, "Choose the car whose saved seat position you want to adjust. The game selects its actual car automatically while driving.")
        Field(section, "Height", seatHeight) : Field(section, "Forward / back", seatDepth) : Field(section, "Left / right", seatSide)
        Tip(seatHeight, "Raise or lower your seat in this car. You can also adjust it in VR with Tab.")
        Tip(seatDepth, "Positive moves you toward the dashboard. This does not change world scale.")
        Tip(seatSide, "Positive moves your seat to the right. Saved separately for each car.")
        Dim reset As New Button With {.Text = "Reset seat position", .Name = "ResetSeat", .AutoSize = True}
        AddHandler reset.Click, Sub()
                                    If busy Then Return
                                    seatHeight.Value = 0 : seatDepth.Value = 0 : seatSide.Value = 0
                                    EditSeat()
                                End Sub
        Tip(reset, "Return this car to its original cockpit position. Save settings to keep the reset.")
        section.Controls.Add(reset)
        AddHandler seatCar.SelectedIndexChanged, Sub()
                                                    Dim car = TryCast(seatCar.SelectedItem, PracticeCar)
                                                    If car Is Nothing Then Return
                                                    Dim p = seatPositions.GetPosition(car.Code)
                                                    loadingSeat = True
                                                    seatHeight.Value = CInt(Math.Round(p.Y * 200))
                                                    seatDepth.Value = -CInt(Math.Round(p.Z * 200))
                                                    seatSide.Value = CInt(Math.Round(p.X * 200))
                                                    loadingSeat = False
                                                End Sub
        For Each slider In {seatHeight, seatDepth, seatSide}
            AddHandler slider.ValueChanged, Sub() EditSeat()
        Next
        seatCar.SelectedItem = RaceCatalog.Current.Cars.FirstOrDefault(Function(c) c.Code = settings.CarCode)
        If seatCar.SelectedIndex < 0 Then seatCar.SelectedIndex = 0
    End Sub
    Private Sub EditSeat()
        If loadingSeat OrElse busy Then Return
        Dim car = TryCast(seatCar.SelectedItem, PracticeCar)
        If car Is Nothing Then Return
        seatPositions.Cars(car.Code) = New SeatPosition With {.X = seatSide.Value / 200.0F, .Y = seatHeight.Value / 200.0F, .Z = -seatDepth.Value / 200.0F}
        editedSeats.Add(car.Code)
    End Sub
    Private Sub RefreshSeatAvailability()
        For Each slider In {seatHeight, seatDepth, seatSide}
            slider.Enabled = Not busy
        Next
        If seatWasBusy AndAlso Not busy AndAlso editedSeats.Count = 0 Then
            seatPositions = SeatPositions.Load(context)
            Dim car = TryCast(seatCar.SelectedItem, PracticeCar)
            If car IsNot Nothing Then
                Dim p = seatPositions.GetPosition(car.Code)
                loadingSeat = True
                seatHeight.Value = CInt(Math.Round(p.Y * 200)) : seatDepth.Value = -CInt(Math.Round(p.Z * 200)) : seatSide.Value = CInt(Math.Round(p.X * 200))
                loadingSeat = False
            End If
        End If
        seatWasBusy = busy
    End Sub
    Private Sub SaveSeats()
        If editedSeats.Count = 0 Then Return
        Dim latest = SeatPositions.Load(context)
        For Each code In editedSeats
            latest.Cars(code) = seatPositions.GetPosition(code)
        Next
        latest.Save(context) : seatPositions = latest : editedSeats.Clear()
    End Sub
End Class
