Imports System.Drawing
Imports System.Windows.Forms

' VR / Desktop switch for the cockpit seat; each mode keeps its own positions.
Public Class SeatModePicker
    Inherits FlowLayoutPanel
    Private ReadOnly vr As New RadioButton With {.Text = "VR", .Name = "SeatModeVr"}
    Private ReadOnly desktopButton As New RadioButton With {.Text = "Desktop", .Name = "SeatModeDesktop"}
    Public Event ValueChanged As EventHandler
    Public Sub New()
        AutoSize = True : WrapContents = False : Margin = New Padding(0)
        AccessibleName = "Seat positions for"
        For Each button In {vr, desktopButton}
            button.Appearance = Appearance.Button : button.AutoSize = True : button.TextAlign = ContentAlignment.MiddleCenter
            button.FlatStyle = FlatStyle.Flat : button.Margin = New Padding(0, 0, 2, 0)
            Controls.Add(button)
            AddHandler button.CheckedChanged, Sub(sender, e)
                                                  UpdateColours()
                                                  If DirectCast(sender, RadioButton).Checked Then RaiseEvent ValueChanged(Me, EventArgs.Empty)
                                              End Sub
        Next
        vr.Checked = True
    End Sub
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Desktop As Boolean
        Get
            Return desktopButton.Checked
        End Get
        Set(value As Boolean)
            If value Then desktopButton.Checked = True Else vr.Checked = True
        End Set
    End Property
    Protected Overrides Sub OnBackColorChanged(e As EventArgs)
        MyBase.OnBackColorChanged(e) : UpdateColours()
    End Sub
    Protected Overrides Sub OnForeColorChanged(e As EventArgs)
        MyBase.OnForeColorChanged(e) : UpdateColours()
    End Sub
    Private Sub UpdateColours()
        If vr Is Nothing OrElse desktopButton Is Nothing Then Return
        For Each button In {vr, desktopButton}
            button.BackColor = If(button.Checked, SystemColors.Highlight, BackColor)
            button.ForeColor = If(button.Checked, SystemColors.HighlightText, ForeColor)
        Next
    End Sub
End Class

