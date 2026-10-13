Imports System.Xml.Linq
Imports DiRT2VR.CustomTracks

Public Class ReplayCarRequirement
    Public Property Id As String = ""
    Public Property Code As String = ""
    Public Property Version As String = ""
    Public Property ManifestSha256 As String = ""
End Class

' Saved with the recording, never reconstructed from a new random opponent selection.
Public Class ReplayContentRequirement
    Public Property Version As Integer = 1
    Public Property TrackId As String = ""
    Public Property CustomTrackVersion As String = ""
    Public Property ExternalManifestSha256 As String = ""
    Public Property ExternalCars As New List(Of ReplayCarRequirement)
    Public Property Cars As New List(Of String)
    Public Property RequiredFiles As New List(Of String)
End Class

Public Module ReplayContent
    Private Const MaximumFiles As Integer = 8192

    Public Function Capture(context As InstallContext, trackId As String, launchConfig As XDocument, Optional token As CancellationToken = Nothing) As ReplayContentRequirement
        Dim result As New ReplayContentRequirement With {.TrackId = trackId, .Cars = RecordedCars(trackId, launchConfig)}
        If TrackPacks.IsLayout(trackId) Then result.CustomTrackVersion = LayoutVersion(TrackPacks.ForLayout(trackId).Read(context.GameRoot, False, token), trackId)
        If TrackPacks.IsLayout(trackId) AndAlso TrackPacks.ForLayout(trackId).External IsNot Nothing Then result.ExternalManifestSha256 = TrackPacks.ForLayout(trackId).External.ManifestHash
        Dim track = RaceCatalog.Current.Track(trackId)
        Dim paths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each code In result.Cars
            Dim car = CustomCars.ExternalCars.Find(code)
            If car IsNot Nothing Then result.ExternalCars.Add(New ReplayCarRequirement With {.Id = car.Id, .Code = code, .Version = car.Manifest.Version, .ManifestSha256 = car.ManifestHash})
            AddFiles(context.GameRoot, IO.Path.Combine(context.GameRoot, "cars", code), True, paths, token)
        Next
        AddFiles(context.GameRoot, IO.Path.Combine(context.GameRoot, "tracks"), False, paths, token)
        AddFiles(context.GameRoot, track.Folder(context), True, paths, token)
        AddFiles(context.GameRoot, IO.Path.GetDirectoryName(track.Folder(context)), False, paths, token)
        result.RequiredFiles = paths.OrderBy(Function(p) p, StringComparer.OrdinalIgnoreCase).ToList()
        RequireAvailable(context, result, token)
        Return result
    End Function

    Public Function RecordedCars(trackId As String, launchConfig As XDocument, Optional recorded As ReplayContentRequirement = Nothing) As List(Of String)
        Dim entry = launchConfig?.Root?.Element("track")
        If entry Is Nothing Then Throw New IOException("The replay track does not match the recorded event.")
        ' Library inspection must still work when a portable pack is absent.
        ' Validate its saved identity here; RequireAvailable checks the actual pack before playback.
        If recorded IsNot Nothing AndAlso recorded.ExternalManifestSha256 <> "" Then
            If Not SafeFiles.Digest(recorded.ExternalManifestSha256) OrElse
                Not System.Text.RegularExpressions.Regex.IsMatch(trackId, "^external-track-[a-z0-9][a-z0-9-]{0,47}/[a-z0-9][a-z0-9-]{0,47}$") OrElse
                CStr(entry.Attribute("country")) <> "usa" OrElse CStr(entry.Attribute("route")) <> "route_0" OrElse
                Not System.Text.RegularExpressions.Regex.IsMatch(If(CStr(entry.Attribute("name")), ""), "^d2ve_[a-z0-9_]{1,48}$") Then Throw New IOException("Invalid recorded external track.")
        Else
            Dim track = RaceCatalog.Current.Track(trackId)
            If CStr(entry.Attribute("country")) <> track.Country OrElse CStr(entry.Attribute("name")) <> track.Track OrElse CStr(entry.Attribute("route")) <> track.Route Then
                Throw New IOException("The replay track does not match the recorded event.")
            End If
        End If
        If recorded IsNot Nothing AndAlso (recorded.ExternalCars Is Nothing OrElse recorded.ExternalCars.Count > 8 OrElse recorded.ExternalCars.Any(Function(c) c Is Nothing) OrElse
            recorded.ExternalCars.Select(Function(c) c.Code).Distinct().Count() <> recorded.ExternalCars.Count) Then Throw New IOException("Invalid recorded external cars.")
        Dim result As New List(Of String)
        Dim count As Integer
        For Each car In entry.Elements("car")
            Dim number As Integer
            If Not Integer.TryParse(CStr(car.Attribute("number")), number) OrElse number < 1 OrElse number > 8 Then Throw New IOException("Invalid recorded replay grid.")
            count += number
            Dim code = CStr(car.Attribute("name"))
            Dim external = recorded?.ExternalCars.FirstOrDefault(Function(c) c.Code = code)
            If external IsNot Nothing Then
                If Not ExternalPacks.AvailableCarCode(If(code, "")) OrElse Not SafeFiles.Version(external.Version) OrElse Not SafeFiles.Digest(external.ManifestSha256) OrElse
                    Not System.Text.RegularExpressions.Regex.IsMatch(If(external.Id, ""), "^external-car-[a-z0-9][a-z0-9-]{0,47}$") Then Throw New IOException("Invalid recorded external car.")
            Else
                code = RaceCatalog.Current.Car(code).Code
            End If
            If Not result.Contains(code, StringComparer.Ordinal) Then result.Add(code)
        Next
        If count < 1 OrElse count > 8 Then Throw New IOException("Invalid recorded replay grid.")
        Return result
    End Function

    Private Function LayoutVersion(receipt As PackReceipt, trackId As String) As String
        Dim selected = receipt.Sessions.SingleOrDefault(Function(s) s.LayoutId = trackId)
        If selected Is Nothing Then Throw New IOException("The replay's custom track is not installed. Install that layout before playback.")
        Return If(selected.Version, receipt.Version)
    End Function

    Private Sub AddFiles(root As String, folder As String, recursive As Boolean, paths As HashSet(Of String), token As CancellationToken)
        token.ThrowIfCancellationRequested()
        Files.NoLinks(folder)
        If Not Directory.Exists(folder) Then Throw New IOException("Replay content is missing: " & IO.Path.GetRelativePath(root, folder))
        ' Walk explicitly so a linked subdirectory is rejected before traversal.
        For Each file In Directory.EnumerateFiles(folder)
            token.ThrowIfCancellationRequested()
            Files.NoLinks(file)
            paths.Add(IO.Path.GetRelativePath(root, file).Replace("\"c, "/"c))
            If paths.Count > MaximumFiles Then Throw New IOException("The replay requires too many content files.")
        Next
        If recursive Then
            For Each child In Directory.EnumerateDirectories(folder) : AddFiles(root, child, True, paths, token) : Next
        End If
    End Sub

    Public Sub RequireAvailable(context As InstallContext, requirement As ReplayContentRequirement, Optional token As CancellationToken = Nothing)
        If requirement Is Nothing OrElse requirement.Version <> 1 OrElse requirement.Cars Is Nothing OrElse
            requirement.Cars.Count < 1 OrElse requirement.Cars.Count > 8 OrElse requirement.RequiredFiles Is Nothing OrElse
            requirement.RequiredFiles.Count > MaximumFiles Then Throw New IOException("Unsupported replay content list.")
        Dim external = If(TrackPacks.IsLayout(requirement.TrackId), TrackPacks.ForLayout(requirement.TrackId).External, Nothing)
        If external IsNot Nothing Then
            If Not external.Manifest.Replay OrElse requirement.ExternalManifestSha256 <> external.ManifestHash Then Throw New IOException("This replay requires the same external track pack version and contents used to record it. Restore that pack, then Refresh.")
        ElseIf requirement.ExternalManifestSha256 <> "" Then
            Throw New IOException("This replay requires an external track pack that is missing or incompatible. Restore the original pack to the external tracks folder, then Refresh.")
        End If
        If requirement.ExternalCars Is Nothing OrElse requirement.ExternalCars.Count > 8 OrElse
            requirement.ExternalCars.Any(Function(c) c Is Nothing) OrElse requirement.ExternalCars.Select(Function(c) c.Code).Distinct().Count() <> requirement.ExternalCars.Count Then Throw New IOException("Invalid replay external-car requirements.")
        For Each item In requirement.ExternalCars
            Dim pack = CustomCars.ExternalCars.Require(item.Code)
            If Not requirement.Cars.Contains(item.Code) OrElse pack.Id <> item.Id OrElse pack.Manifest.Version <> item.Version OrElse pack.ManifestHash <> item.ManifestSha256 OrElse Not pack.Manifest.Replay Then Throw New IOException("This replay requires the same external car pack version and contents used to record it. Restore that pack, then Refresh.")
            pack.Verify(token)
        Next
        For Each code In requirement.Cars
            If CustomCars.ExternalCars.Find(code) IsNot Nothing AndAlso Not requirement.ExternalCars.Any(Function(c) c.Code = code) Then Throw New IOException("The replay is missing its external car identity.")
        Next
        Dim track = RaceCatalog.Current.Track(requirement.TrackId)
        Dim cars = requirement.Cars.Select(Function(code) RaceCatalog.Current.Car(code)).ToArray()
        If cars.Select(Function(c) c.Code).Distinct(StringComparer.Ordinal).Count() <> cars.Length Then Throw New IOException("Duplicate car in replay content list.")
        Dim trackRoot = "tracks/" & track.Country & "/" & track.Track & "/"
        Dim routeRoot = trackRoot & track.Route & "/"
        Dim carRoots = cars.Select(Function(c) "cars/" & c.Code & "/").ToArray()
        Dim required As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each relative In requirement.RequiredFiles
            Try
                SafeFiles.Relative(relative)
            Catch ex As IOException
                Throw New IOException("Invalid file path in replay content list.", ex)
            End Try
            If Not ((relative.StartsWith("tracks/", StringComparison.OrdinalIgnoreCase) AndAlso Not relative.Substring(7).Contains("/"c)) OrElse
                    relative.StartsWith(routeRoot, StringComparison.OrdinalIgnoreCase) OrElse
                    (relative.StartsWith(trackRoot, StringComparison.OrdinalIgnoreCase) AndAlso Not relative.Substring(trackRoot.Length).Contains("/"c)) OrElse
                    carRoots.Any(Function(root) relative.StartsWith(root, StringComparison.OrdinalIgnoreCase))) OrElse Not required.Add(relative) Then
                Throw New IOException("Invalid file path in replay content list.")
            End If
        Next
        ' A truncated/old manifest cannot make empty content folders pass validation.
        For Each car In cars
            For Each name In {"cameras.xml", car.Code & ".ctf", car.Code & "_highLOD.pssg"}
                required.Add("cars/" & car.Code & "/" & name)
            Next
        Next
        ' route.clm is an optional baked shadow map (e.g. Aspen night layouts omit it).
        ' When present, Capture and the custom-pack receipt still require it.
        For Each name In {"track.jpk", "grids.pssg", "progress_track.xml"} : required.Add(routeRoot & name) : Next
        Dim problems As New List(Of String)
        For Each relative In required
            token.ThrowIfCancellationRequested()
            Dim path = SafeFiles.Inside(context.GameRoot, relative)
            If external IsNot Nothing AndAlso relative.StartsWith(trackRoot, StringComparison.Ordinal) Then
                Dim packed = external.Manifest.Files.FirstOrDefault(Function(f) f.Path = "payload/" & relative)
                If packed Is Nothing Then Throw New IOException("External replay refers to a file outside the prepared pack: " & relative)
                path = SafeFiles.Inside(external.Root, packed.Path)
            End If
            For Each item In requirement.ExternalCars
                If relative.StartsWith("cars/" & item.Code & "/", StringComparison.Ordinal) Then
                    Dim pack = CustomCars.ExternalCars.Require(item.Code)
                    Dim packed = pack.Manifest.Files.FirstOrDefault(Function(f) f.Path = "payload/" & relative)
                    If packed Is Nothing Then Throw New IOException("External replay car file is outside the prepared pack: " & relative)
                    path = SafeFiles.Inside(pack.Root, packed.Path)
                End If
            Next
            If Not File.Exists(path) OrElse New FileInfo(path).Length = 0 Then problems.Add(relative)
        Next
        If problems.Count <> 0 Then
            Throw New IOException("Required track or car files are missing or empty. Install or repair this content:" & vbCrLf &
                String.Join(vbCrLf, problems.Take(12)) & If(problems.Count > 12, vbCrLf & "And " & (problems.Count - 12).ToString() & " more.", ""))
        End If
        If TrackPacks.IsLayout(requirement.TrackId) Then
            Dim pack = TrackPacks.ForLayout(requirement.TrackId)
            Dim receipt = pack.Read(context.GameRoot, False, token)
            If String.IsNullOrEmpty(requirement.CustomTrackVersion) OrElse LayoutVersion(receipt, requirement.TrackId) <> requirement.CustomTrackVersion Then
                Throw New IOException("This replay needs " & pack.Name & " version " & requirement.CustomTrackVersion & ". The installed track version differs.")
            End If
            pack.Verify(context.GameRoot, receipt, token)
            CustomTrackService.RequireLauncher(receipt)
        ElseIf requirement.CustomTrackVersion <> "" Then
            Throw New IOException("Unexpected custom-track version in replay content list.")
        End If
    End Sub
End Module
