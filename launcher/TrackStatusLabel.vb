Imports System.Drawing
Imports System.Windows.Forms

' Keep progress prominent in either appearance, including after theme refreshes.
Public Class TrackStatusLabel
    Inherits Label
    Private ReadOnly pulseTimer As New Timer With {.Interval = 80}
    Private attention As Boolean
    Private updatingInk As Boolean
    Private pulseStarted As Long
    <DllImport("user32.dll", EntryPoint:="SystemParametersInfoW")>
    Private Shared Function SystemParametersInfo(action As UInteger, parameter As UInteger, ByRef value As Integer, flags As UInteger) As Boolean
    End Function
    Public Sub New()
        DoubleBuffered = True
        AddHandler pulseTimer.Tick, Sub()
                                        RefreshPulse()
                                        UpdateInk()
                                    End Sub
    End Sub
    <System.ComponentModel.DefaultValue(False)>
    Public Property Pulse As Boolean
        Get
            Return attention
        End Get
        Set(value As Boolean)
            If attention = value Then Return
            attention = value : RefreshPulse() : UpdateInk()
        End Set
    End Property
    Private Sub RefreshPulse()
        If pulseTimer Is Nothing OrElse IsDisposed OrElse Disposing Then Return
        Dim animations As Integer
        Dim animate = attention AndAlso IsHandleCreated AndAlso Visible AndAlso Enabled AndAlso
            Not SystemInformation.HighContrast AndAlso SystemParametersInfo(&H1042UI, 0, animations, 0) AndAlso animations <> 0
        If animate AndAlso Not pulseTimer.Enabled Then pulseStarted = Environment.TickCount64
        pulseTimer.Enabled = animate
    End Sub
    Private Sub UpdateInk()
        If updatingInk Then Return
        Dim ink = If(SystemInformation.HighContrast, SystemColors.ControlText, If(BackColor.GetBrightness() < 0.5F, Color.FromArgb(150, 230, 110), Color.FromArgb(30, 110, 25)))
        If pulseTimer IsNot Nothing AndAlso pulseTimer.Enabled Then
            ' A slow brightness pulse, never an on/off blink. Repaint this label only.
            Dim amount = (1 - Math.Cos((Environment.TickCount64 - pulseStarted) * Math.PI * 2 / 2400)) / 2
            Dim peak = If(BackColor.GetBrightness() < 0.5F, Color.FromArgb(205, 255, 160), Color.FromArgb(40, 130, 30))
            ink = Color.FromArgb(CInt(ink.R + (peak.R - ink.R) * amount), CInt(ink.G + (peak.G - ink.G) * amount), CInt(ink.B + (peak.B - ink.B) * amount))
        End If
        updatingInk = True
        Try
            If ForeColor <> ink Then ForeColor = ink
        Finally
            updatingInk = False
        End Try
    End Sub
    Protected Overrides Sub OnForeColorChanged(e As EventArgs)
        MyBase.OnForeColorChanged(e) : RefreshPulse() : UpdateInk()
    End Sub
    Protected Overrides Sub OnBackColorChanged(e As EventArgs)
        MyBase.OnBackColorChanged(e) : UpdateInk()
    End Sub
    Protected Overrides Sub OnVisibleChanged(e As EventArgs)
        MyBase.OnVisibleChanged(e) : RefreshPulse() : UpdateInk()
    End Sub
    Protected Overrides Sub OnEnabledChanged(e As EventArgs)
        MyBase.OnEnabledChanged(e) : RefreshPulse() : UpdateInk()
    End Sub
    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e) : RefreshPulse()
    End Sub
    Protected Overrides Sub OnHandleDestroyed(e As EventArgs)
        If Not Disposing Then pulseTimer.Stop()
        MyBase.OnHandleDestroyed(e)
    End Sub
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then pulseTimer.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
