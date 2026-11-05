Imports System.Drawing
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

' Application-local message filter, not an input hook (as in GRID2VR). Mouse-wheel messages
' addressed to a value editor (slider, closed drop-down list or number box) scroll the page that
' contains it instead, so scrolling a long settings page never changes a setting. Clicking,
' dragging and the keyboard still edit values; an open drop-down list keeps its own wheel
' scrolling. No input or window messages are synthesized or sent to other windows, and focus
' never moves.
Public NotInheritable Class SettingsWheelGuard
    Implements IMessageFilter, IDisposable

    Public Const MouseWheel As Integer = &H20A, MouseHWheel As Integer = &H20E
    ' Nothing guards every form of this process.
    Private ReadOnly owner As Form
    Private ReadOnly scrollLines As Func(Of Integer)
    Private ReadOnly dropdownOpen As Func(Of ComboBox, Boolean)
    Private ReadOnly remainders As New ConditionalWeakTable(Of ScrollableControl, WheelRemainder)
    Public ReadOnly Property IsDisposed As Boolean

    Private NotInheritable Class WheelRemainder
        Public Vertical As Integer
        Public Horizontal As Integer
    End Class

    Public Sub New(Optional owner As Form = Nothing, Optional scrollLines As Func(Of Integer) = Nothing, Optional dropdownOpen As Func(Of ComboBox, Boolean) = Nothing)
        Me.owner = owner
        Me.scrollLines = If(scrollLines, New Func(Of Integer)(Function() SystemInformation.MouseWheelScrollLines))
        Me.dropdownOpen = If(dropdownOpen, New Func(Of ComboBox, Boolean)(Function(combo) combo.DroppedDown))
        Application.AddMessageFilter(Me)
    End Sub

    Public Function PreFilterMessage(ByRef message As Message) As Boolean Implements IMessageFilter.PreFilterMessage
        If IsDisposed OrElse (message.Msg <> MouseWheel AndAlso message.Msg <> MouseHWheel) Then Return False
        If owner IsNot Nothing AndAlso (owner.IsDisposed OrElse owner.Disposing) Then Return False
        Dim target = Control.FromChildHandle(message.HWnd)
        If target Is Nothing OrElse target.IsDisposed Then Return False
        Dim form = target.FindForm()
        If form Is Nothing OrElse (owner IsNot Nothing AndAlso form IsNot owner) Then Return False
        ' Number boxes and editable drop-downs have native child windows: resolve the editor from
        ' the message's ancestry, not just its immediate handle.
        Dim editor As Control = target
        While editor IsNot Nothing AndAlso editor IsNot form AndAlso Not (TypeOf editor Is TrackBar OrElse TypeOf editor Is ComboBox OrElse TypeOf editor Is NumericUpDown)
            editor = editor.Parent
        End While
        If editor Is Nothing OrElse editor Is form Then Return False
        Dim combo = TryCast(editor, ComboBox)
        If combo IsNot Nothing AndAlso dropdownOpen(combo) Then Return False
        Dim word = CInt((message.WParam.ToInt64() >> 16) And &HFFFFL)
        ScrollPage(editor, If(word >= &H8000, word - &H10000, word), message.Msg = MouseHWheel)
        message.Result = IntPtr.Zero
        Return True ' Even a page that cannot scroll must not change a value.
    End Function

    Private Sub ScrollPage(editor As Control, delta As Integer, horizontal As Boolean)
        If delta = 0 Then Return
        Dim direction = If(horizontal, delta, -delta)
        Dim parent = editor.Parent
        While parent IsNot Nothing
            Dim page = TryCast(parent, ScrollableControl)
            If page IsNot Nothing AndAlso page.AutoScroll AndAlso Not page.IsDisposed Then
                Dim scroll As ScrollProperties = If(horizontal, DirectCast(page.HorizontalScroll, ScrollProperties), page.VerticalScroll)
                Dim minimum = scroll.Minimum
                Dim maximum = Math.Max(minimum, scroll.Maximum - scroll.LargeChange + 1)
                Dim current = scroll.Value
                If scroll.Visible AndAlso scroll.Enabled AndAlso maximum > minimum AndAlso Not If(direction > 0, current >= maximum, current <= minimum) Then
                    Dim remainder = remainders.GetOrCreateValue(page)
                    Dim lines = scrollLines()
                    If lines = 0 Then ' Windows' wheel scrolling is turned off.
                        If horizontal Then remainder.Horizontal = 0 Else remainder.Vertical = 0
                        Return
                    End If
                    ' High-resolution wheels and touchpads send small deltas: keep the remainder per page.
                    Dim total = If(horizontal, remainder.Horizontal, remainder.Vertical) + direction
                    Dim steps = total \ SystemInformation.MouseWheelScrollDelta
                    Dim pending = total Mod SystemInformation.MouseWheelScrollDelta
                    If horizontal Then remainder.Horizontal = pending Else remainder.Vertical = pending
                    If steps = 0 Then Return
                    Dim unit As Long = If(lines < 0, scroll.LargeChange, Math.Min(Integer.MaxValue, CLng(scroll.SmallChange) * lines))
                    Dim [next] = CInt(Math.Clamp(current + steps * unit, minimum, maximum))
                    Dim position = page.AutoScrollPosition
                    page.AutoScrollPosition = If(horizontal, New Point([next], -position.Y), New Point(-position.X, [next]))
                    Return
                End If
            End If
            parent = parent.Parent
        End While
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If IsDisposed Then Return
        _IsDisposed = True
        Application.RemoveMessageFilter(Me)
        remainders.Clear()
    End Sub
End Class
