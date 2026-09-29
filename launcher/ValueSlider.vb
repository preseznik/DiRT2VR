Imports System.Drawing
Imports System.Windows.Forms
Imports System.Runtime.InteropServices

' Native trackbar plus an always-visible value; arrow keys retain single-step precision.
Public Class ValueSlider
    Inherits UserControl
    Private ReadOnly track As MarkerTrackBar
    Private ReadOnly valueText As New SliderValueLabel With {.AutoSize = True, .Anchor = AnchorStyles.Right, .TextAlign = ContentAlignment.MiddleRight, .Margin = New Padding(8, 0, 0, 0)}
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
        LogicalHeight = 32
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
            track.AccessibleDescription = recommendedValue.Value.ToString() & unit & " recommended (green notch)"
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
    ' WinForms' disabled Label colour can be black even on a dark user-painted
    ' surface. Draw this readout explicitly without changing whether input is enabled.
    Private Class SliderValueLabel
        Inherits Label
        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim ink = If(Enabled, ForeColor, If(SystemInformation.HighContrast, SystemColors.GrayText,
                If(BackColor.GetBrightness() < 0.5F, Color.FromArgb(166, 177, 183), Color.FromArgb(95, 101, 105))))
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ink, TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        End Sub
    End Class
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
        <StructLayout(LayoutKind.Sequential)>
        Private Structure NotifyHeader
            Public Window As IntPtr
            Public Id As UIntPtr
            Public Code As Integer
        End Structure
        <StructLayout(LayoutKind.Sequential)>
        Private Structure NativeRect
            Public Left, Top, Right, Bottom As Integer
        End Structure
        <StructLayout(LayoutKind.Sequential)>
        Private Structure CustomDraw
            Public Header As NotifyHeader
            Public Stage As UInteger
            Public Hdc As IntPtr
            Public Bounds As NativeRect
            Public Item As UIntPtr
            Public State As UInteger
            Public Parameter As IntPtr
        End Structure
        <DllImport("user32.dll", EntryPoint:="SendMessageW")>
        Private Shared Function SendRect(window As IntPtr, message As UInteger, wParam As IntPtr, ByRef rectangle As NativeRect) As IntPtr
        End Function
        Protected Overrides Sub WndProc(ByRef m As Message)
            MyBase.WndProc(m)
            If Not marker.HasValue OrElse m.Msg <> &H204E OrElse m.LParam = IntPtr.Zero Then Return ' reflected WM_NOTIFY
            If Marshal.PtrToStructure(Of NotifyHeader)(m.LParam).Code <> -12 Then Return ' NM_CUSTOMDRAW
            Dim draw = Marshal.PtrToStructure(Of CustomDraw)(m.LParam)
            If draw.Stage = 1 Then ' CDDS_PREPAINT: keep native drawing and request the final HDC.
                m.Result = New IntPtr(m.Result.ToInt64() Or &H10L) ' CDRF_NOTIFYPOSTPAINT
            ElseIf draw.Stage = 2 AndAlso draw.Hdc <> IntPtr.Zero Then
                Dim x = SendMessage(Handle, &H40FUI, IntPtr.Zero, IntPtr.Zero).ToInt32() ' TBM_GETTICPOS
                If x < 0 Then Return
                Dim thumb As NativeRect
                SendRect(Handle, &H419UI, IntPtr.Zero, thumb) ' TBM_GETTHUMBRECT
                Dim y = Math.Min(ClientSize.Height - Px(Me, 8), thumb.Bottom + Px(Me, 1))
                Using canvas = Graphics.FromHdc(draw.Hdc), brush As New SolidBrush(If(SystemInformation.HighContrast, SystemColors.Highlight, If(BackColor.GetBrightness() < 0.5F, Color.FromArgb(183, 232, 49), Color.FromArgb(66, 130, 0))))
                    canvas.FillRectangle(brush, x - Px(Me, 1), Math.Max(0, y), Px(Me, 3), Px(Me, 6))
                End Using
            End If
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
