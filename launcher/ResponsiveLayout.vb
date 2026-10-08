Imports System.Drawing
Imports System.Windows.Forms

Public Module LauncherLayout
    Public Function Px(control As Control, value As Integer) As Integer
        Return CInt(value * control.DeviceDpi / 96.0)
    End Function
    Public Function Stack() As VerticalStack
        Return New VerticalStack With {.Dock = DockStyle.Top, .Margin = New Padding(0)}
    End Function
    Public Function Section(parent As Control, title As String) As VerticalStack
        Dim panel = Stack()
        panel.Margin = New Padding(0, 0, 0, 14)
        panel.Controls.Add(New Label With {.Text = title, .UseMnemonic = False, .AutoSize = True, .Font = New Font("Segoe UI", 11, FontStyle.Bold), .Margin = New Padding(0, 8, 0, 10)})
        parent.Controls.Add(panel)
        Return panel
    End Function
    Public Sub Field(parent As Control, title As String, value As Control)
        parent.Controls.Add(New SettingRow(title, value))
    End Sub
    Public Sub StyleChoice(box As ComboBox, owner As Control)
        box.FlatStyle = FlatStyle.Flat
        box.DrawMode = DrawMode.OwnerDrawFixed
        Dim updateColors As Action = Sub()
                                         box.BackColor = owner.BackColor : box.ForeColor = owner.ForeColor
                                     End Sub
        AddHandler owner.BackColorChanged, Sub() updateColors()
        AddHandler owner.ForeColorChanged, Sub() updateColors()
        updateColors()
        AddHandler box.DrawItem, AddressOf DrawChoice
        AddHandler box.DropDown, Sub() box.DropDownWidth = Math.Min(Px(box, 600), Screen.FromControl(box).WorkingArea.Width)
    End Sub
    Private Sub DrawChoice(sender As Object, e As DrawItemEventArgs)
        Dim box = DirectCast(sender, ComboBox)
        Dim highlighted = (e.State And DrawItemState.Selected) <> 0 AndAlso (e.State And DrawItemState.ComboBoxEdit) = 0
        Dim background = If(highlighted, SystemColors.Highlight, box.BackColor)
        Dim foreground = If(Not box.Enabled, SystemColors.GrayText, If(highlighted, SystemColors.HighlightText, box.ForeColor))
        Using brush As New SolidBrush(background)
            e.Graphics.FillRectangle(brush, e.Bounds)
        End Using
        If e.Index >= 0 Then
            TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items(e.Index)), box.Font, e.Bounds, foreground, TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End If
        e.DrawFocusRectangle()
    End Sub
    Public Function HelpLink(action As Action) As LinkLabel
        Dim link As New LinkLabel With {.Text = "Instructions", .AutoSize = True, .Margin = New Padding(0, 10, 0, 10), .Name = "InstructionsLink"}
        Dim updateColor As Action = Sub()
                                        link.LinkColor = If(link.BackColor.GetBrightness() < 0.5F, Color.LightSkyBlue, Color.FromArgb(0, 80, 160))
                                        link.ActiveLinkColor = link.LinkColor
                                        link.VisitedLinkColor = link.LinkColor
                                    End Sub
        AddHandler link.BackColorChanged, Sub() updateColor()
        AddHandler link.ForeColorChanged, Sub() updateColor()
        updateColor()
        AddHandler link.LinkClicked, Sub() action()
        Return link
    End Function
End Module

' Explicit vertical flow avoids TableLayoutPanel retaining tall row allocations after reflow.
Public Class VerticalStack
    Inherits Panel
    Private arranging As Boolean
    Protected Overrides Sub OnControlAdded(e As ControlEventArgs)
        e.Control.Dock = DockStyle.None
        AddHandler e.Control.SizeChanged, AddressOf ChildSizeChanged
        MyBase.OnControlAdded(e)
    End Sub
    Protected Overrides Sub OnControlRemoved(e As ControlEventArgs)
        RemoveHandler e.Control.SizeChanged, AddressOf ChildSizeChanged
        MyBase.OnControlRemoved(e)
    End Sub
    Private Sub ChildSizeChanged(sender As Object, e As EventArgs)
        PerformLayout()
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging Then Return
        arranging = True
        Try
            Dim y = Padding.Top
            For Each child As Control In Controls
                Dim width = Math.Max(1, ClientSize.Width - Padding.Horizontal - child.Margin.Horizontal)
                Dim fixedWidth = TypeOf child Is Button OrElse TypeOf child Is LinkLabel
                child.SetBounds(Padding.Left + child.Margin.Left, y + child.Margin.Top, If(fixedWidth, Math.Min(width, child.PreferredSize.Width), width), child.Height)
                If (TypeOf child Is Label AndAlso child.AutoSize) OrElse TypeOf child Is Button OrElse TypeOf child Is FlowLayoutPanel Then
                    child.Height = child.GetPreferredSize(New Size(width, 0)).Height
                End If
                y = child.Bottom + child.Margin.Bottom
            Next
            Height = y + Padding.Bottom
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        ' Scroll layout probes with unconstrained widths (65535). Do not turn that
        ' measurement into a transient giant window for every native child.
        Return New Size(Width, Height)
    End Function
