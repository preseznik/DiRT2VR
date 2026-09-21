Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports DiRT2VR

Module UpdateTests
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
