Imports System.Drawing
Imports System.Windows.Forms

Public Class BindingColumns
    Inherits Panel
    Private ReadOnly headings As Label() = {New Label With {.Text = "ACTION"}, New Label With {.Text = "KEYBOARD"}, New Label With {.Text = "CONTROLLER / WHEEL"}}
    Public Sub New()
        Margin = New Padding(0)
        For Each label In headings
            label.AutoSize = False : label.Font = New Font("Segoe UI", 8.5F, FontStyle.Bold)
            Controls.Add(label)
        Next
    End Sub
    Public Sub RefreshStyle()
        PerformLayout()
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If headings Is Nothing Then Return
        Dim show = LauncherAppearance.Modern AndAlso Width >= Px(Me, 540)
        Height = If(show, Px(Me, 30), 0)
        For Each label In headings
            label.Visible = show
        Next
        Dim first = CInt(Width * 0.30), second = CInt(Width * 0.50)
        headings(0).SetBounds(0, 5, first, Px(Me, 22))
        headings(1).SetBounds(first, 5, second - first, Px(Me, 22))
        headings(2).SetBounds(second, 5, Width - second, Px(Me, 22))
    End Sub
End Class

' Classic and compact editors share the same binding collection and capture path.
Public Class ControllerBindingCell
    Inherits Panel
    Private ReadOnly classic As Control
    Private ReadOnly compact As New Panel
    Private ReadOnly add As New Button With {.Text = "Unassigned  +", .AccessibleName = "Add controller binding", .Name = "AddControllerBinding"}
    Private ReadOnly actionName As String
    Private ReadOnly tips As New ToolTip
    Private rows As String() = {}
    Private assignments As ControllerBinding() = {}
    Private modern As Boolean
    Private arranging As Boolean
    Public Event AddBinding As EventHandler
    Public Event EditBinding(binding As ControllerBinding)
    Public Event RemoveBinding(binding As ControllerBinding)
    Public Sub New(action As String, previous As Control)
        actionName = action : classic = previous : classic.Dock = DockStyle.None
        Margin = New Padding(0) : Controls.AddRange({classic, compact}) : compact.Controls.Add(add)
        add.AccessibleName = "Add " & action & " controller binding"
        tips.SetToolTip(add, "Add a button or two-button combination. Existing assignments are kept.")
        AddHandler add.Click, Sub() RaiseEvent AddBinding(Me, EventArgs.Empty)
        AddHandler classic.SizeChanged, Sub() PerformLayout()
        AddHandler Disposed, Sub() tips.Dispose()
    End Sub
    Public Sub SetModern(value As Boolean)
        If modern = value AndAlso compact.Visible = value Then Return
        modern = value : classic.Visible = Not value : compact.Visible = value
        PerformLayout()
    End Sub
    Public Sub UpdateBindings(values As ControllerBinding(), labels As String())
        ' Polling can be frequent. Unchanged bindings never rebuild or repaint rows.
        If rows.SequenceEqual(labels) AndAlso assignments.SequenceEqual(values) Then Return
        If assignments.SequenceEqual(values) Then
            Dim buttons = compact.Controls.OfType(Of Button)().Where(Function(b) CStr(b.Tag) = "binding").ToArray()
            For i = 0 To labels.Length - 1
                If rows(i) = labels(i) Then Continue For
                buttons(i).Text = labels(i) : buttons(i).AccessibleName = actionName & ": " & labels(i)
                tips.SetToolTip(buttons(i), labels(i) & Environment.NewLine & "Click to replace this assignment.")
            Next
            rows = labels : Return
        End If
        rows = labels : assignments = values
        compact.SuspendLayout()
        Try
            For Each control In compact.Controls.Cast(Of Control)().Where(Function(c) c IsNot add).ToArray()
                compact.Controls.Remove(control) : control.Dispose()
            Next
            For i = 0 To values.Length - 1
                Dim binding = values(i)
                Dim edit As New Button With {.Text = labels(i), .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True, .AccessibleName = actionName & ": " & labels(i), .Tag = "binding"}
                Dim remove As New Button With {.Text = "×", .AccessibleName = "Remove " & actionName & ": " & labels(i), .Tag = "remove"}
                tips.SetToolTip(edit, labels(i) & Environment.NewLine & "Click to replace this assignment.")
                tips.SetToolTip(remove, "Remove this assignment.")
                AddHandler edit.Click, Sub() RaiseEvent EditBinding(binding)
                AddHandler remove.Click, Sub() RaiseEvent RemoveBinding(binding)
                compact.Controls.Add(edit) : compact.Controls.Add(remove)
                LauncherAppearance.Apply(edit) : LauncherAppearance.Apply(remove)
            Next
            add.Text = If(values.Length = 0, "Unassigned  +", "+")
        Finally
            compact.ResumeLayout(False)
        End Try
        PerformLayout()
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse classic Is Nothing Then Return
        arranging = True
        Try
            If Not modern Then
                classic.SetBounds(0, 0, Width, classic.PreferredSize.Height)
                Height = classic.Height : Return
            End If
            Dim rowHeight = Px(Me, 34), gap = Px(Me, 5), y = 0, buttonWidth = Px(Me, 32)
            Dim fields = compact.Controls.Cast(Of Control)().Where(Function(c) c IsNot add).ToArray()
            For i = 0 To fields.Length - 1 Step 2
                fields(i).SetBounds(0, y, Math.Max(1, Width - buttonWidth * 2 - gap * 2), rowHeight)
                fields(i + 1).SetBounds(fields(i).Right + gap, y, buttonWidth, rowHeight)
                y += rowHeight + gap
            Next
            add.SetBounds(If(fields.Length = 0, 0, Width - buttonWidth), 0, If(fields.Length = 0, Width, buttonWidth), rowHeight)
            Height = Math.Max(rowHeight, y - gap) : compact.SetBounds(0, 0, Width, Height)
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, Height)
    End Function
End Class


Public Class BindingKeyCell
    Inherits Panel
    Private arranging As Boolean
    Public Sub New()
        Margin = New Padding(0)
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If arranging OrElse Controls.Count = 0 Then Return
        arranging = True
        Try
            Dim main = DirectCast(Controls(0), Button), rowHeight = Math.Max(Px(Me, 32), main.PreferredSize.Height)
            main.AutoSize = False
            Dim clearWidth = If(Controls.Count > 1, Px(Me, 30), 0), gap = If(clearWidth > 0, Px(Me, 5), 0)
            main.SetBounds(0, 0, Math.Max(1, Width - clearWidth - gap), rowHeight)
            If clearWidth > 0 Then
                Controls(1).AutoSize = False : Controls(1).SetBounds(main.Right + gap, 0, clearWidth, rowHeight)
            End If
            Height = rowHeight
        Finally
            arranging = False
        End Try
    End Sub
    Public Overrides Function GetPreferredSize(proposedSize As Size) As Size
        Return New Size(Width, If(Controls.Count = 0, Px(Me, 32), Math.Max(Px(Me, 32), Controls(0).PreferredSize.Height)))
    End Function
End Class