Partial Public Class MainForm
    ' One mode's saved positions plus the edits not yet saved.
    Private Class SeatTarget
        Public ReadOnly Desktop As Boolean
        Public Positions As SeatPositions
        Public ReadOnly Edited As New HashSet(Of String)
        Public UniversalEdited As Boolean
        Public ModeEdited As Boolean
        Public Sub New(context As InstallContext, desktopMode As Boolean)
            Desktop = desktopMode : Positions = SeatPositions.Load(context, Desktop)
        End Sub
        Public ReadOnly Property Dirty As Boolean
            Get
                Return Edited.Count > 0 OrElse UniversalEdited OrElse ModeEdited
            End Get
        End Property
        Public Sub Reload(context As InstallContext)
            Positions = SeatPositions.Load(context, Desktop)
        End Sub
        Public Sub Save(context As InstallContext)
            If Not Dirty Then Return
            Dim latest = SeatPositions.Load(context, Desktop)
            For Each code In Edited
                latest.Cars(code) = Positions.GetPosition(code)
            Next
            If UniversalEdited Then latest.Universal = Positions.Universal.Copy()
            If ModeEdited Then latest.UseUniversal = Positions.UseUniversal
            latest.Save(context, Desktop) : Positions = latest : Edited.Clear()
            UniversalEdited = False : ModeEdited = False
        End Sub
    End Class
    Private ReadOnly seatMode As New SeatModePicker With {.Name = "SeatMode"}
    Private ReadOnly seatCar As ComboBox = Choice("SeatCar")
    Private ReadOnly seatHeight As New ValueSlider("SeatHeight", -100, 100, 0, " cm", 2D)
    Private ReadOnly seatDepth As New ValueSlider("SeatDepth", -100, 100, 0, " cm", 2D)
    Private ReadOnly seatSide As New ValueSlider("SeatSideways", -100, 100, 0, " cm", 2D)
    Private ReadOnly desktopCockpitFov As New ValueSlider("DesktopCockpitFov", DesktopSeatTransaction.MinimumFov, DesktopSeatTransaction.MaximumFov, DesktopSeatTransaction.GameFov,
        valueLabels:=Enumerable.Range(DesktopSeatTransaction.MinimumFov, DesktopSeatTransaction.MaximumFov - DesktopSeatTransaction.MinimumFov + 1).Select(Function(v) v & "°" & If(v = DesktopSeatTransaction.GameFov, " (game)", "")).ToArray())
    Private ReadOnly universalSeat As New CheckBox With {.Text = "Use universal seat position", .Name = "UseUniversalSeat", .AutoSize = True}
    Private seatTargets As SeatTarget()
    Private activeSeat As SeatTarget
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
        seatTargets = {New SeatTarget(context, False), New SeatTarget(context, True)}
        activeSeat = seatTargets(0)
        Dim section = LauncherLayout.Section(parent, "Cockpit seat (Experimental)")
        Field(section, "Positions for", seatMode)
        Tip(seatMode, "VR and desktop keep separate seat positions. Choose which ones the settings below edit.")
        universalSeat.Checked = activeSeat.Positions.UseUniversal
        section.Controls.Add(universalSeat)
        Tip(universalSeat, "Use one seat position for all cars in the selected mode. Your per-car positions are kept for when you turn this off. Save and relaunch to apply.")
        AddHandler universalSeat.CheckedChanged, Sub()
                                                    If loadingSeat OrElse busy Then Return
                                                    activeSeat.Positions.UseUniversal = universalSeat.Checked
                                                    activeSeat.ModeEdited = True
                                                    LoadSeatSelection()
                                                End Sub
        seatCar.Items.AddRange(RaceCatalog.Current.Cars.Cast(Of Object).ToArray())
        Field(section, "Car", seatCar)
        Tip(seatCar, "Choose the car whose saved seat position you want to adjust. The game selects its actual car automatically while driving.")
        Field(section, "Height", seatHeight) : Field(section, "Forward / back", seatDepth) : Field(section, "Left / right", seatSide)
        Tip(seatHeight, "Raise or lower the seat for the selected mode. In VR you can also adjust it with Tab. Desktop positions are not applied to LAN games.")
        Tip(seatDepth, "Positive moves you toward the dashboard. This does not change world scale.")
        Tip(seatSide, "Positive moves your seat to the right. Edits the shared position when universal mode is on.")
        Dim reset As New Button With {.Text = "Reset seat position", .Name = "ResetSeat", .AutoSize = True}
        AddHandler reset.Click, Sub()
                                    If busy Then Return
                                    seatHeight.Value = 0 : seatDepth.Value = 0 : seatSide.Value = 0
                                    EditSeat()
                                End Sub
        Tip(reset, "Reset the active seat position of the selected mode to zero. In universal mode, individual car positions stay unchanged. Save settings to keep the reset.")
        section.Controls.Add(reset)
        desktopCockpitFov.Value = settings.DesktopCockpitFov
        Field(section, "Field of view (desktop)", desktopCockpitFov)
        Tip(desktopCockpitFov, "Vertical field of view of the desktop cockpit view, for every car. DiRT 2 uses 52°; raise it to see more of the cockpit, lower it to zoom in. VR uses your headset's own field of view. Not applied to LAN games. Save and relaunch to apply.")
        desktopCockpitFov.Parent.Visible = False
        AddHandler seatMode.ValueChanged, Sub()
                                              activeSeat = seatTargets(If(seatMode.Desktop, 1, 0))
                                              desktopCockpitFov.Parent.Visible = seatMode.Desktop
                                              loadingSeat = True : universalSeat.Checked = activeSeat.Positions.UseUniversal : loadingSeat = False
                                              LoadSeatSelection()
                                          End Sub
        AddHandler seatCar.SelectedIndexChanged, Sub() LoadSeatSelection()
        For Each slider In {seatHeight, seatDepth, seatSide}
            AddHandler slider.ValueChanged, Sub() EditSeat()
        Next
        seatCar.SelectedItem = RaceCatalog.Current.Cars.FirstOrDefault(Function(c) c.Code = settings.CarCode)
        If seatCar.SelectedIndex < 0 Then seatCar.SelectedIndex = 0
    End Sub
    Private Sub LoadSeatSelection()
        Dim car = TryCast(seatCar.SelectedItem, PracticeCar), positions = activeSeat.Positions
        seatCar.Enabled = Not busy AndAlso Not positions.UseUniversal
        If car Is Nothing AndAlso Not positions.UseUniversal Then Return
        Dim p = If(positions.UseUniversal, positions.Universal, positions.GetPosition(car.Code))
        loadingSeat = True
        seatHeight.Value = CInt(Math.Round(p.Y * 200))
        seatDepth.Value = -CInt(Math.Round(p.Z * 200))
        seatSide.Value = CInt(Math.Round(p.X * 200))
        loadingSeat = False
    End Sub
    Private Sub EditSeat()
        If loadingSeat OrElse busy Then Return
        Dim p As New SeatPosition With {.X = seatSide.Value / 200.0F, .Y = seatHeight.Value / 200.0F, .Z = -seatDepth.Value / 200.0F}
        If activeSeat.Positions.UseUniversal Then
            activeSeat.Positions.Universal = p : activeSeat.UniversalEdited = True
        Else
            Dim car = TryCast(seatCar.SelectedItem, PracticeCar)
            If car Is Nothing Then Return
            activeSeat.Positions.Cars(car.Code) = p : activeSeat.Edited.Add(car.Code)
        End If
    End Sub
    Private Sub RefreshSeatAvailability()
        universalSeat.Enabled = Not busy
        seatCar.Enabled = Not busy AndAlso Not activeSeat.Positions.UseUniversal
        For Each slider In {seatHeight, seatDepth, seatSide}
            slider.Enabled = Not busy
        Next
        ' Pick up positions the game saved during the session, unless edited here.
        If seatWasBusy AndAlso Not busy Then
            For Each target In seatTargets.Where(Function(t) Not t.Dirty)
                target.Reload(context)
            Next
            loadingSeat = True : universalSeat.Checked = activeSeat.Positions.UseUniversal : loadingSeat = False
            LoadSeatSelection()
        End If
        seatWasBusy = busy
    End Sub
    Private Sub SaveSeats()
        For Each target In seatTargets
            target.Save(context)
        Next
    End Sub
End Class
