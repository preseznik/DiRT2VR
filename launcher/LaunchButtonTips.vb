Imports System.Drawing
Imports System.Windows.Forms

' Disabled WinForms buttons do not receive mouse events. Hit-test only the
' registered launch buttons so their explanation remains available on hover.
Public NotInheritable Class LaunchButtonTips
    Private ReadOnly owner As Form
    Private ReadOnly tips As ToolTip
    Private ReadOnly reasons As New Dictionary(Of Control, String)
    Private ReadOnly defaultTips As New Dictionary(Of Control, String)
    Private hovered As Control
    Private hoverStarted As Long
    Private shown As Boolean

    Public Sub New(owner As Form, tips As ToolTip)
        Me.owner = owner : Me.tips = tips
    End Sub

    Public Sub SetReason(button As Control, reason As String)
        If reasons.ContainsKey(button) AndAlso reasons(button) = reason Then Return
        If Not defaultTips.ContainsKey(button) Then defaultTips(button) = tips.GetToolTip(button)
        reasons(button) = reason
        Dim description = If(reason = "", defaultTips(button), reason)
        button.AccessibleDescription = description
        tips.SetToolTip(button, description)
        If hovered Is button Then Reset()
    End Sub

    Public Sub Poll()
        Dim target As Control = Nothing
        If owner.Visible AndAlso Form.ActiveForm Is owner Then
            Dim pointer = Cursor.Position
            target = reasons.Keys.FirstOrDefault(Function(button) Not button.Enabled AndAlso button.Visible AndAlso
                reasons(button) <> "" AndAlso button.RectangleToScreen(button.ClientRectangle).Contains(pointer))
        End If
        If target IsNot hovered Then
            Reset() : hovered = target : hoverStarted = Environment.TickCount64
        End If
        If target Is Nothing OrElse shown OrElse Environment.TickCount64 - hoverStarted < tips.InitialDelay Then Return
        Dim location = owner.PointToClient(Cursor.Position)
        tips.Show(reasons(target), owner, New Point(location.X, location.Y + 22), tips.AutoPopDelay)
        shown = True
    End Sub

    Private Sub Reset()
        If shown Then tips.Hide(owner)
        hovered = Nothing : shown = False
    End Sub
End Class
