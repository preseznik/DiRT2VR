Imports System.Net.Http
Imports System.Reflection
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

Public Module BuildInfo
    Public ReadOnly FullVersion As String = GetType(BuildInfo).Assembly.GetCustomAttribute(Of AssemblyInformationalVersionAttribute)().InformationalVersion
    Public ReadOnly Version As String = FullVersion.Split("+"c)(0)
    Public ReadOnly BuildDate As String = If(GetType(BuildInfo).Assembly.GetCustomAttributes(Of AssemblyMetadataAttribute)().FirstOrDefault(Function(a) a.Key = "BuildUtc")?.Value, "Development build")
    Public ReadOnly Channel As String = If(GetType(BuildInfo).Assembly.GetCustomAttributes(Of AssemblyMetadataAttribute)().FirstOrDefault(Function(a) a.Key = "ReleaseChannel")?.Value, If(Version.Contains("-"), "Experimental", "Stable"))
    Public ReadOnly Description As String = "Native cockpit VR and a desktop launcher for DiRT 2, with direct practice, AI races and configurable controls."
End Module

' SemVer precedence, including alpha.10 > alpha.2; build metadata is not an update.
Public Class ReleaseVersion
    Implements IComparable(Of ReleaseVersion)
    Private ReadOnly core As Long()
    Private ReadOnly pre As String()
    Public ReadOnly Text As String
    Private Sub New(value As String, numbers As Long(), identifiers As String())
        Text = value : core = numbers : pre = identifiers
    End Sub
    Public Shared Function Parse(value As String) As ReleaseVersion
        If value Is Nothing OrElse value.Length > 150 Then Throw New IOException("Invalid release version.")
        Dim match = Regex.Match(value, "^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$")
        If Not match.Success Then Throw New IOException("Invalid release version.")
        Dim numbers(2) As Long
        For i = 0 To 2
            If Not Long.TryParse(match.Groups(i + 1).Value, numbers(i)) Then Throw New IOException("Invalid release version.")
        Next
        Dim ids = If(match.Groups(4).Success, match.Groups(4).Value.Split("."c), Array.Empty(Of String)())
        If ids.Any(Function(id) id.Length > 1 AndAlso id(0) = "0"c AndAlso id.All(Function(c) c >= "0"c AndAlso c <= "9"c)) Then Throw New IOException("Invalid prerelease version.")
        Return New ReleaseVersion(value.TrimStart("v"c).Split("+"c)(0), numbers, ids)
    End Function
    Public Function CompareTo(other As ReleaseVersion) As Integer Implements IComparable(Of ReleaseVersion).CompareTo
        For i = 0 To 2
            Dim order = core(i).CompareTo(other.core(i))
            If order <> 0 Then Return order
        Next
        If pre.Length = 0 OrElse other.pre.Length = 0 Then Return If(pre.Length = other.pre.Length, 0, If(pre.Length = 0, 1, -1))
        For i = 0 To Math.Min(pre.Length, other.pre.Length) - 1
            Dim a = pre(i), b = other.pre(i)
            Dim an = a.All(Function(c) c >= "0"c AndAlso c <= "9"c), bn = b.All(Function(c) c >= "0"c AndAlso c <= "9"c)
            Dim order As Integer
            If an AndAlso bn Then
                order = a.Length.CompareTo(b.Length)
                If order = 0 Then order = String.CompareOrdinal(a, b)
            ElseIf an <> bn Then
                order = If(an, -1, 1)
            Else
                order = String.CompareOrdinal(a, b)
            End If
            If order <> 0 Then Return order
        Next
        Return pre.Length.CompareTo(other.pre.Length)
    End Function
End Class

Public Class ReleaseUpdate
    Public Property IsExperimental As Boolean
    Public Property Version As ReleaseVersion
    Public Property Page As String
    Public Property Download As String
    Public Property Digest As String
    Public Property Size As Long
    Public Property AssetName As String
End Class

