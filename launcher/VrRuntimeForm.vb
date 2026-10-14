Imports System.Drawing
Imports System.Windows.Forms

Public Class VrRuntimeForm
    Inherits LauncherForm

    Private ReadOnly systemRuntime As New RadioButton With {.Text = "OpenXR — system default", .Name = "ChooseOpenXR", .AutoSize = True}
    Private ReadOnly steamRuntime As New RadioButton With {.Text = "SteamVR", .Name = "ChooseSteamVR", .AutoSize = True, .Checked = True}
    Private ReadOnly remember As New CheckBox With {.Text = "Remember my choice", .Name = "RememberRuntime", .AutoSize = True, .Checked = True}

    Public ReadOnly Property SelectedMode As String
        Get
            Return If(systemRuntime.Checked, "openxr", "steamvr")
        End Get
    End Property

    Public ReadOnly Property RememberChoice As Boolean
        Get
            Return remember.Checked
        End Get
    End Property

    Public Sub New()
        Text = "Choose your VR runtime"
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterScreen
        MinimizeBox = False : MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.AutoSize = True, .Padding = New Padding(20), .ColumnCount = 1}
        layout.Controls.Add(New Label With {.Text = "Choose how to connect to your headset", .AutoSize = True, .Font = New Font(Font, FontStyle.Bold)})
        systemRuntime.Margin = New Padding(3, 16, 3, 3)
        layout.Controls.Add(systemRuntime)
        layout.Controls.Add(New Label With {.Text = VrRuntime.SystemDescription(), .Name = "DetectedRuntime", .AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(24, 3, 3, 12)})
        layout.Controls.Add(steamRuntime)
        layout.Controls.Add(New Label With {.Text = "Start your chosen headset software and connect your headset before launching.", .AutoSize = True, .MaximumSize = New Size(460, 0), .Margin = New Padding(3, 12, 3, 12)})
        layout.Controls.Add(remember)
        layout.Controls.Add(New Label With {.Text = "Change this later in Settings → VR runtime.", .AutoSize = True, .Margin = New Padding(3, 6, 3, 3)})
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Fill, .Margin = New Padding(0, 16, 0, 0)}
        Dim launch As New Button With {.Text = "Launch VR", .Name = "ConfirmRuntime", .AutoSize = True, .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.Text = "Cancel", .Name = "CancelRuntime", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        buttons.Controls.AddRange({launch, cancel}) : layout.Controls.Add(buttons)
        Controls.Add(layout)
        AcceptButton = launch : CancelButton = cancel
    End Sub
End Class