End Class

' Moving bounds preserves control instances, keyboard focus and in-progress input capture.
Public Class SettingRow
    Inherits Panel
    Private ReadOnly caption As Label
    Private ReadOnly editor As Control
    Private arranging As Boolean
    Public Sub New(title As String, value As Control)
        Dock = DockStyle.Top : Margin = New Padding(0) : TabStop = False : AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink
        caption = New Label With {.Text = title, .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .Name = value.Name & "Label"}
        editor = value : editor.Dock = DockStyle.None : editor.Margin = New Padding(0)
        If editor.AccessibleName Is Nothing Then editor.AccessibleName = title
        Controls.Add(caption) : Controls.Add(editor)
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse editor Is Nothing OrElse ClientSize.Width <= 0 Then Return
        arranging = True
        Try
            Dim compact = TypeOf editor Is ValueSlider OrElse TypeOf editor Is ComboBox OrElse TypeOf editor Is MouseLookPicker
            Dim gap = Px(Me, If(compact, 8, 10)), padding = Px(Me, If(compact, 3, 5)), width = ClientSize.Width
            Dim stacked = width < Px(Me, If(compact, 380, 590)) AndAlso Not TypeOf editor Is CheckBox
            Dim labelWidth = If(compact, Math.Min(Px(Me, 170), CInt(width * 0.4)), Math.Min(Px(Me, 215), width \ 2))
            Dim editorWidth = If(stacked, width, width - labelWidth - gap)
            Dim editorHeight = If(TypeOf editor Is ValueSlider, Px(Me, DirectCast(editor, ValueSlider).LogicalHeight), editor.GetPreferredSize(New Size(editorWidth, 0)).Height)
            editorHeight = Math.Max(Px(Me, 28), editorHeight)
            Dim labelHeight = TextRenderer.MeasureText(caption.Text, Font, New Size(If(stacked, width, labelWidth), 0), TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix).Height
            Dim rowHeight = If(stacked, labelHeight + editorHeight + gap, Math.Max(labelHeight, editorHeight))
            caption.SetBounds(0, padding, If(stacked, width, labelWidth), If(stacked, labelHeight, rowHeight))
            editor.SetBounds(If(stacked, 0, labelWidth + gap), padding + If(stacked, labelHeight + gap, (rowHeight - editorHeight) \ 2), editorWidth, editorHeight)
            Height = rowHeight + padding * 2
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, Height)
    End Function
End Class

Public Class ResponsiveColumns
    Inherits Panel
    <System.ComponentModel.DefaultValue(1060)>
    Public Property WideAt As Integer = 1060
    Public ReadOnly First As VerticalStack = Stack()
    Public ReadOnly Second As VerticalStack = Stack()
    Private arranging As Boolean
    Public Sub New()
        Dock = DockStyle.Top : Margin = New Padding(0) : TabStop = False : AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink
        First.Dock = DockStyle.None : Second.Dock = DockStyle.None
        Controls.Add(First) : Controls.Add(Second)
        AddHandler First.SizeChanged, Sub() PerformLayout()
        AddHandler Second.SizeChanged, Sub() PerformLayout()
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse First Is Nothing OrElse Width <= 0 Then Return
        arranging = True
        Try
            Dim wide = Width >= Px(Me, WideAt), gap = Px(Me, 30)
            Dim firstWidth = If(wide, CInt((Width - gap) * 0.54), Width)
            First.SetBounds(0, 0, firstWidth, First.GetPreferredSize(New Size(firstWidth, 0)).Height)
            Dim secondWidth = If(wide, Width - firstWidth - gap, Width)
            Second.SetBounds(If(wide, firstWidth + gap, 0), If(wide, 0, First.Height), secondWidth, Second.GetPreferredSize(New Size(secondWidth, 0)).Height)
            Height = If(wide, Math.Max(First.Height, Second.Height), First.Height + Second.Height)
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, Height)
    End Function
