Imports System.Drawing
Imports System.Windows.Forms

' Launcher settings added since the last published release show a green "new"
' beside them in Experimental builds. When publishing a release, set
' LastPublished to it so earlier marks disappear. Stable builds never show marks.
Public Module NewFeatures
    Public Const LastPublished As String = "0.18.0"
    ' Control name -> the published release it was added after.
    Private ReadOnly addedAfter As New Dictionary(Of String, String) From {
        {"ExtendedGrass", "0.18.0"}, {"VrPerformanceDisplay", "0.18.0"}, {"VrPaddockZoom", "0.18.0"}, {"DrivingTransmission", "0.18.0"},
        {"DrivingSavePreset", "0.18.0"}, {"DrivingLoadPreset", "0.18.0"}, {"DrivingWheelRotation", "0.18.0"}}
    ' The running build's channel; tests may set it.
    Public Property Channel As String = BuildInfo.Channel
    Public Function IsNew(name As String) As Boolean
        Dim after As String = Nothing
        Return Channel = "Experimental" AndAlso name IsNot Nothing AndAlso addedAfter.TryGetValue(name, after) AndAlso after = LastPublished
    End Function
    ' Adds a mark after a control in a flowing row when that control is new.
    Public Sub MarkIfNew(panel As FlowLayoutPanel, control As Control)
        If IsNew(control.Name) Then panel.Controls.Add(New NewMark With {.Margin = New Padding(2, control.Margin.Top + 8, 6, 0)})
    End Sub
End Module

Public Class NewMark
    Inherits Label
    Public Sub New()
        Name = "NewMark" : Text = "new" : AutoSize = True : UseMnemonic = False : Margin = New Padding(0)
        Font = New Font("Segoe UI", 8.25F, FontStyle.Bold)
        AccessibleName = "New since the last release"
    End Sub
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        ' Theme changes reset ForeColor on every label; always draw in the accent.
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, If(SystemInformation.HighContrast, SystemColors.Highlight, LauncherAppearance.Accent),
                              TextFormatFlags.Left Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
    End Sub
End Class
