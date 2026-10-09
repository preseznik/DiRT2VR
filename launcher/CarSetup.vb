Imports System.Drawing
Imports System.Windows.Forms

Public Class CarSetup
    Public Property Enabled As Boolean
    Public Property Steps As Integer() = {2, 2, 2, 2, 2, 2}
    Public Sub Validate()
        If Steps Is Nothing OrElse Steps.Length <> 6 OrElse Steps.Any(Function(v) v < 0 OrElse v > 4) Then Throw New IOException("Vehicle setup needs six positions between 1 and 5. Open Vehicle setup and reset it.")
    End Sub
    Public Function Copy() As CarSetup
        Validate()
        Return New CarSetup With {.Enabled = Enabled, .Steps = DirectCast(Steps.Clone(), Integer())}
    End Function
    Public Shared Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_CAR_SETUP")
        If Not settings.DirectMode Then Return
        If settings.DirectCarSetup Is Nothing Then Throw New IOException("Vehicle setup is missing. Open Vehicle setup and reset it.")
        settings.DirectCarSetup.Validate()
        If settings.DirectCarSetup.Enabled Then start.Environment("DIRT2VR_CAR_SETUP") = String.Concat(settings.DirectCarSetup.Steps.Select(Function(v) ChrW(AscW("0"c) + v)))
    End Sub
End Class

Public Class CarSetupForm
    Inherits Form
    Private ReadOnly useSetup As New CheckBox With {.Text = "Use this setup for direct launches", .AutoSize = True}
    Private ReadOnly bars As New List(Of SetupSteps)
    Public ReadOnly Property Result As CarSetup
        Get
            Return New CarSetup With {.Enabled = useSetup.Checked, .Steps = bars.Select(Function(b) b.Value).ToArray()}
        End Get
    End Property
    Public Sub New(current As CarSetup)
        Text = "DiRT2VR — Vehicle setup"
        StartPosition = FormStartPosition.CenterParent
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(700, 490) : MinimumSize = New Size(620, 490)
        Font = New Font("Segoe UI", 10)
        Dim draft = If(current, New CarSetup()).Copy()
        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(18), .ColumnCount = 1, .RowCount = 5}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        body.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        body.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        body.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        body.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        body.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        body.Controls.Add(New Label With {.Text = "VEHICLE SETUP", .Font = New Font("Segoe UI", 22, FontStyle.Bold), .AutoSize = True, .Margin = New Padding(0, 0, 0, 12)}, 0, 0)
        useSetup.Checked = draft.Enabled : body.Controls.Add(useSetup, 0, 1)
        Dim table As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 6, .Margin = New Padding(0, 10, 0, 10)}
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 35))
        table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16))
        Dim names = {"GEAR RATIO", "DOWNFORCE", "SUSPENSION", "RIDE HEIGHT", "DIFFERENTIAL", "BRAKE BIAS"}
        Dim left = {"SHORT", "HIGH", "STIFF", "LOW", "STRONG", "REAR"}
        Dim right = {"LONG", "LOW", "SOFT", "HIGH", "LOOSE", "FRONT"}
        For i = 0 To 5
            table.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F / 6))
            table.Controls.Add(New Label With {.Text = names(i), .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .Font = New Font(Font, FontStyle.Bold)}, 0, i)
            table.Controls.Add(New Label With {.Text = left(i), .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft}, 1, i)
            Dim bar As New SetupSteps With {.Dock = DockStyle.Fill, .AccessibleName = names(i) & ": " & left(i) & " to " & right(i), .Value = draft.Steps(i), .Name = "Setup" & i}
            bars.Add(bar) : table.Controls.Add(bar, 2, i)
            table.Controls.Add(New Label With {.Text = right(i), .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleRight}, 3, i)
        Next
        body.Controls.Add(table, 0, 2)
        body.Controls.Add(New Label With {.Text = "Practice and Race • Desktop and VR • All cars and tracks" & vbCrLf & "Next direct launch only. Normal Launch uses the game's setup.", .AutoSize = False, .Dock = DockStyle.Fill, .Height = 52, .Margin = New Padding(0, 2, 0, 12)}, 0, 3)
        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
        Dim cancel As New Button With {.Text = "Cancel", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        Dim apply As New Button With {.Text = "Apply", .AutoSize = True, .DialogResult = DialogResult.OK}
        Dim reset As New Button With {.Text = "Reset to centre", .AutoSize = True}
        actions.Controls.AddRange({cancel, apply, reset})
        AddHandler reset.Click, Sub()
                                    For Each bar In bars
                                        bar.Value = 2
                                    Next
                                End Sub
        AddHandler useSetup.CheckedChanged, Sub() table.Enabled = useSetup.Checked
        table.Enabled = useSetup.Checked
        body.Controls.Add(actions, 0, 4)
        Controls.Add(body) : AcceptButton = apply : CancelButton = cancel
        LauncherAppearance.Apply(Me)
    End Sub
End Class

' Five native setup positions with keyboard focus, matching the game's stepped bars.
Public Class SetupSteps
    Inherits Control
    Private position As Integer = 2
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Value As Integer
        Get
            Return position
        End Get
        Set(value As Integer)
            position = Math.Clamp(value, 0, 4) : AccessibleDescription = "Position " & (position + 1).ToString() & " of 5" : Invalidate()
        End Set
    End Property
    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.Selectable, True)
        TabStop = True : AccessibleRole = AccessibleRole.Slider
    End Sub
    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Return {Keys.Left, Keys.Right, Keys.Home, Keys.End}.Contains(keyData And Keys.KeyCode) OrElse MyBase.IsInputKey(keyData)
    End Function
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.Left : Value -= 1
            Case Keys.Right : Value += 1
            Case Keys.Home : Value = 0
            Case Keys.End : Value = 4
            Case Else : MyBase.OnKeyDown(e) : Return
        End Select
        e.Handled = True : e.SuppressKeyPress = True
    End Sub
    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = MouseButtons.Left Then
            Focus() : Value = Math.Clamp(CInt(Math.Floor(e.X * 5.0 / Math.Max(1, Width))), 0, 4)
        End If
    End Sub
    Protected Overrides Sub OnGotFocus(e As EventArgs)
        MyBase.OnGotFocus(e) : Invalidate()
    End Sub
    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e) : Invalidate()
    End Sub
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        For i = 0 To 4
            Dim box As New Rectangle(i * Width \ 5 + 3, 4, Math.Max(1, Width \ 5 - 6), Math.Max(1, Height - 8))
            Dim colour = If(Not Enabled, SystemColors.GrayText, If(i = position, LauncherAppearance.Accent, LauncherAppearance.Line))
            If SystemInformation.HighContrast AndAlso Enabled Then colour = If(i = position, SystemColors.Highlight, SystemColors.ControlDark)
            Using brush As New SolidBrush(colour)
                e.Graphics.FillRectangle(brush, box)
            End Using
        Next
        If Focused Then ControlPaint.DrawFocusRectangle(e.Graphics, New Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), ForeColor, BackColor)
    End Sub
End Class