End Class

Public Class LauncherFooter
    Inherits Panel
    Private ReadOnly launch As Button, vr As Button, save As Button, recover As Button, logs As Button
    Private arranging As Boolean
    Public Sub New(desktop As Button, headset As Button, settings As Button, restore As Button, openLogs As Button)
        launch = desktop : vr = headset : save = settings : recover = restore : logs = openLogs
        Dock = DockStyle.Fill : Margin = New Padding(0, 12, 0, 0)
        Controls.AddRange({launch, vr, save, recover, logs})
        For Each button In Controls.OfType(Of Button)()
            AddHandler button.VisibleChanged, Sub() PerformLayout()
        Next
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse launch Is Nothing Then Return
        arranging = True
        Try
            Dim gap = Px(Me, 8)
            For Each button In Controls.OfType(Of Button)()
                button.Size = button.GetPreferredSize(Size.Empty)
            Next
            Dim rowHeight = Controls.OfType(Of Button)().Max(Function(b) b.Height)
            Dim leftWidth = If(launch.Visible, launch.Width + vr.Width + gap, 0)
            Dim utilityWidth = save.Width + recover.Width + logs.Width + gap * 2
            Dim compact = Width < leftWidth + utilityWidth + gap * 2
            launch.Location = New Point(0, 0) : vr.Location = New Point(launch.Right + gap, 0)
            logs.Location = New Point(Width - logs.Width, If(compact, rowHeight + gap, 0))
            recover.Location = New Point(logs.Left - gap - recover.Width, logs.Top)
            save.Location = New Point(If(compact, Width - save.Width, recover.Left - gap - save.Width), 0)
            Height = rowHeight * If(compact, 2, 1) + If(compact, gap, 0) + Px(Me, 4)
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, Height)
    End Function
End Class

Public Class BindingRow
    Inherits Panel
    Private ReadOnly title As Label, keyboardTitle As Label, controllerTitle As Label
    Private ReadOnly key As Control, controller As Control
    Private ReadOnly recommended As Label
    Private modern As Boolean
    <System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)>
    Public Property UseModernStyle As Boolean
        Get
            Return modern
        End Get
        Set(value As Boolean)
            If modern = value Then Return
            modern = value
            Margin = New Padding(0, 0, 0, If(value, 2, 16))
            PerformLayout() : Invalidate()
        End Set
    End Property
    Private arranging As Boolean
    Public Sub New(action As String, keyboard As Control, device As Control, Optional isRecommended As Boolean = False)
        Dock = DockStyle.Top : Margin = New Padding(0, 0, 0, 16) : AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink
        title = New Label With {.Text = action, .AutoSize = True, .Font = New Font("Segoe UI", 10, FontStyle.Bold)}
        keyboardTitle = New Label With {.Text = "Keyboard", .AutoSize = True}
        controllerTitle = New Label With {.Text = "Controller / wheel", .AutoSize = True}
        key = keyboard : controller = device : controller.Dock = DockStyle.None
        Controls.AddRange({title, keyboardTitle, key, controllerTitle, controller})
        If isRecommended Then
            recommended = New Label With {.Text = "RECOMMENDED", .AutoSize = True, .Font = New Font("Segoe UI", 8, FontStyle.Bold)}
            Controls.Add(recommended)
        End If
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint, True)
        AddHandler controller.SizeChanged, Sub() PerformLayout()
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse key Is Nothing Then Return
        arranging = True
        Try
            Dim narrow = Width < Px(Me, If(modern, 540, 650))
            keyboardTitle.Visible = Not modern OrElse narrow
            controllerTitle.Visible = Not modern OrElse narrow
            If modern Then
                Dim first = If(narrow, 0, CInt(Width * 0.30)), second = If(narrow, CInt(Width * 0.40), CInt(Width * 0.50)), modernGap = Px(Me, 6)
                title.MaximumSize = New Size(If(narrow, Width, first - modernGap), 0)
                title.Location = New Point(0, modernGap)
                Dim titleBottom = title.Bottom
                If recommended IsNot Nothing Then
                    recommended.Location = New Point(0, titleBottom + Px(Me, 2))
                    recommended.ForeColor = If(SystemInformation.HighContrast, ForeColor, LauncherAppearance.Accent)
                    titleBottom = recommended.Bottom
                End If
                keyboardTitle.Location = New Point(0, titleBottom + modernGap)
                key.SetBounds(first, If(narrow, keyboardTitle.Bottom + modernGap, modernGap), second - first - modernGap, key.PreferredSize.Height)
                controllerTitle.Location = New Point(second, keyboardTitle.Top)
                controller.SetBounds(second, If(narrow, controllerTitle.Bottom + modernGap, modernGap), Math.Max(1, Width - second), controller.Height)
                controller.PerformLayout()
                Height = Math.Max(titleBottom, Math.Max(key.Bottom, controller.Bottom)) + modernGap * 2
                Return
            End If
            If recommended IsNot Nothing Then recommended.Location = New Point(0, title.Bottom + Px(Me, 3))
            title.MaximumSize = New Size(If(narrow, Math.Max(1, Width), Px(Me, 110)), 0)
            Dim gap = Px(Me, 8), labelHeight = Math.Max(title.PreferredHeight, controllerTitle.PreferredHeight)
            title.Location = Point.Empty
            keyboardTitle.Location = New Point(If(narrow, 0, Px(Me, 120)), If(narrow, labelHeight + gap, 0))
            key.SetBounds(keyboardTitle.Left, keyboardTitle.Bottom + gap, Px(Me, 155), key.PreferredSize.Height)
            controllerTitle.Location = New Point(If(narrow, 0, Px(Me, 295)), If(narrow, key.Bottom + gap, 0))
            controller.SetBounds(controllerTitle.Left, controllerTitle.Bottom + gap, Math.Max(1, Width - controllerTitle.Left), controller.Height)
            controller.PerformLayout()
            Height = controller.Bottom + gap
        Finally
            arranging = False
        End Try
    End Sub
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        If modern Then
            Using pen As New Pen(If(SystemInformation.HighContrast, SystemColors.ControlText, LauncherAppearance.Line))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1)
            End Using
        End If
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, Height)
    End Function
