Imports System.Drawing
Imports System.Windows.Forms

' Keep progress prominent in either appearance, including after theme refreshes.
Public Class TrackStatusLabel
    Inherits Label
    Private Sub UpdateInk()
        Dim ink = If(SystemInformation.HighContrast, SystemColors.ControlText, If(BackColor.GetBrightness() < 0.5F, Color.FromArgb(150, 230, 110), Color.FromArgb(30, 110, 25)))
        If ForeColor <> ink Then ForeColor = ink
    End Sub
    Protected Overrides Sub OnForeColorChanged(e As EventArgs)
        MyBase.OnForeColorChanged(e) : UpdateInk()
    End Sub
    Protected Overrides Sub OnBackColorChanged(e As EventArgs)
        MyBase.OnBackColorChanged(e) : UpdateInk()
    End Sub
End Class
