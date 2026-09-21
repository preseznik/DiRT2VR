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
    Public Sub New(value As InstallContext, Optional knownUpdate As ReleaseUpdate = Nothing)
        context = value
        Text = "DiRT2VR — Help / About"
        Using stream = GetType(MainForm).Assembly.GetManifestResourceStream("DiRT2VR.ico"), appIcon As New Icon(stream)
            Icon = DirectCast(appIcon.Clone(), Icon)
        End Using
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(640, Math.Min(740, Screen.PrimaryScreen.WorkingArea.Height - 100)) : MinimumSize = New Size(600, 540)
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False : MinimizeBox = False : MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(20), .ColumnCount = 1, .AutoScroll = True}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        AddText(layout, "DiRT2VR", 20, True)
        Dim revision = BuildInfo.FullVersion.Split("+"c).Skip(1).FirstOrDefault()
        Dim build = AddText(layout, "Version " & BuildInfo.Version & Environment.NewLine & "Build: " & If(revision Is Nothing, "local", revision.Substring(0, Math.Min(12, revision.Length))) & " — " & BuildInfo.BuildDate)
        build.Name = "BuildVersion"
        AddText(layout, "Developed by Bohloney", 11, True)
        AddText(layout, BuildInfo.Description)
        Dim links As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        Link(links, "Instructions", Sub() OpenInstructions())
        Link(links, "GitHub / report an issue", Sub() OpenUrl(UpdateService.Repository & "/issues"))
        Link(links, "Releases", Sub() OpenUrl(UpdateService.Releases))
        layout.Controls.Add(links)
        AddText(layout, "Launch plays on your monitor; Launch VR uses SteamVR. Toggle VR switches the view; Recenter sets your seated position. Controls can be changed on the Controls tab.")
        layout.Controls.Add(preview)
        Dim actions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        actions.Controls.AddRange({checkButton, installButton}) : layout.Controls.Add(actions)
        layout.Controls.Add(status) : layout.Controls.Add(progress)
        AddText(layout, "Close the game before updating. Downloads are verified before setup opens. Settings are kept. ZIP installs become installer-managed. Windows may request administrator approval.")
        Dim closeButton As New Button With {.Text = "Close", .AutoSize = True, .DialogResult = DialogResult.Cancel, .Name = "CloseAbout"}
        Dim footer As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .AutoSize = True, .FlowDirection = FlowDirection.RightToLeft, .Padding = New Padding(12)}
        footer.Controls.Add(closeButton) : CancelButton = closeButton
        Controls.Add(layout)
        Controls.Add(footer)
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
    Private Sub OpenInstructions()
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
        Try
            context.RequireClosed()
            status.Text = "Downloading update… Close this window to cancel."
            Dim reporter As New Progress(Of Integer)(Sub(value)
                                                        If Not IsDisposed Then progress.Value = value
                                                    End Sub)
            Dim installer = Await New UpdateService(client).DownloadAsync(availableUpdate, IO.Path.Combine(context.UserRoot, "updates"), reporter, cancellation.Token)
            cancellation.Token.ThrowIfCancellationRequested()
            ' Restore under the playing user's account before the installer can elevate.
            Using guard As New Mutex(False, "Global\DiRT2VR.Session")
                Dim held As Boolean
                Try
                    held = guard.WaitOne(0)
                Catch ex As AbandonedMutexException
                    held = True
                End Try
                If Not held Then Throw New IOException("Close the running DiRT2VR session before updating.")
                Try
                    context.RequireClosed()
                    Call (New GraphicsTransaction(context)).Recover()
                    Worker.Invoke(context, "recover")
                Finally
                    guard.ReleaseMutex()
                End Try
            End Using
            Process.Start(InstallerStartInfo(installer, context.GameRoot))
            Dim launcher = Owner
            Close() : launcher?.Close()
        Catch ex As OperationCanceledException
            If Not IsDisposed Then status.Text = "Update canceled. The current installation is unchanged."
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