End Class

Public Class LanServerList
    Inherits ListView
    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        MyBase.OnSizeChanged(e)
        If Columns.Count < 4 OrElse ClientSize.Width <= 0 Then Return
        Dim narrow = ClientSize.Width < Px(Me, 650)
        View = If(narrow, View.Tile, View.Details)
        If narrow Then
            TileSize = New Size(Math.Max(1, ClientSize.Width - Px(Me, 24)), Px(Me, 100))
        Else
            Dim available = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
            Columns(0).Width = CInt(available * 0.28) : Columns(1).Width = CInt(available * 0.29)
            Columns(2).Width = CInt(available * 0.13) : Columns(3).Width = available - Columns(0).Width - Columns(1).Width - Columns(2).Width
        End If
    End Sub
End Class

' Keep optional controls alive (including their saved assignments) while collapsed.
Public Class CollapsibleSection
    Inherits Panel
    Public ReadOnly Content As VerticalStack = Stack()
    Private ReadOnly header As Button
    Private ReadOnly title As String
    Private expanded As Boolean
    Private arranging As Boolean
    Public Event Collapsed As EventHandler
    Public Sub New(caption As String)
        title = caption
        Margin = New Padding(0, 0, 0, 12) : TabStop = False
        header = New Button With {.Text = "▶ " & title, .AutoSize = False, .TextAlign = ContentAlignment.MiddleLeft, .AccessibleName = title, .AccessibleDescription = "Collapsed. Activate to show optional seat and panel bindings."}
        Content.Dock = DockStyle.None : Content.Visible = False
        Controls.Add(header) : Controls.Add(Content)
        AddHandler Content.SizeChanged, Sub() PerformLayout()
        AddHandler header.Click, Sub()
                                     expanded = Not expanded
                                     header.Text = If(expanded, "▼ ", "▶ ") & title
                                     header.AccessibleDescription = If(expanded, "Expanded. Activate to hide optional seat and panel bindings.", "Collapsed. Activate to show optional seat and panel bindings.")
                                     Content.Visible = expanded
                                     PerformLayout()
                                     If Not expanded Then RaiseEvent Collapsed(Me, EventArgs.Empty)
                                 End Sub
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse header Is Nothing Then Return
        arranging = True
        Try
            Dim width = Math.Max(1, ClientSize.Width)
            header.SetBounds(0, 0, width, header.GetPreferredSize(New Size(width, 0)).Height)
            Content.SetBounds(0, header.Bottom + Px(Me, 6), width, Content.Height)
            Height = If(expanded, Content.Bottom, header.Bottom)
        Finally
            arranging = False
        End Try
    End Sub
End Class
