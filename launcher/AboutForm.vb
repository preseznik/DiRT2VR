Imports System.Drawing
Imports System.Net.Http
Imports System.Windows.Forms
Imports System.Threading.Tasks

Public Class AboutForm
    Inherits Form
    Private ReadOnly context As InstallContext
    Private ReadOnly client As HttpClient = UpdateService.CreateClient()
    Private ReadOnly cancellation As New CancellationTokenSource()
    Private ReadOnly checkButton As New Button With {.Text = "Check for updates", .AutoSize = True, .Name = "CheckUpdates"}
    Private ReadOnly installButton As New Button With {.Text = "Download and install", .AutoSize = True, .Enabled = False, .Name = "InstallUpdate"}
    Private ReadOnly preview As New CheckBox With {.Text = "Include experimental releases", .AutoSize = True, .Checked = BuildInfo.Version.Contains("-"), .Name = "PreviewUpdates"}
    Private ReadOnly status As New Label With {.AutoSize = True, .MaximumSize = New Size(570, 0), .Text = "Check GitHub Releases for a newer version. No GitHub account is needed.", .Name = "UpdateStatus"}
    Private ReadOnly progress As New ProgressBar With {.Dock = DockStyle.Top, .Visible = False}
    Private availableUpdate As ReleaseUpdate
    Private working As Boolean
    Private ReadOnly helpTabs As New TabControl With {.Dock = DockStyle.Fill, .Name = "HelpTabs"}
    Private ReadOnly instructions As New InstructionsView()
    Public Sub New(value As InstallContext, Optional knownUpdate As ReleaseUpdate = Nothing)
        context = value
        Text = "DiRT2VR — Help / About"
        Using stream = GetType(MainForm).Assembly.GetManifestResourceStream("DiRT2VR.ico"), appIcon As New Icon(stream)
            Icon = DirectCast(appIcon.Clone(), Icon)
        End Using
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(900, Math.Min(820, Screen.PrimaryScreen.WorkingArea.Height - 100)) : MinimumSize = New Size(560, 540)
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False : MinimizeBox = False : MaximizeBox = True
        Dim aboutPage As New TabPage("About") With {.UseVisualStyleBackColor = False, .BackColor = BackColor, .AutoScroll = True}
        Dim instructionsPage As New TabPage("Instructions") With {.UseVisualStyleBackColor = False, .BackColor = BackColor, .Padding = New Padding(16)}
        helpTabs.TabPages.AddRange({aboutPage, instructionsPage})
        instructions.Font = Font : instructions.BackColor = BackColor : instructions.ForeColor = ForeColor
        instructions.ShowTopic("Getting started")
        instructionsPage.Controls.Add(instructions)
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(20), .ColumnCount = 1}
        aboutPage.Controls.Add(layout)
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        AddText(layout, "DiRT2VR", 20, True)
        Dim revision = BuildInfo.FullVersion.Split("+"c).Skip(1).FirstOrDefault()
        Dim build = AddText(layout, "Version " & BuildInfo.Version & Environment.NewLine & "Build: " & If(revision Is Nothing, "local", revision.Substring(0, Math.Min(12, revision.Length))) & " — " & BuildInfo.BuildDate)
        build.Name = "BuildVersion"
        AddText(layout, "Developed by Bohloney", 11, True)
        AddText(layout, BuildInfo.Description)
        Dim links As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        Link(links, "Instructions", Sub() ShowInstructions())
        Link(links, "Open README", Sub() OpenReadme())
        Link(links, "GitHub / report an issue", Sub() OpenUrl(UpdateService.Repository & "/issues"))
        Link(links, "Releases", Sub() OpenUrl(UpdateService.Releases))
        layout.Controls.Add(links)
        layout.Controls.Add(preview)
        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        actions.Controls.AddRange({checkButton, installButton}) : layout.Controls.Add(actions)
        layout.Controls.Add(status) : layout.Controls.Add(progress)
        AddText(layout, "Close the game before updating. Your settings are kept.")
        Dim closeButton As New Button With {.Text = "Close", .AutoSize = True, .DialogResult = DialogResult.Cancel, .Name = "CloseAbout"}
        Dim footer As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(12)}
        footer.Controls.Add(closeButton) : CancelButton = closeButton
        Controls.Add(helpTabs)
        Controls.Add(footer)
        AddHandler aboutPage.SizeChanged, Sub()
                                              For Each label In layout.Controls.OfType(Of Label)()
                                                  label.MaximumSize = New Size(Math.Max(1, aboutPage.ClientSize.Width - layout.Padding.Horizontal - Px(Me, 16)), 0)
                                              Next
                                          End Sub
        AddHandler Shown, Sub()
                              Dim work = Screen.FromControl(Me).WorkingArea
                              MinimumSize = New Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height))
                              Size = New Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height))
                          End Sub
        AddHandler checkButton.Click, Async Sub() Await CheckUpdate()
        AddHandler installButton.Click, Async Sub() Await InstallUpdate()
        AddHandler preview.CheckedChanged, Sub()
                                              availableUpdate = Nothing : installButton.Enabled = False
                                              status.Text = "Update channel changed. Check again to refresh available releases."
                                          End Sub
        AddHandler FormClosing, Sub() cancellation.Cancel()
        AddHandler FormClosed, Sub() client.Dispose()
        If knownUpdate IsNot Nothing Then
            availableUpdate = knownUpdate : DescribeUpdate() : SetWorking(False)
        End If
    End Sub
    Private Shared Function AddText(layout As TableLayoutPanel, text As String, Optional size As Single = 10, Optional bold As Boolean = False) As Label
        Dim label As New Label With {.Text = text, .AutoSize = True, .MaximumSize = New Size(570, 0), .Margin = New Padding(0, 0, 0, 12), .Font = New Font("Segoe UI", size, If(bold, FontStyle.Bold, FontStyle.Regular))}
        layout.Controls.Add(label) : Return label
    End Function
    Private Shared Sub Link(panel As FlowLayoutPanel, text As String, action As Action)
        Dim button As New Button With {.Text = text, .AutoSize = True}
        AddHandler button.Click, Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         MessageBox.Show(ex.Message, "DiRT2VR", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                     End Try
                                 End Sub
        panel.Controls.Add(button)
    End Sub
    Private Shared Sub OpenUrl(url As String)
        Process.Start(New ProcessStartInfo(url) With {.UseShellExecute = True})
    End Sub
    Public Sub ShowInstructions(Optional topic As String = "Getting started")
        helpTabs.SelectedIndex = 1
        instructions.ShowTopic(topic)
    End Sub
    Private Sub OpenReadme()
        Dim readme = IO.Path.Combine(context.ModRoot, "README.md")
        If File.Exists(readme) Then
            Dim start As New ProcessStartInfo("notepad.exe") With {.UseShellExecute = False}
            start.ArgumentList.Add(readme) : Process.Start(start)
        Else
            OpenUrl(UpdateService.Repository & "#readme")
        End If
    End Sub
    Private Sub SetWorking(value As Boolean)
        working = value : checkButton.Enabled = Not value : preview.Enabled = Not value
        installButton.Enabled = Not value AndAlso availableUpdate?.Download IsNot Nothing
    End Sub
    Private Async Function CheckUpdate() As Task
        If working Then Return
        SetWorking(True) : availableUpdate = Nothing : status.Text = "Checking GitHub Releases…"
        Try
            availableUpdate = Await New UpdateService(client).CheckAsync(BuildInfo.Version, preview.Checked, cancellation.Token)
            If IsDisposed Then Return
            DescribeUpdate()
        Catch ex As OperationCanceledException
            If Not IsDisposed Then status.Text = "Update check canceled or timed out. Try again when connected."
        Catch ex As Exception
            If Not IsDisposed Then status.Text = "Could not check for updates: " & ex.Message
        Finally
            If Not IsDisposed Then SetWorking(False)
        End Try
    End Function
    Private Sub DescribeUpdate()
        status.Text = If(availableUpdate Is Nothing, "No newer published release is available on this channel.", If(availableUpdate.Download Is Nothing, "Version " & availableUpdate.Version.Text & " is available, but has no verified installer. Open Releases for details.", "Version " & availableUpdate.Version.Text & " is available (" & Math.Ceiling(availableUpdate.Size / 1048576.0).ToString() & " MB)."))
    End Sub
    Private Async Function InstallUpdate() As Task
        If working OrElse availableUpdate?.Download Is Nothing Then Return
        SetWorking(True) : progress.Value = 0 : progress.Visible = True
        Dim downloading = True
        Try
            context.RequireClosed()
            status.Text = "Downloading update… Close this window to cancel."
            Dim reporter As New Progress(Of Integer)(Sub(value)
                                                        If Not IsDisposed AndAlso downloading Then
                                                            progress.Value = value
                                                            If value = 100 Then status.Text = "Verifying download… Close this window to cancel."
                                                        End If
                                                    End Sub)
            Dim installer = Await New UpdateService(client).DownloadAsync(availableUpdate, IO.Path.Combine(context.UserRoot, "updates"), reporter, cancellation.Token)
            downloading = False
            cancellation.Token.ThrowIfCancellationRequested()
            status.Text = "Restoring original files before setup… Close this window to cancel setup."
            ' Restore under the playing user's account before the installer can elevate.
            Await UpdateService.PrepareInstallerAsync(context, Sub() Worker.Invoke(context, "recover", quiet:=True), cancellation.Token)
            cancellation.Token.ThrowIfCancellationRequested()
            status.Text = "Opening setup… Windows may request administrator approval."
            Dim launcher = Owner
            Await Task.Run(Sub()
                               cancellation.Token.ThrowIfCancellationRequested()
                               Process.Start(InstallerStartInfo(installer, context.GameRoot))
                           End Sub, cancellation.Token)
            If Not IsDisposed Then Close()
            launcher?.Close()
        Catch ex As OperationCanceledException
            If Not IsDisposed Then status.Text = "Update canceled or timed out. Setup was not started."
        Catch ex As Exception
            If Not IsDisposed Then status.Text = "Update could not start: " & ex.Message
        Finally
            If Not IsDisposed Then
                progress.Visible = False : SetWorking(False)
            End If
        End Try
    End Function
    Public Shared Function InstallerStartInfo(installer As String, game As String) As ProcessStartInfo
        Dim start As New ProcessStartInfo(installer) With {.UseShellExecute = True}
        start.ArgumentList.Add("/DIR=" & game) : start.ArgumentList.Add("/NORESTART")
        Return start
    End Function
End Class
