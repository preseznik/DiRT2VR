Imports DiRT2VR.CustomCars
Imports DiRT2VR.CustomTracks
Imports System.Drawing
Imports System.Windows.Forms

Public Class PackExportDialog
    Inherits LauncherForm

    Public Shared Sub Export(owner As IWin32Window, context As InstallContext, kind As String, id As String, packName As String)
        Using picker As New SaveFileDialog With {.Title = "Export " & packName & " pack", .Filter = "ZIP archive (*.zip)|*.zip", .DefaultExt = "zip", .AddExtension = True, .OverwritePrompt = True,
            .FileName = "DiRT2VR-" & id.Replace("/", "-") & ".zip"}
            If picker.ShowDialog(owner) <> DialogResult.OK Then Return
            Using dialog As New PackExportDialog(context, kind, id, packName, picker.FileName)
                dialog.ShowDialog(owner)
            End Using
        End Using
    End Sub

    Public Sub New(context As InstallContext, kind As String, id As String, packName As String, destination As String)
        Text = "Export pack — " & packName
        StartPosition = FormStartPosition.CenterParent : AutoScaleMode = AutoScaleMode.Dpi
        ClientSize = New Size(570, 205) : MinimumSize = New Size(440, 245)
        MaximizeBox = False : MinimizeBox = False : ShowInTaskbar = False
        Font = New Font("Segoe UI", 10)
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(16), .ColumnCount = 1, .RowCount = 3}
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 30))
        layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
        Dim status As New Label With {.Text = "Checking installed pack…", .Dock = DockStyle.Fill, .AutoEllipsis = True, .UseMnemonic = False}
        Dim bar As New ProgressBar With {.Dock = DockStyle.Fill, .Maximum = 100, .Margin = New Padding(0, 4, 0, 8)}
        Dim stopButton As New Button With {.Text = "Cancel", .AutoSize = True, .Anchor = AnchorStyles.Right}
        CancelButton = stopButton
        layout.Controls.Add(status) : layout.Controls.Add(bar) : layout.Controls.Add(stopButton) : Controls.Add(layout)
        LauncherAppearance.Apply(Me)
        Dim cancellation As New CancellationTokenSource(), running = True
        Dim stopExport As Action = Sub()
                                      cancellation.Cancel() : stopButton.Enabled = False : status.Text = "Cancelling export…"
                                  End Sub
        AddHandler stopButton.Click, Sub() stopExport()
        AddHandler FormClosing, Sub(sender, e)
                                    If running Then
                                        e.Cancel = True : stopExport()
                                    End If
                                End Sub
        AddHandler Shown, Async Sub()
                              Dim result As String, icon As MessageBoxIcon
                              Try
                                  Dim progress As New Progress(Of TrackProgress)(Sub(p)
                                                                                   If Not IsDisposed AndAlso Not cancellation.IsCancellationRequested Then
                                                                                       status.Text = p.Message : bar.Value = p.Percent
                                                                                   End If
                                                                               End Sub)
                                  Await Task.Run(Sub() CustomTrackService.WithSessionLock(Sub()
                                      context.RequireClosed()
                                      If CustomTrackService.RecoveryPending(context) OrElse New AssetTransaction(context).Pending OrElse New GraphicsTransaction(context).Pending Then Throw New IOException("Choose Restore original files before exporting a pack.")
                                      PackExport.Write(context.GameRoot, kind, id, destination, BuildInfo.Version,
                                                       Sub(p) DirectCast(progress, IProgress(Of TrackProgress)).Report(p), cancellation.Token)
                                  End Sub))
                                  result = "Pack saved:" & vbCrLf & destination & vbCrLf & vbCrLf &
                                      "On the other PC, close DiRT 2 and its launcher. Extract the ZIP into the DiRT 2 game folder containing dirt2.exe, then open an up-to-date DiRT2VR launcher and choose Refresh. " &
                                      If(kind = "track", "All installed layouts of this pack are included. ", "") & "No source-game rebuild is needed. Existing compatibility checks and play-mode limits still apply."
                                  icon = MessageBoxIcon.Information
                              Catch ex As OperationCanceledException
                                  result = "Export cancelled. Existing archives and installed content were kept." : icon = MessageBoxIcon.Information
                              Catch ex As Exception
                                  result = ex.Message : icon = MessageBoxIcon.Error
                              Finally
                                  running = False
                              End Try
                              MessageBox.Show(Me, result, "Export pack", MessageBoxButtons.OK, icon)
                              Close()
                          End Sub
        AddHandler FormClosed, Sub() cancellation.Dispose()
    End Sub
End Class
