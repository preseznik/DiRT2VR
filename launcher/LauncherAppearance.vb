Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms
Imports Microsoft.Win32

' One presentation layer; controls, data and event handlers survive style changes.
Public Module LauncherAppearance
    Public Property Modern As Boolean
    Public ReadOnly Property Dark As Boolean
        Get
            Return Application.IsDarkModeEnabled
        End Get
    End Property
    Public ReadOnly Property Surface As Color
        Get
            Return If(Dark, Color.FromArgb(23, 28, 32), Color.FromArgb(247, 249, 245))
        End Get
    End Property
    Public ReadOnly Property Cell As Color
        Get
            Return If(Dark, Color.FromArgb(35, 42, 47), Color.White)
        End Get
    End Property
    Public ReadOnly Property Ink As Color
        Get
            Return If(Dark, Color.FromArgb(236, 240, 242), Color.FromArgb(30, 38, 31))
        End Get
    End Property
    Public ReadOnly Property Line As Color
        Get
            Return If(Dark, Color.FromArgb(66, 76, 82), Color.FromArgb(185, 195, 181))
        End Get
    End Property
    Public ReadOnly Property Accent As Color
        Get
            Return If(Dark, Color.FromArgb(183, 232, 49), Color.FromArgb(66, 102, 0))
        End Get
    End Property
    Private Class OriginalStyle
        Public Padding As Padding
        Public Flat As FlatStyle, Visual As Boolean
        Public Sub New(control As Control)
            Padding = control.Padding
            If TypeOf control Is Button Then
                Flat = DirectCast(control, Button).FlatStyle : Visual = DirectCast(control, Button).UseVisualStyleBackColor
            End If
        End Sub
    End Class
    Private ReadOnly originals As New ConditionalWeakTable(Of Control, OriginalStyle)
    Public Sub Apply(root As Control)
        Dim original = originals.GetValue(root, Function(c) New OriginalStyle(c))
        Dim styled = Modern AndAlso Not SystemInformation.HighContrast
        root.SuspendLayout()
        Try
            If styled Then
                root.BackColor = If(TypeOf root Is TextBoxBase OrElse TypeOf root Is ListControl OrElse TypeOf root Is ListView, Cell, Surface)
                root.ForeColor = Ink
            Else
                Dim inputSurface = TypeOf root Is TextBoxBase OrElse TypeOf root Is ListControl OrElse TypeOf root Is ListView
                root.BackColor = If(Dark AndAlso Not SystemInformation.HighContrast, Color.FromArgb(32, 32, 32), If(inputSurface, SystemColors.Window, SystemColors.Control))
                root.ForeColor = If(Dark AndAlso Not SystemInformation.HighContrast, Color.White, If(inputSurface, SystemColors.WindowText, SystemColors.ControlText))
            End If
            If TypeOf root Is Button Then
                Dim button = DirectCast(root, Button)
                button.FlatStyle = If(styled, FlatStyle.Flat, original.Flat)
                button.Padding = If(styled AndAlso button.Name <> "HelpAbout", New Padding(Px(root, 9), Px(root, 3), Px(root, 9), Px(root, 3)), original.Padding)
                button.UseVisualStyleBackColor = Not styled AndAlso original.Visual
                If styled Then
                    button.BackColor = Cell : button.FlatAppearance.BorderColor = Line
                    button.FlatAppearance.MouseOverBackColor = If(Dark, Color.FromArgb(48, 60, 65), Color.FromArgb(231, 239, 218))
                    button.FlatAppearance.MouseDownBackColor = If(Dark, Color.FromArgb(61, 73, 80), Color.FromArgb(218, 231, 196))
                    If button.Name = "LaunchVR" Then
                        button.BackColor = Color.FromArgb(183, 232, 49) : button.ForeColor = Color.FromArgb(25, 35, 8)
                    ElseIf button.Name = "SaveSettings" Then
                        button.FlatAppearance.BorderColor = Accent
                    End If
                End If
            End If
            If TypeOf root Is LinkLabel AndAlso SystemInformation.HighContrast Then DirectCast(root, LinkLabel).LinkColor = SystemColors.HotTrack
            If TypeOf root Is ModernTabs Then DirectCast(root, ModernTabs).UseModernStyle = styled
            If TypeOf root Is BindingRow Then DirectCast(root, BindingRow).UseModernStyle = Modern
            If TypeOf root Is BindingColumns Then DirectCast(root, BindingColumns).RefreshStyle()
            If TypeOf root Is ControllerBindingCell Then DirectCast(root, ControllerBindingCell).SetModern(Modern)
            For Each child As Control In root.Controls
                Apply(child)
            Next
        Finally
            root.ResumeLayout(True)
        End Try
        ' No global Refresh/Update, including during device polling or slider movement.
        root.Invalidate()
    End Sub
End Module

