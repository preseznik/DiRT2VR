Imports System.Drawing
Imports System.Text.Json
Imports System.Windows.Forms

' A launch is correlated before the child writes its first status. Running alone
' means Process.Start succeeded; it does not mean the renderer window exists.
Public Class LaunchProgressState
    Private ReadOnly processId As Integer
    Private ReadOnly requestedUtc As DateTime
    Public Property Message As String = "Starting the launcher…"
    Public Property Finished As Boolean
    Public Property WindowReady As Boolean
    Public Property EndedWithoutStatus As Boolean
    Public Sub New(processId As Integer, requestedUtc As DateTime)
        Me.processId = processId : Me.requestedUtc = requestedUtc
    End Sub
    Public Sub Observe(status As SessionStatus, exited As Boolean)
        If Finished Then Return
        If status IsNot Nothing AndAlso status.ProcessId = processId AndAlso status.UpdatedUtc >= requestedUtc Then
            If Not String.IsNullOrWhiteSpace(status.StartupMessage) Then Message = status.StartupMessage
            If status.State = "Ready" OrElse status.State = "Failed" Then
                Finished = True : Return
            End If
            If status.GameWindowReady Then
                WindowReady = True : Finished = True : Return
            End If
        End If
        If exited Then
            EndedWithoutStatus = True : Finished = True
        End If
    End Sub
End Class

Public Class LaunchProgressForm
    Inherits LauncherForm
    Private ReadOnly stage As New Label With {.Name = "StartupMessage", .AutoSize = True, .Dock = DockStyle.Fill, .MaximumSize = New Size(380, 0)}
    Private ReadOnly elapsed As New Label With {.Name = "StartupElapsed", .AutoSize = True, .Anchor = AnchorStyles.Left}
    Public Sub New(vr As Boolean)
        SuspendLayout()
        Text = "Launching DiRT 2"
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(420, 170)
        FormBorderStyle = FormBorderStyle.FixedDialog
        StartPosition = FormStartPosition.Manual
        ShowInTaskbar = False : MinimizeBox = False : MaximizeBox = False
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(16), .ColumnCount = 1, .RowCount = 4}
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 12))
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label With {.Text = If(vr, "Starting DiRT 2 in VR", "Starting DiRT 2"), .AutoSize = True, .Font = New Font(Font, FontStyle.Bold), .Margin = New Padding(3, 0, 3, 10)}, 0, 0)
        layout.Controls.Add(stage, 0, 1)
        layout.Controls.Add(New ProgressBar With {.Name = "StartupActivity", .Dock = DockStyle.Fill, .Style = ProgressBarStyle.Marquee, .MarqueeAnimationSpeed = 30, .AccessibleName = "Startup in progress"}, 0, 2)
        Dim footer As New TableLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .ColumnCount = 2, .Margin = New Padding(0, 8, 0, 0)}
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        footer.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim hide As New Button With {.Name = "HideLaunchProgress", .Text = "Hide", .AutoSize = True}
        AddHandler hide.Click, Sub() Close()
        CancelButton = hide
        footer.Controls.Add(elapsed, 0, 0) : footer.Controls.Add(hide, 1, 0)
        layout.Controls.Add(footer, 0, 3) : Controls.Add(layout)
        RefreshProgress("Starting the launcher…", TimeSpan.Zero)
        AutoScaleDimensions = New SizeF(96, 96)
        ResumeLayout(True)
    End Sub
    Protected Overrides Sub OnLoad(e As EventArgs)
        MyBase.OnLoad(e)
        CenterToParent() ' CenterParent start positioning only applies to modal dialogs.
    End Sub
    Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
        Get
            Return True
        End Get
    End Property
    Public Sub RefreshProgress(message As String, duration As TimeSpan)
        stage.Text = message
        elapsed.Text = $"Elapsed: {Math.Floor(duration.TotalMinutes):00}:{duration.Seconds:00}"
    End Sub
End Class

' Owned by the graphical launcher, never by the blocking session/file worker.
Public Class LaunchProgressMonitor
    Implements IDisposable
    Private ReadOnly context As InstallContext
    Private ReadOnly popup As LaunchProgressForm
    Private ReadOnly timer As New System.Windows.Forms.Timer With {.Interval = 100}
    Private ReadOnly elapsed As Stopwatch = Stopwatch.StartNew()
    Private child As Process
    Private disposed As Boolean
    Public ReadOnly Property State As LaunchProgressState
    Public Event Completed()
    Public Sub New(owner As Form, context As InstallContext, vr As Boolean, start As ProcessStartInfo)
        Me.context = context
        popup = New LaunchProgressForm(vr)
        Try
            popup.Show(owner) : popup.Update()
            Dim requestedUtc = DateTime.UtcNow
            child = Process.Start(start)
            _State = New LaunchProgressState(child.Id, requestedUtc)
            StartupFocus.AllowSetForegroundWindow(CUInt(child.Id))
            AddHandler timer.Tick, AddressOf Poll
            timer.Start()
        Catch
            Dispose() : Throw
        End Try
    End Sub
    Private Sub Poll(sender As Object, e As EventArgs)
        If disposed Then Return
        ' Read the final status after observing exit, not just before it is written.
        Dim exited = child.HasExited
        Dim status As SessionStatus = Nothing
        Try
            Dim path = IO.Path.Combine(context.UserRoot, "session.json")
            status = SessionStatus.Read(path)
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException
            ' An atomic replacement or damaged old status cannot stop exit monitoring.
        End Try
        State.Observe(status, exited)
        If State.Finished Then
            Dispose()
            RaiseEvent Completed()
        ElseIf Not popup.IsDisposed Then
            popup.RefreshProgress(State.Message, elapsed.Elapsed)
        End If
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        If disposed Then Return
        disposed = True
        timer.Stop() : timer.Dispose()
        If Not popup.IsDisposed Then popup.Close()
        popup.Dispose()
        child?.Dispose() ' Release our handle, never stop the session or game.
        elapsed.Stop()
    End Sub
End Class
