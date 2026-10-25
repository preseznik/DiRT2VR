Imports System.Drawing
Imports System.Windows.Forms

Public Class VrLayerRecoveryForm
    Inherits LauncherForm

    Private ReadOnly remember As New CheckBox With {.Text = "Remember for this DiRT2VR installation", .Name = "RememberLayerBypass", .AutoSize = True}
    Public ReadOnly Property RememberChoice As Boolean
        Get
            Return remember.Checked
        End Get
    End Property
    Public Sub New(request As VrLayerRecoveryRequest)
        Text = "VR compatibility"
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        AutoSize = True : AutoSizeMode = AutoSizeMode.GrowAndShrink : AutoScroll = True
        MaximumSize = Screen.PrimaryScreen.WorkingArea.Size
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.CenterScreen
        MinimizeBox = False : MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.AutoSize = True, .Padding = New Padding(20), .ColumnCount = 1}
        layout.Controls.Add(New Label With {.Text = "An OpenXR layer is preventing VR from starting", .Name = "LayerFailureTitle", .AutoSize = True, .MaximumSize = New Size(560, 0), .Font = New Font(Font, FontStyle.Bold)})
        Dim description = String.Join(Environment.NewLine & Environment.NewLine, request.Layers.Select(Function(layer) layer.DisplayName & ": " & layer.Consequence))
        layout.Controls.Add(New Label With {.Text = description, .Name = "LayerConsequences", .AutoSize = True, .MaximumSize = New Size(560, 0), .Margin = New Padding(3, 12, 3, 12)})
        layout.Controls.Add(New Label With {.Text = "Retry with these layers disabled for DiRT2VR? Other applications keep their current settings.", .AutoSize = True, .MaximumSize = New Size(560, 0)})
        remember.Margin = New Padding(3, 12, 3, 6)
        layout.Controls.Add(remember)
        layout.Controls.Add(New Label With {.Text = "Remembered choices can be removed in Settings → VR compatibility.", .AutoSize = True, .MaximumSize = New Size(560, 0)})
        Dim details As New TextBox With {.Name = "LayerFailureDetails", .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Both,
            .WordWrap = False, .Width = 560, .Height = 150, .Visible = False, .Text = request.Report}
        layout.Controls.Add(details)
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Fill, .Margin = New Padding(0, 16, 0, 0)}
        Dim retry As New Button With {.Text = "Disable and retry", .Name = "RetryWithoutLayers", .AutoSize = True, .DialogResult = DialogResult.OK}
        Dim cancel As New Button With {.Text = "Cancel", .Name = "CancelLayerRecovery", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        Dim show As New Button With {.Text = "Show details", .Name = "ShowLayerDetails", .AutoSize = True}
        AddHandler show.Click, Sub()
                                   details.Visible = Not details.Visible
                                   show.Text = If(details.Visible, "Hide details", "Show details")
                               End Sub
        buttons.Controls.AddRange({retry, cancel, show}) : layout.Controls.Add(buttons)
        Controls.Add(layout)
        AcceptButton = cancel : CancelButton = cancel
    End Sub
    Public Shared Function Confirm(request As VrLayerRecoveryRequest) As VrLayerRecoveryChoice
        Using dialog As New VrLayerRecoveryForm(request)
            Return New VrLayerRecoveryChoice With {.Retry = dialog.ShowDialog() = DialogResult.OK, .Remember = dialog.RememberChoice}
        End Using
    End Function
End Class

Public Class VrCompatibilityForm
    Inherits LauncherForm

    Private ReadOnly context As InstallContext
    Private ReadOnly layers As New ListBox With {.Name = "RememberedLayerBypasses", .Dock = DockStyle.Fill, .IntegralHeight = False, .HorizontalScrollbar = True}
    Private ReadOnly summary As New Label With {.Name = "LayerBypassSummary", .AutoSize = True, .MaximumSize = New Size(520, 0)}
    Private ReadOnly reenable As New Button With {.Text = "Re-enable selected", .Name = "ReenableLayer", .AutoSize = True}
    Public Sub New(context As InstallContext)
        Me.context = context
        Text = "VR compatibility"
        Font = New Font("Segoe UI", 10) : AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(580, 340) : MinimumSize = New Size(460, 300)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False : MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(16), .ColumnCount = 1, .RowCount = 4}
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(summary, 0, 0)
        layout.Controls.Add(layers, 0, 1)
        layout.Controls.Add(New Label With {.Text = "Changes apply to the next VR launch. Other applications are unaffected. Changed layer registrations require a new approval if they still prevent VR from starting.", .AutoSize = True, .MaximumSize = New Size(520, 0)}, 0, 2)
        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Dock = DockStyle.Fill, .Margin = New Padding(0, 12, 0, 0)}
        Dim close As New Button With {.Text = "Close", .AutoSize = True, .DialogResult = DialogResult.Cancel}
        buttons.Controls.AddRange({close, reenable}) : layout.Controls.Add(buttons, 0, 3)
        AddHandler layers.SelectedIndexChanged, Sub() reenable.Enabled = layers.SelectedItem IsNot Nothing
        AddHandler reenable.Click, Sub()
                                      Dim selected = TryCast(layers.SelectedItem, VrLayerBypass)
                                      If selected Is Nothing Then Return
                                      Try
                                          CustomTrackService.WithSessionLock(Sub() VrLayerPreferences.Reenable(context, selected.LayerName))
                                          RefreshLayers()
                                      Catch ex As Exception
                                          MessageBox.Show(Me, ex.Message, "VR compatibility", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                      End Try
                                  End Sub
        Controls.Add(layout) : CancelButton = close
        AddHandler Shown, Sub() RefreshLayers()
        RefreshLayers()
    End Sub
    Private Sub RefreshLayers()
        layers.Items.Clear()
        layers.Items.AddRange(VrLayerPreferences.Load(context).Bypasses.Cast(Of Object)().ToArray())
        summary.Text = If(layers.Items.Count = 0, "No OpenXR layers are disabled by remembered choices.", "Remembered OpenXR layer bypasses for this installation:")
        reenable.Enabled = False
        If layers.Items.Count > 0 Then layers.SelectedIndex = 0
    End Sub
End Class
