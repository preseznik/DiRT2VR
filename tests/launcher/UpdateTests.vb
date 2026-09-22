Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports DiRT2VR

Module UpdateTests
    Public Sub Startup(context As InstallContext, folder As String, check As Action(Of Boolean, String))
        Dim completion As New TaskCompletionSource(Of ReleaseUpdate)(), calls As Integer
        Dim releaseUpdate = UpdateService.SelectUpdate(JsonSerializer.Serialize({Release("v9.0.0")}), BuildInfo.Version, True)
        Using form As New MainForm(context, Function(token)
                                               calls += 1
                                               Return completion.Task
                                           End Function)
            form.ShowInTaskbar = False : form.StartPosition = FormStartPosition.Manual : form.Location = New Drawing.Point(-32000, -32000)
            form.Show() : Application.DoEvents()
            Dim notice = DirectCast(form.Controls.Find("UpdateAvailable", True).Single(), Button)
            check(calls = 1 AndAlso Not notice.Visible AndAlso form.Controls.Find("LaunchDesktop", True).Single().Enabled, "startup check runs once without blocking launch")
            completion.SetResult(releaseUpdate)
            PumpUntil(Function() notice.Visible)
            check(notice.Visible AndAlso notice.AccessibleDescription.Contains("9.0.0"), "background result displays the new version indicator")
            Using bitmap As New Drawing.Bitmap(form.Width, form.Height)
                form.DrawToBitmap(bitmap, New Drawing.Rectangle(0, 0, form.Width, form.Height))
                bitmap.Save(Path.Combine(folder, "launcher-update-available.png"))
            End Using
            Using closeDialog As New System.Windows.Forms.Timer With {.Interval = 20}
                Dim verified = False
                AddHandler closeDialog.Tick, Sub()
                                                 Dim about = Application.OpenForms.OfType(Of AboutForm)().SingleOrDefault()
                                                 If about Is Nothing Then Return
                                                 verified = about.Controls.Find("UpdateStatus", True).Single().Text.Contains("9.0.0") AndAlso about.Controls.Find("InstallUpdate", True).Single().Enabled
                                                 closeDialog.Stop() : about.Close()
                                             End Sub
                closeDialog.Start() : notice.PerformClick()
                check(verified, "update indicator opens About with cached update ready to install")
            End Using
            form.Hide() : form.Show() : Application.DoEvents()
            check(calls = 1, "reshowing window does not repeat startup request")
            form.Close()
        End Using
        For Each result In {Task.FromResult(Of ReleaseUpdate)(Nothing), Task.FromException(Of ReleaseUpdate)(New HttpRequestException("offline"))}
            Using form As New MainForm(context, Function(token) result)
                form.ShowInTaskbar = False : form.StartPosition = FormStartPosition.Manual : form.Location = New Drawing.Point(-32000, -32000)
                form.Show() : Application.DoEvents()
                check(Not form.Controls.Find("UpdateAvailable", True).Single().Visible AndAlso form.Controls.Find("LaunchDesktop", True).Single().Enabled, "no release/offline startup remains quiet and usable")
                form.Close()
            End Using
        Next
        Dim late As New TaskCompletionSource(Of ReleaseUpdate)(), observed As CancellationToken
        Using form As New MainForm(context, Function(token)
                                               observed = token
                                               Return late.Task
                                           End Function)
            form.ShowInTaskbar = False : form.StartPosition = FormStartPosition.Manual : form.Location = New Drawing.Point(-32000, -32000)
            form.Show() : Application.DoEvents() : form.Close()
            late.SetResult(releaseUpdate) : Application.DoEvents()
            check(observed.IsCancellationRequested AndAlso form.IsDisposed, "closing cancels startup check and ignores late results")
        End Using
    End Sub
    Private Sub PumpUntil(done As Func(Of Boolean))
        Dim deadline = DateTime.UtcNow.AddSeconds(3)
        While Not done() AndAlso DateTime.UtcNow < deadline
            Application.DoEvents() : Thread.Sleep(5)
        End While
    End Sub
    Private Function Release(tag As String, Optional draft As Boolean = False, Optional preview As Boolean = False, Optional url As String = Nothing, Optional digest As String = Nothing) As Object
        Dim name = "DiRT2VR-" & tag.TrimStart("v"c) & "-Setup.exe"
        Return New With {.tag_name = tag, .draft = draft, .prerelease = preview, .assets = {New With {
            .name = name, .state = "uploaded", .size = 5,
            .browser_download_url = If(url, UpdateService.Repository & "/releases/download/" & tag & "/" & name),
            .digest = If(digest, "sha256:" & New String("a"c, 64))}}}
    End Function

    Public Sub Run(folder As String, check As Action(Of Boolean, String), live As Boolean)
        Dim ordered = {"0.1.0-alpha", "0.1.0-alpha.2", "0.1.0-alpha.10", "0.1.0-beta", "0.1.0", "0.2.0", "1.0.0"}
        For i = 1 To ordered.Length - 1
            check(ReleaseVersion.Parse(ordered(i)).CompareTo(ReleaseVersion.Parse(ordered(i - 1))) > 0, "SemVer ordering " & ordered(i))
        Next
        check(ReleaseVersion.Parse("v1.0.0+abc").CompareTo(ReleaseVersion.Parse("1.0.0+def")) = 0, "build metadata is not a newer release")
        For Each bad In {"01.0.0", "1.0", "1.0.0-alpha.01", "../setup", "1.0.0/evil", "99999999999999999999999.0.0"}
            Dim rejected = False
            Try
                ReleaseVersion.Parse(bad)
            Catch ex As IOException
                rejected = True
            End Try
            check(rejected, "invalid release tag rejected: " & bad)
        Next
        Dim json = JsonSerializer.Serialize({Release("v0.1.0"), Release("v1.0.0", draft:=True), Release("v0.2.0-alpha.10", preview:=True), Release("v0.2.0-alpha.2", preview:=True)})
        check(UpdateService.SelectUpdate(json, "0.1.0-alpha.2", False).Version.Text = "0.1.0", "stable channel excludes drafts and experimental builds")
        check(UpdateService.SelectUpdate(json, "0.1.0", True).Version.Text = "0.2.0-alpha.10", "newest version chosen regardless of API ordering")
        check(UpdateService.SelectUpdate(json, "1.0.0", True) Is Nothing, "no downgrade offered")
        check(UpdateService.SelectUpdate("[]", BuildInfo.Version, True) Is Nothing, "empty repository has no update")
        For Each item In {Release("v0.3.0", url:="https://example.com/setup.exe"), Release("v0.3.0", digest:="sha256:bad"), Release("v0.3.0", digest:="")}
            check(UpdateService.SelectUpdate(JsonSerializer.Serialize({item}), "0.1.0", True).Download Is Nothing, "unverifiable installer cannot be downloaded")
        Next
        Dim sourceOnly = "[{""draft"":false,""prerelease"":false,""tag_name"":""v0.3.0"",""assets"":[]}]"
        check(UpdateService.SelectUpdate(sourceOnly, "0.1.0", True).Download Is Nothing, "source-only release cannot install")
        check(UpdateService.SelectUpdate("[{""tag_name"":""unrelated""}]", "0.1.0", True) Is Nothing, "unrelated release ignored")
        Dim start = AboutForm.InstallerStartInfo("C:\download folder\setup.exe", "F:\Games Ž\DiRT 2")
        check(start.UseShellExecute AndAlso start.ArgumentList.SequenceEqual({"/DIR=F:\Games Ž\DiRT 2", "/NORESTART"}), "installer handoff preserves Unicode/spaces and is interactive")
        BackgroundPhases(folder, check)
        Task.Run(Async Function()
                     Await NetworkTests(folder, check)
                     If live Then
                         Using client = UpdateService.CreateClient()
                             Dim result = Await New UpdateService(client).CheckAsync(BuildInfo.Version, True, CancellationToken.None)
                             Console.WriteLine("LIVE GitHub release check: " & If(result?.Version.Text, "no newer release"))
                             check(True, "real anonymous GitHub API check completed")
                         End Using
                     End If
                 End Function).GetAwaiter().GetResult()
    End Sub

    Private Sub BackgroundPhases(folder As String, check As Action(Of Boolean, String))
        Dim context As New InstallContext(Path.Combine(folder, "update recovery game"), Path.Combine(folder, "update recovery user"))
        Dim uiThread = Environment.CurrentManagedThreadId, workerThread As Integer, ticks As Integer
        Using window As New Form(), timer As New System.Windows.Forms.Timer With {.Interval = 10}, entered As New ManualResetEventSlim(), release As New ManualResetEventSlim(), cancel As New CancellationTokenSource()
            window.ShowInTaskbar = False : window.StartPosition = FormStartPosition.Manual : window.Location = New Drawing.Point(-32000, -32000)
            window.Show()
            AddHandler timer.Tick, Sub() ticks += 1
            timer.Start()
            Dim preparation = UpdateService.PrepareInstallerAsync(context, Sub()
                                                                              workerThread = Environment.CurrentManagedThreadId
                                                                              entered.Set()
                                                                              If Not release.Wait(5000) Then Throw New TimeoutException("Recovery test gate timed out")
                                                                          End Sub, cancel.Token)
            Try
                PumpUntil(Function() preparation.IsCompleted OrElse (entered.IsSet AndAlso ticks > 1))
                If preparation.IsCompleted Then preparation.GetAwaiter().GetResult()
                check(entered.IsSet AndAlso ticks > 1 AndAlso Not preparation.IsCompleted AndAlso workerThread <> uiThread, "update recovery keeps the Windows message loop responsive while worker is blocked")
                Using guard As New Mutex(False, "Global\DiRT2VR.Session")
                    Dim acquired = guard.WaitOne(0)
                    If acquired Then guard.ReleaseMutex()
                    check(Not acquired, "update recovery holds the session guard on its worker thread")
                End Using
                cancel.Cancel()
            Finally
                release.Set()
            End Try
            Dim canceled As Boolean
            Try
                preparation.GetAwaiter().GetResult()
            Catch ex As OperationCanceledException
                canceled = True
            End Try
            check(canceled, "cancel during recovery prevents proceeding to installer")
            window.Close()
        End Using
        Dim called As Boolean, rejected As Boolean
        Using guard As New Mutex(False, "Global\DiRT2VR.Session")
            check(guard.WaitOne(0), "canceled update releases its session guard")
            Try
                UpdateService.PrepareInstallerAsync(context, Sub() called = True, CancellationToken.None).GetAwaiter().GetResult()
            Catch ex As IOException
                rejected = True
            Finally
                guard.ReleaseMutex()
            End Try
        End Using
        check(rejected AndAlso Not called, "update refuses recovery while another session holds the guard")
        rejected = False
        Try
            UpdateService.PrepareInstallerAsync(context, Sub() Throw New IOException("recovery conflict"), CancellationToken.None).GetAwaiter().GetResult()
        Catch ex As IOException
            rejected = ex.Message = "recovery conflict"
        End Try
        check(rejected, "recovery errors propagate to the updater instead of starting setup")
        UpdateService.PrepareInstallerAsync(context, Sub() called = True, CancellationToken.None).GetAwaiter().GetResult()
        check(called, "a recovery failure releases the guard and allows a retry")
        Dim downloadThread As Integer, payload = Encoding.UTF8.GetBytes("verified update test")
        Dim update As New ReleaseUpdate With {.Download = "https://github.com/preseznik/DiRT2VR/test", .AssetName = "test-setup.exe", .Size = payload.Length, .Digest = Convert.ToHexString(SHA256.HashData(payload))}
        Using client As New HttpClient(New ReplyHandler(Function()
                                                           downloadThread = Environment.CurrentManagedThreadId
                                                           Return New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New ByteArrayContent(payload)}
                                                       End Function))
            Dim download = New UpdateService(client).DownloadAsync(update, Path.Combine(folder, "background update"), Nothing, CancellationToken.None)
            check(download.Wait(5000) AndAlso downloadThread <> uiThread, "download and verification run off the UI even when HTTP operations complete synchronously")
            check(File.ReadAllBytes(download.Result).SequenceEqual(payload), "background download still verifies and preserves installer bytes")
        End Using
    End Sub

    Private Async Function NetworkTests(folder As String, check As Action(Of Boolean, String)) As Task
        Dim payload = Encoding.UTF8.GetBytes("test installer bytes")
        Dim update As New ReleaseUpdate With {.Download = UpdateService.Repository & "/releases/download/v1.0.0/DiRT2VR-1.0.0-Setup.exe", .AssetName = "DiRT2VR-1.0.0-Setup.exe", .Size = payload.Length, .Digest = Convert.ToHexString(SHA256.HashData(payload))}
        Dim cache = Path.Combine(folder, "update downloads Ž")
        Using client As New HttpClient(New ReplyHandler(Function() New HttpResponseMessage(HttpStatusCode.OK) With {.Content = New ByteArrayContent(payload)}))
            Dim service As New UpdateService(client)
            Dim target = Await service.DownloadAsync(update, cache, Nothing, CancellationToken.None)
            check(File.ReadAllBytes(target).SequenceEqual(payload), "verified installer download saved intact")
            File.Delete(target)
            For Each failure In {"checksum", "truncated", "oversize", "canceled"}
                update.Size = payload.Length : update.Digest = Convert.ToHexString(SHA256.HashData(payload))
                If failure = "checksum" Then update.Digest = New String("0"c, 64)
                If failure = "truncated" Then update.Size += 1
                If failure = "oversize" Then update.Size -= 1
                Dim rejected = False
                Using cancel As New CancellationTokenSource()
                    If failure = "canceled" Then cancel.Cancel()
                    Try
                        Await service.DownloadAsync(update, cache, Nothing, cancel.Token)
                    Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is OperationCanceledException
                        rejected = True
                    End Try
                End Using
                check(rejected AndAlso Not Directory.EnumerateFiles(cache).Any(), failure & " download discarded without executable or partial file")
            Next
        End Using
        For Each status In {HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests}
            Using client As New HttpClient(New ReplyHandler(Function() New HttpResponseMessage(status)))
                Dim rejected = False
                Try
                    Await New UpdateService(client).CheckAsync("0.1.0", True, CancellationToken.None)
                Catch ex As IOException
                    rejected = ex.Message.Contains("Releases")
                End Try
                check(rejected, "GitHub " & CInt(status) & " gives actionable failure")
            End Using
        Next
    End Function

    Private Class ReplyHandler
        Inherits HttpMessageHandler
        Private ReadOnly response As Func(Of HttpResponseMessage)
        Public Sub New(reply As Func(Of HttpResponseMessage))
            response = reply
        End Sub
        Protected Overrides Function SendAsync(request As HttpRequestMessage, token As CancellationToken) As Task(Of HttpResponseMessage)
            token.ThrowIfCancellationRequested()
            Return Task.FromResult(response())
        End Function
    End Class
End Module
