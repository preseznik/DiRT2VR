Imports System.Drawing
Imports System.Windows.Forms
Imports System.Runtime.InteropServices

' Native trackbar plus an always-visible value; arrow keys retain single-step precision.
Public Class ValueSlider
    Inherits UserControl
    Private ReadOnly track As MarkerTrackBar
    Private ReadOnly valueText As New Label With {.AutoSize = True, .Anchor = AnchorStyles.Right, .TextAlign = ContentAlignment.MiddleRight, .Margin = New Padding(8, 0, 0, 0)}
    Private ReadOnly suffix As String
    Private ReadOnly divisor As Decimal
    Private ReadOnly labels As String()
    Public ReadOnly Property LogicalHeight As Integer
    Public Event ValueChanged As EventHandler
    Public Sub New(controlName As String, low As Integer, high As Integer, initial As Integer, Optional unit As String = "", Optional displayDivisor As Decimal = 1D, Optional valueLabels As String() = Nothing, Optional recommendedValue As Integer? = Nothing)
        If displayDivisor <= 0D Then Throw New ArgumentOutOfRangeException(NameOf(displayDivisor))
        If valueLabels IsNot Nothing AndAlso valueLabels.Length <> high - low + 1 Then Throw New ArgumentException("One label is required per slider value.", NameOf(valueLabels))
        If recommendedValue.HasValue AndAlso (recommendedValue.Value <= low OrElse recommendedValue.Value >= high) Then Throw New ArgumentOutOfRangeException(NameOf(recommendedValue))
        track = New MarkerTrackBar(recommendedValue) With {.Dock = DockStyle.Fill, .AutoSize = False, .TickStyle = If(recommendedValue.HasValue, TickStyle.BottomRight, TickStyle.None), .SmallChange = 1, .LargeChange = 5, .Margin = New Padding(0), .TickFrequency = high - low}
        labels = valueLabels
        Name = controlName : suffix = unit : divisor = displayDivisor
        LogicalHeight = If(recommendedValue.HasValue, 58, 38)
        Height = LogicalHeight : Width = 280 : MinimumSize = New Size(150, LogicalHeight) : Dock = DockStyle.Top
        TabStop = False
        track.Name = controlName & "Slider" : track.AccessibleName = controlName
        valueText.Name = controlName & "Value"
        track.Minimum = low : track.Maximum = high : track.Value = initial
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(track, 0, 0) : layout.Controls.Add(valueText, 1, 0)
        If recommendedValue.HasValue Then
            Dim caption As New Label With {.Name = controlName & "Recommendation", .Text = recommendedValue.Value.ToString() & unit & " recommended", .AutoSize = True, .Margin = New Padding(3, 0, 0, 0)}
            layout.RowCount = 2 : layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            layout.Controls.Add(caption, 0, 1) : layout.SetColumnSpan(caption, 2)
            track.AccessibleDescription = caption.Text
        End If
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
    Private Class MarkerTrackBar
        Inherits TrackBar
        Private ReadOnly marker As Integer?
        Public Sub New(value As Integer?)
            marker = value
        End Sub
        <DllImport("user32.dll", EntryPoint:="SendMessageW")>
        Private Shared Function SendMessage(window As IntPtr, message As UInteger, wParam As IntPtr, lParam As IntPtr) As IntPtr
        End Function
        Protected Overrides Sub OnHandleCreated(e As EventArgs)
            MyBase.OnHandleCreated(e)
            RestoreMarker()
        End Sub
        Protected Overrides Sub OnSizeChanged(e As EventArgs)
            MyBase.OnSizeChanged(e)
            RestoreMarker()
        End Sub
        Private Sub RestoreMarker()
            If marker.HasValue AndAlso IsHandleCreated Then
                ' WinForms regenerates native ticks on resize and handle recreation.
                SendMessage(Handle, &H409UI, IntPtr.Zero, IntPtr.Zero) ' TBM_CLEARTICS
                SendMessage(Handle, &H404UI, IntPtr.Zero, New IntPtr(marker.Value)) ' TBM_SETTIC
                Invalidate()
            End If
        End Sub
    End Class
End Class