Public Class LauncherForm
    Inherits Form
    Protected Overrides Sub OnLoad(e As EventArgs)
        LauncherAppearance.Apply(Me)
        AddHandler SystemEvents.UserPreferenceChanged, AddressOf PreferencesChanged
        MyBase.OnLoad(e)
    End Sub
    Private Sub PreferencesChanged(sender As Object, e As UserPreferenceChangedEventArgs)
        If Not {UserPreferenceCategory.Color, UserPreferenceCategory.VisualStyle, UserPreferenceCategory.Accessibility, UserPreferenceCategory.General}.Contains(e.Category) Then Return
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(Sub()
                            If Not IsDisposed Then LauncherAppearance.Apply(Me)
                        End Sub)
        Catch ex As InvalidOperationException
            ' A window may close while Windows is delivering its theme notification.
        End Try
    End Sub
    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        RemoveHandler SystemEvents.UserPreferenceChanged, AddressOf PreferencesChanged
        MyBase.OnFormClosed(e)
    End Sub
End Class

' A real CheckBox retains Space, focus, accessibility and checked-state semantics.
Public Class AppearanceSwitch
    Inherits CheckBox
    Public Sub New()
        Name = "ModernInterface" : AccessibleName = "Modern interface"
        AutoSize = False : Size = New Size(220, 34)
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
        UpdateText()
    End Sub
    Protected Overrides Sub OnCheckedChanged(e As EventArgs)
        UpdateText() : MyBase.OnCheckedChanged(e) : Invalidate()
    End Sub
    Private Sub UpdateText()
        Text = If(Checked, "On · Modern", "Off · Classic")
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Px(Me, 225), Px(Me, 34))
    End Function
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        e.Graphics.Clear(BackColor) : e.Graphics.SmoothingMode = SmoothingMode.AntiAlias
        Dim pillHeight = Px(Me, 22), pillWidth = Px(Me, 42), y = (Height - pillHeight) \ 2
        Dim color = If(SystemInformation.HighContrast, If(Checked, SystemColors.Highlight, SystemColors.GrayText), If(Checked, LauncherAppearance.Accent, LauncherAppearance.Line))
        Using path As New GraphicsPath(), brush As New SolidBrush(color)
            path.AddArc(1, y, pillHeight, pillHeight, 90, 180) : path.AddArc(pillWidth - pillHeight, y, pillHeight, pillHeight, 270, 180) : path.CloseFigure()
            e.Graphics.FillPath(brush, path)
        End Using
        Dim inset = Px(Me, 3), diameter = pillHeight - inset * 2
        Using brush As New SolidBrush(If(SystemInformation.HighContrast, SystemColors.HighlightText, Color.White))
            e.Graphics.FillEllipse(brush, If(Checked, pillWidth - inset - diameter, inset), y + inset, diameter, diameter)
        End Using
        TextRenderer.DrawText(e.Graphics, Text, Font, New Rectangle(pillWidth + Px(Me, 10), 0, Math.Max(1, Width - pillWidth - Px(Me, 10)), Height), If(Enabled, ForeColor, SystemColors.GrayText), TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
        If Focused AndAlso ShowFocusCues Then ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, ForeColor, BackColor)
    End Sub
End Class

Public Class ModernTabs
    Inherits TabControl
    Private modern As Boolean
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property UseModernStyle As Boolean
        Get
            Return modern
        End Get
        Set(value As Boolean)
            If modern = value Then Return
            modern = value
            SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer, value)
            DrawMode = If(value, TabDrawMode.OwnerDrawFixed, TabDrawMode.Normal)
            Padding = If(value, New Point(Px(Me, 14), Px(Me, 9)), New Point(Px(Me, 6), Px(Me, 3)))
            Invalidate()
        End Set
    End Property
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        If Not modern Then
            MyBase.OnPaint(e) : Return
        End If
        e.Graphics.Clear(LauncherAppearance.Surface)
        For index = 0 To TabCount - 1
            Dim state = If(index = SelectedIndex AndAlso Focused, DrawItemState.Focus, DrawItemState.None)
            OnDrawItem(New DrawItemEventArgs(e.Graphics, Font, GetTabRect(index), index, state))
        Next
        Using pen As New Pen(LauncherAppearance.Line)
            e.Graphics.DrawRectangle(pen, 0, DisplayRectangle.Top - 1, Width - 1, Height - DisplayRectangle.Top)
        End Using
    End Sub
    Protected Overrides Sub OnDrawItem(e As DrawItemEventArgs)
        If Not modern OrElse e.Index < 0 Then
            MyBase.OnDrawItem(e) : Return
        End If
        Dim selected = e.Index = SelectedIndex
        Using brush As New SolidBrush(LauncherAppearance.Surface)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        TextRenderer.DrawText(e.Graphics, TabPages(e.Index).Text, Font, e.Bounds, If(selected, LauncherAppearance.Accent, LauncherAppearance.Ink), TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
        If selected Then
            Using pen As New Pen(LauncherAppearance.Accent, Px(Me, 3))
                e.Graphics.DrawLine(pen, e.Bounds.Left + 6, e.Bounds.Bottom - 2, e.Bounds.Right - 6, e.Bounds.Bottom - 2)
            End Using
        End If
        If Focused AndAlso selected Then e.DrawFocusRectangle()
    End Sub
End Class
