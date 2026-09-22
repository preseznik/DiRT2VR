Imports System.Drawing
Imports System.Windows.Forms

Public Class LanLaunchForm
    Inherits Form

    Public Sub New(joining As Boolean)
        Text = If(joining, "JOIN", "HOST") & " — Launch mode"
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False : MinimizeBox = False : MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.AutoSize = True, .Padding = New Padding(20), .ColumnCount = 1}
        layout.Controls.Add(New Label With {.Text = "How would you like to play?", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        layout.Controls.Add(New Label With {.Text = "VR uses your saved Graphics and Controls settings. Start SteamVR and connect your headset before choosing VR.", .AutoSize = True, .MaximumSize = New Size(450, 0), .Margin = New Padding(3, 12, 3, 12)})
        layout.Controls.Add(New Label With {.Text = "Multiplayer VR has not yet been tested.", .AutoSize = True})
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .Margin = New Padding(0, 16, 0, 0)}
        Dim desktop As New Button With {.Text = "Desktop", .Name = "ChooseDesktop", .AutoSize = True, .DialogResult = DialogResult.No}
        Dim headset As New Button With {.Text = "VR", .Name = "ChooseVR", .AutoSize = True, .DialogResult = DialogResult.Yes}
        Dim cancel As New Button With {.Text = "Cancel", .Name = "CancelLaunch", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        buttons.Controls.AddRange({desktop, headset, cancel}) : layout.Controls.Add(buttons)
        Controls.Add(layout)
        AcceptButton = desktop : CancelButton = cancel
    End Sub
End Class