Public Class UpdateService
    Public Const Repository As String = "https://github.com/preseznik/DiRT2VR"
    Public Const Releases As String = Repository & "/releases"
    Public Const Api As String = "https://api.github.com/repos/preseznik/DiRT2VR/releases?per_page=100"
    Private Const MaxDownload As Long = 512L * 1024 * 1024
    Private ReadOnly client As HttpClient
    Public Sub New(value As HttpClient)
        client = value
    End Sub
    Public Shared Function CreateClient() As HttpClient
        Dim http As New HttpClient With {.Timeout = TimeSpan.FromMinutes(10)}
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DiRT2VR/" & BuildInfo.Version)
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28")
        Return http
    End Function
    Public Shared Function SelectUpdate(json As String, current As String, includePreview As Boolean) As ReleaseUpdate
        Return SelectRelease(json, current, includePreview)
    End Function
    Public Shared Function SelectStable(json As String) As ReleaseUpdate
        Return SelectRelease(json, Nothing, False)
    End Function
    Private Shared Function SelectRelease(json As String, current As String, includePreview As Boolean) As ReleaseUpdate
        Dim installed = If(current Is Nothing, Nothing, ReleaseVersion.Parse(current))
        Dim newest As ReleaseUpdate = Nothing
        Using document = JsonDocument.Parse(json)
            For Each release In document.RootElement.EnumerateArray()
                Try
                    If release.GetProperty("draft").GetBoolean() Then Continue For
                    Dim version = ReleaseVersion.Parse(release.GetProperty("tag_name").GetString())
                    Dim experimental = release.GetProperty("prerelease").GetBoolean() OrElse version.Text.Contains("-")
                    If Not includePreview AndAlso experimental Then Continue For
                    If (installed IsNot Nothing AndAlso version.CompareTo(installed) <= 0) OrElse (newest IsNot Nothing AndAlso version.CompareTo(newest.Version) <= 0) Then Continue For
                    Dim assetName = "DiRT2VR-" & version.Text & "-Setup.exe"
                    Dim candidate As New ReleaseUpdate With {.Version = version, .IsExperimental = experimental, .Page = Releases & "/tag/" & Uri.EscapeDataString(release.GetProperty("tag_name").GetString())}
                    For Each releaseAsset In release.GetProperty("assets").EnumerateArray()
                        If releaseAsset.GetProperty("name").GetString() <> assetName OrElse releaseAsset.GetProperty("state").GetString() <> "uploaded" Then Continue For
                        Dim url = releaseAsset.GetProperty("browser_download_url").GetString()
                        Dim expected = Repository & "/releases/download/" & Uri.EscapeDataString(release.GetProperty("tag_name").GetString()) & "/" & assetName
                        Dim digest As JsonElement
                        Dim hash = If(releaseAsset.TryGetProperty("digest", digest) AndAlso digest.ValueKind = JsonValueKind.String, digest.GetString(), "")
                        Dim size = releaseAsset.GetProperty("size").GetInt64()
                        If url <> expected OrElse Not Regex.IsMatch(hash, "^sha256:[0-9a-fA-F]{64}$") OrElse size <= 0 OrElse size > MaxDownload Then Continue For
                        candidate.Download = url : candidate.Digest = hash.Substring(7)
                        candidate.Size = size : candidate.AssetName = assetName
                    Next
                    newest = candidate
                Catch ex As Exception When TypeOf ex Is KeyNotFoundException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is FormatException
                    ' Ignore malformed or unrelated release entries, never execute their URLs.
                End Try
            Next
        End Using
        Return newest
    End Function
    Public Function CheckAsync(current As String, preview As Boolean, token As CancellationToken) As Task(Of ReleaseUpdate)
        Return ReadReleasesAsync(Function(json) SelectUpdate(json, current, preview), token)
    End Function
    Public Function CheckStableAsync(token As CancellationToken) As Task(Of ReleaseUpdate)
        Return ReadReleasesAsync(AddressOf SelectStable, token)
    End Function
    Private Async Function ReadReleasesAsync(selectRelease As Func(Of String, ReleaseUpdate), token As CancellationToken) As Task(Of ReleaseUpdate)
        Dim newest As ReleaseUpdate = Nothing, page As String = Api
        Dim visited As New HashSet(Of String)(StringComparer.Ordinal)
        Using timeout = CancellationTokenSource.CreateLinkedTokenSource(token)
            timeout.CancelAfter(TimeSpan.FromSeconds(20))
            While page IsNot Nothing
                If Not visited.Add(page) OrElse visited.Count > 100 Then Throw New IOException("Invalid release pagination.")
                Using response = Await client.GetAsync(page, timeout.Token)
                    If response.StatusCode = Net.HttpStatusCode.NotFound Then Throw New IOException("Public GitHub releases are unavailable. Use the Releases link to check the repository.")
                    If CInt(response.StatusCode) = 403 OrElse CInt(response.StatusCode) = 429 Then Throw New IOException("GitHub's update-check limit was reached. Try again later or use the Releases link.")
                    response.EnsureSuccessStatusCode()
                    Dim candidate = selectRelease(Await response.Content.ReadAsStringAsync(timeout.Token))
                    If candidate IsNot Nothing AndAlso (newest Is Nothing OrElse candidate.Version.CompareTo(newest.Version) > 0) Then newest = candidate
                    page = Nothing
                    Dim links As IEnumerable(Of String) = Nothing
                    If response.Headers.TryGetValues("Link", links) Then
                        For Each link In links
                            For Each match As Match In Regex.Matches(link, "<([^>]+)>;\s*rel=""next""")
                                Dim nextPage = match.Groups(1).Value
                                Dim uri As Uri = Nothing
                                If Not Uri.TryCreate(nextPage, UriKind.Absolute, uri) OrElse uri.Scheme <> "https" OrElse uri.Host <> "api.github.com" OrElse Not uri.IsDefaultPort OrElse uri.UserInfo <> "" OrElse uri.Fragment <> "" OrElse uri.AbsolutePath <> "/repos/preseznik/DiRT2VR/releases" OrElse Not Regex.IsMatch(uri.Query, "^\?(per_page=100&page=[1-9][0-9]*|page=[1-9][0-9]*&per_page=100)$") Then Throw New IOException("Invalid release pagination URL.")
                                page = nextPage
                            Next
                        Next
                    End If
                End Using
            End While
        End Using
        Return newest
    End Function
    Public Function DownloadAsync(update As ReleaseUpdate, folder As String, progress As IProgress(Of Integer), token As CancellationToken) As Task(Of String)
        ' Disk flush, hashing and antivirus inspection must not block the window thread.
        Return Task.Run(Function() DownloadCoreAsync(update, folder, progress, token), token)
    End Function
    Private Async Function DownloadCoreAsync(update As ReleaseUpdate, folder As String, progress As IProgress(Of Integer), token As CancellationToken) As Task(Of String)
        If update.Download Is Nothing Then Throw New IOException("This release has no verifiable installer. Open Releases for manual installation.")
        Files.NoLinks(folder) : Directory.CreateDirectory(folder)
        Dim target = IO.Path.Combine(folder, Guid.NewGuid().ToString("N") & "-" & update.AssetName)
        Dim temporary = target & ".part"
        Try
            Using timeout = CancellationTokenSource.CreateLinkedTokenSource(token)
                timeout.CancelAfter(TimeSpan.FromMinutes(10))
                token = timeout.Token
                Using response = Await client.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, token)
                    response.EnsureSuccessStatusCode()
                    Using source = Await response.Content.ReadAsStreamAsync(token), output As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, True)
                        Dim buffer(81919) As Byte, total As Long, lastPercent As Integer = -1
                        While True
                            Dim count = Await source.ReadAsync(buffer, token)
                            If count = 0 Then Exit While
                            total += count
                            If total > update.Size OrElse total > MaxDownload Then Throw New IOException("Update download exceeded its expected size.")
                            Await output.WriteAsync(buffer.AsMemory(0, count), token)
                            Dim percent = CInt(total * 100 \ update.Size)
                            If percent <> lastPercent Then progress?.Report(percent)
                            lastPercent = percent
                        End While
                        output.Flush(True)
                        If total <> update.Size Then Throw New IOException("Update download was incomplete. Please retry.")
                    End Using
                End Using
                token.ThrowIfCancellationRequested()
                If Not String.Equals(Files.Hash(temporary), update.Digest, StringComparison.OrdinalIgnoreCase) Then Throw New IOException("Update checksum does not match GitHub. The download was discarded.")
                token.ThrowIfCancellationRequested()
                File.Move(temporary, target)
                Return target
            End Using
        Finally
            If File.Exists(temporary) Then File.Delete(temporary)
        End Try
    End Function
    Public Shared Function PrepareInstallerAsync(context As InstallContext, recoverFiles As Action, token As CancellationToken, Optional beforeInstaller As Action = Nothing) As Task
        Return Task.Run(Sub()
                            token.ThrowIfCancellationRequested()
                            ' Mutex ownership is thread-affine: acquire and release inside this one worker.
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
                                    token.ThrowIfCancellationRequested()
                                    recoverFiles()
                                    token.ThrowIfCancellationRequested()
                                    beforeInstaller?.Invoke()
                                    token.ThrowIfCancellationRequested()
                                Finally
                                    guard.ReleaseMutex()
                                End Try
                            End Using
                        End Sub, token)
    End Function
End Class
