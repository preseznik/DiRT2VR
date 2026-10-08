Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

Public Class MouseLookPicker
    Inherits TableLayoutPanel
    Private ReadOnly choices As New List(Of MouseChoice)
    Public Sub New()
        Name = "ChaseMouseMode" : AccessibleName = "Mouse look activation"
        AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink
        ColumnCount = 3 : RowCount = 1 : Margin = New Padding(0)
        For Each mode In {"left", "right", "always"}
            ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / 3))
            Dim button As New MouseChoice(mode) With {.Dock = DockStyle.Fill}
            choices.Add(button) : Controls.Add(button)
        Next
        SelectedMode = "right"
    End Sub
    <System.ComponentModel.DefaultValue("right")>
    Public Property SelectedMode As String
        Get
            Return choices.Single(Function(c) c.Checked).Mode
        End Get
        Set(value As String)
            Dim selected = choices.SingleOrDefault(Function(c) c.Mode = value)
            If selected Is Nothing Then Throw New ArgumentException("Unknown mouse look mode.")
            selected.Checked = True
        End Set
    End Property

    Private Class MouseChoice
        Inherits RadioButton
        Public ReadOnly Mode As String
        Public Sub New(value As String)
            Mode = value : Name = "ChaseMouse_" & value
            Text = If(value = "always", "Always on", "Hold " & value)
            AccessibleName = "Mouse look: " & Text
            AccessibleDescription = If(value = "always", "Move the mouse without holding a button.", "Hold the " & value & " mouse button while moving.")
            Appearance = Appearance.Button : FlatStyle = FlatStyle.Flat
            TextImageRelation = TextImageRelation.ImageAboveText
            TextAlign = ContentAlignment.MiddleCenter : ImageAlign = ContentAlignment.MiddleCenter
            AutoSize = True : Margin = New Padding(2) : Padding = New Padding(5)
            RefreshIcon()
        End Sub
        Private Sub RefreshIcon()
            If Mode Is Nothing Then Return
            Dim accent = If(SystemInformation.HighContrast, SystemColors.Highlight, LauncherAppearance.Accent)
            FlatAppearance.BorderColor = If(Checked, accent, LauncherAppearance.Line)
            FlatAppearance.BorderSize = If(Checked, 2, 1)
            FlatAppearance.CheckedBackColor = BackColor
            Dim bitmap As New Bitmap(Px(Me, 30), Px(Me, 34))
            Using g = Graphics.FromImage(bitmap), outline As New Pen(If(Enabled, ForeColor, SystemColors.GrayText), 1.5F), fill As New SolidBrush(If(Enabled, accent, SystemColors.GrayText))
                g.ScaleTransform(DeviceDpi / 96.0F, DeviceDpi / 96.0F)
                g.SmoothingMode = SmoothingMode.AntiAlias
                Using body As New GraphicsPath()
                    body.AddArc(3, 1, 24, 22, 180, 180)
                    body.AddArc(3, 11, 24, 22, 0, 180) : body.CloseFigure()
                    Dim state = g.Save()
                    g.SetClip(body)
                    If Mode = "left" OrElse Mode = "always" Then g.FillRectangle(fill, 3, 1, 12, 14)
                    If Mode = "right" OrElse Mode = "always" Then g.FillRectangle(fill, 15, 1, 12, 14)
                    g.Restore(state)
                    g.DrawPath(outline, body)
                End Using
                g.DrawLine(outline, 15, 1, 15, 15) : g.DrawLine(outline, 3, 15, 27, 15)
                g.DrawRectangle(outline, 13, 6, 4, 6)
                If Mode = "always" Then
                    g.DrawLines(outline, {New Point(10, 21), New Point(7, 24), New Point(10, 27)})
                    g.DrawLines(outline, {New Point(20, 21), New Point(23, 24), New Point(20, 27)})
                End If
            End Using
            Dim previous = Image : Image = bitmap : previous?.Dispose()
        End Sub
        Protected Overrides Sub OnCheckedChanged(e As EventArgs)
            MyBase.OnCheckedChanged(e) : RefreshIcon()
        End Sub
        Protected Overrides Sub OnForeColorChanged(e As EventArgs)
            MyBase.OnForeColorChanged(e) : RefreshIcon()
        End Sub
        Protected Overrides Sub OnBackColorChanged(e As EventArgs)
            MyBase.OnBackColorChanged(e) : RefreshIcon()
        End Sub
        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            MyBase.OnEnabledChanged(e) : RefreshIcon()
        End Sub
        Protected Overrides Sub OnDpiChangedAfterParent(e As EventArgs)
            MyBase.OnDpiChangedAfterParent(e) : RefreshIcon()
        End Sub
        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                Dim previous = Image : Image = Nothing : previous?.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class
End Class
