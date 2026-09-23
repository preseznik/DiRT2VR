Imports System.Drawing
Imports System.Windows.Forms

' Native trackbar plus an always-visible value; arrow keys retain single-step precision.
Public Class ValueSlider
    Inherits UserControl
    Private ReadOnly track As New TrackBar With {.Dock = DockStyle.Fill, .AutoSize = False, .TickStyle = TickStyle.None, .SmallChange = 1, .LargeChange = 5, .Margin = New Padding(0)}
    Private ReadOnly valueText As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Right, .TextAlign = ContentAlignment.MiddleRight, .Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly suffix As String
    Private ReadOnly divisor As Decimal
    Private ReadOnly labels As String()
    Public Event ValueChanged As EventHandler
    Public Sub New(controlName As String, low As Integer, high As Integer, initial As Integer, Optional unit As String = "", Optional displayDivisor As Decimal = 1D, Optional valueLabels As String() = Nothing)
        If displayDivisor <= 0D Then Throw New ArgumentOutOfRangeException(NameOf(displayDivisor))
        If valueLabels IsNot Nothing AndAlso valueLabels.Length <> high - low + 1 Then Throw New ArgumentException("One label is required per slider value.", NameOf(valueLabels))
        labels = valueLabels
        Name = controlName : suffix = unit : divisor = displayDivisor
        Height = 38 : Width = 280 : MinimumSize = New Size(150, 38) : Dock = DockStyle.Top
        TabStop = False
        track.Name = controlName & "Slider" : track.AccessibleName = controlName
        valueText.Name = controlName & "Value"
        track.Minimum = low : track.Maximum = high : track.Value = initial
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(track, 0, 0) : layout.Controls.Add(valueText, 1, 0)
        Controls.Add(layout)
        AddHandler track.ValueChanged, Sub()
                                           RefreshValue()
                                           RaiseEvent ValueChanged(Me, EventArgs.Empty)
                                       End Sub
        RefreshValue()
    End Sub
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property Value As Integer
        Get
            Return track.Value
        End Get
        Set(value As Integer)
            track.Value = value
        End Set
    End Property
    Private Sub RefreshValue()
        valueText.Text = If(labels Is Nothing, (track.Value / divisor).ToString(If(divisor = 1D, "0", "0.0")) & suffix, labels(track.Value - track.Minimum))
        AccessibleDescription = valueText.Text
    End Sub
    Protected Overrides Sub OnBackColorChanged(e As EventArgs)
        MyBase.OnBackColorChanged(e)
        If track IsNot Nothing Then track.BackColor = BackColor
    End Sub
End Class
