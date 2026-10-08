Public Class PackageManifest
    Public Property Version As String = ""
    Public Property Channel As String = "Stable"
    Public Property Files As New Dictionary(Of String, String)
End Class
Public Class InstallationReceipt
    Public Property Version As Integer = 1
    Public Property ProxyHash As String = ""
End Class
Public Class Installation
    Private ReadOnly context As InstallContext
    Public Sub New(value As InstallContext)
        context = value
    End Sub
    Public Function PayloadHash() As String
        Dim manifest = Files.ReadJson(Of PackageManifest)(IO.Path.Combine(context.ModRoot, "package.json"))
        Const relative As String = "DiRT2VR/payload/d3d11.dll"
        If manifest Is Nothing OrElse manifest.Files Is Nothing OrElse Not manifest.Files.ContainsKey(relative) Then Throw New IOException("Package manifest is missing the VR proxy.")
        Dim payload = IO.Path.Combine(context.GameRoot, relative)
        Files.NoLinks(payload)
        If Files.Hash(payload) <> manifest.Files(relative) Then Throw New IOException("The packaged proxy is damaged. Extract the complete package again.")
        Return manifest.Files(relative)
    End Function
    Public Sub Check()
        context.ValidateGame() : context.RequireClosed()
        Files.NoLinks(context.ModRoot)
        Dim expected = PayloadHash()
        Dim target = IO.Path.Combine(context.GameRoot, "d3d11.dll")
        Files.NoLinks(target)
        If File.Exists(target) Then
            Dim current = Files.Hash(target)
            Dim receiptPath = IO.Path.Combine(context.ModRoot, "installation.json")
            Dim receipt = If(File.Exists(receiptPath), Files.ReadJson(Of InstallationReceipt)(receiptPath), Nothing)
            If current <> expected AndAlso (receipt Is Nothing OrElse receipt.Version <> 1 OrElse current <> receipt.ProxyHash) Then Throw New IOException("Another or modified d3d11.dll already exists in the game folder. DiRT2VR will not overwrite it. Remove that mod using its own instructions first.")
        End If
    End Sub
    Public Sub Setup()
        Check()
        Dim target = IO.Path.Combine(context.GameRoot, "d3d11.dll")
        Dim expected = PayloadHash()
        If Not File.Exists(target) OrElse Files.Hash(target) <> expected Then Files.AtomicWrite(target, File.ReadAllBytes(IO.Path.Combine(context.ModRoot, "payload\d3d11.dll")))
        Files.SaveJson(IO.Path.Combine(context.ModRoot, "installation.json"), New InstallationReceipt With {.ProxyHash = expected})
    End Sub
    Public Sub RemoveProxy()
        context.RequireClosed()
        Call (New BloomTransaction(context)).Recover()
        Call (New FilterTransaction(context)).Recover()
        Call (New DirectMenus(context)).Recover()
        Call (New StartupMovies(context)).Recover()
        Call (New LanTransaction(context)).Recover()
        Call (New AssetTransaction(context)).Recover()
        Dim receiptPath = IO.Path.Combine(context.ModRoot, "installation.json")
        Dim target = IO.Path.Combine(context.GameRoot, "d3d11.dll")
        Files.NoLinks(target) : Files.NoLinks(receiptPath)
        If File.Exists(target) Then
            If Not File.Exists(receiptPath) Then Throw New IOException("The proxy has no ownership receipt. It was preserved.")
            Dim receipt = Files.ReadJson(Of InstallationReceipt)(receiptPath)
            If receipt Is Nothing OrElse receipt.Version <> 1 OrElse Files.Hash(target) <> receipt.ProxyHash Then Throw New IOException("The installed proxy changed outside DiRT2VR. It was preserved.")
            CustomTracks.SafeFiles.DeleteOwned(target)
        End If
        If File.Exists(receiptPath) Then CustomTracks.SafeFiles.DeleteOwned(receiptPath)
    End Sub
End Class

Public Module Worker
    Public Sub Run(context As InstallContext, operation As String, Optional carCode As String = "sti", Optional trackId As String = Nothing, Optional opponents As Integer = 0, Optional opponentCars As String = "same", Optional workId As String = Nothing, Optional postProcessTest As String = Nothing, Optional filterSnapshot As String = Nothing)
        postProcessTest = If(postProcessTest, If(operation = "prepare" OrElse operation = "prepare-desktop", ButtermilkPostProcess.DefaultProfile(trackId), "normal"))
        ButtermilkPostProcess.Validate(postProcessTest, trackId)
        context.ValidateGame() : context.RequireClosed()
        Select Case operation
            Case "setup" : Call (New Installation(context)).Setup()
            Case "prepare", "prepare-desktop"
                If CustomTracks.TrackPacks.IsLayout(trackId) Then
                    CustomTracks.TrackPacks.ForLayout(trackId).RequireMode(trackId, operation <> "prepare-desktop", If(opponents = 0, "practice", "race"), carCode, opponents, 1)
                    CustomTracks.SessionFiles.Recover(context.GameRoot)
                    RaceCatalog.Current.ValidateInstalled(context, trackId, carCode)
                    Dim receipt = CustomTracks.TrackPacks.ForLayout(trackId).Read(context.GameRoot, False)
                    CustomTrackService.RequireLauncher(receipt)
                    If opponents > 0 AndAlso Not CustomTracks.TrackPacks.ForLayout(trackId).SupportsRace(receipt, trackId) Then Throw New IOException("Rebuild " & CustomTracks.TrackPacks.ForLayout(trackId).Name & " to update its AI driving paths before starting a Race. Direct practice is still available.")
                End If
                Dim transaction As New AssetTransaction(context)
                transaction.Recover() : transaction.Prepare(carCode:=carCode, trackId:=trackId, configOnly:=operation = "prepare-desktop", opponents:=opponents, opponentCars:=opponentCars)
                If CustomTracks.TrackPacks.IsLayout(trackId) Then
                    Dim progressPatch As Func(Of Byte(), Byte()) = Nothing
                    If CustomTracks.TrackPacks.Nordschleife.IsLayout(trackId) Then progressPatch = AddressOf NordschleifeProgress.Patch
                    CustomTracks.SessionFiles.Prepare(context.GameRoot, trackId, progressPatch)
                End If
                ' Layer exposure after VR motion-blur changes; recover in the reverse order.
                If CustomTracks.TrackPacks.IsLayout(trackId) Then Call (New ButtermilkPostProcess(context)).Prepare(postProcessTest, trackId)
            Case "prepare-lan"
                Call (New LanTransaction(context)).Prepare()
            Case "prepare-bloom"
                Call (New BloomTransaction(context)).Prepare()
            Case "prepare-filter"
                FilterLaunch.Prepare(context, filterSnapshot)
            Case "prepare-profile"
                Call (New LanTransaction(context)).Prepare(offlineProfile:=True)
            Case "prepare-movies"
                Call (New StartupMovies(context)).Prepare()
            Case "prepare-direct-menus", "prepare-direct-menus-movies"
                Call (New DirectMenus(context)).Prepare(operation = "prepare-direct-menus-movies")
            Case "recover"
                Call (New BloomTransaction(context)).Recover()
                Call (New FilterTransaction(context)).Recover()
                Call (New ButtermilkPostProcess(context)).Recover()
                CustomTracks.SessionFiles.Recover(context.GameRoot)
                CustomTracks.PackInstallation.Recover(context.GameRoot)
                Call (New DirectMenus(context)).Recover()
                Call (New StartupMovies(context)).Recover()
                Call (New LanTransaction(context)).Recover()
                Call (New AssetTransaction(context)).Recover()
            Case "install-custom"
                Call (New BloomTransaction(context)).Recover()
                Call (New FilterTransaction(context)).Recover()
                Call (New ButtermilkPostProcess(context)).Recover()
                CustomTracks.SessionFiles.Recover(context.GameRoot)
                CustomTracks.PackInstallation.Install(context.GameRoot, IO.Path.Combine(CustomTrackService.Staging(context, workId), "conversion/install"), BuildInfo.Version)
            Case "remove-custom"
                Call (New BloomTransaction(context)).Recover()
                Call (New FilterTransaction(context)).Recover()
                Call (New ButtermilkPostProcess(context)).Recover()
                CustomTracks.PackInstallation.Uninstall(context.GameRoot, If(trackId, CustomTracks.AspenPack.Id))
            Case "remove"
                Call (New BloomTransaction(context)).Recover()
                Call (New FilterTransaction(context)).Recover()
                Call (New ButtermilkPostProcess(context)).Recover()
                CustomTracks.SessionFiles.Recover(context.GameRoot)
                Call (New Installation(context)).RemoveProxy()
            Case Else : Throw New ArgumentException("Unknown file operation.")
        End Select
    End Sub
    Public Sub Invoke(context As InstallContext, operation As String, Optional carCode As String = "sti", Optional trackId As String = Nothing, Optional opponents As Integer = 0, Optional opponentCars As String = "same", Optional quiet As Boolean = False, Optional workId As String = Nothing, Optional postProcessTest As String = Nothing, Optional filterSnapshot As String = Nothing)
        Dim start As New ProcessStartInfo(Environment.ProcessPath) With {.UseShellExecute = False, .CreateNoWindow = True}
        For Each arg In {"--worker", operation, "--game", context.GameRoot, "--owner-base", IO.Path.GetDirectoryName(context.UserRoot)}
            start.ArgumentList.Add(arg)
        Next
        If workId IsNot Nothing Then
            CustomTrackService.Staging(context, workId)
            start.ArgumentList.Add("--track-work") : start.ArgumentList.Add(workId)
        End If
        If operation = "prepare-filter" Then
            FilterLaunch.SnapshotPath(context, filterSnapshot)
            start.ArgumentList.Add("--filter-snapshot") : start.ArgumentList.Add(filterSnapshot)
        End If
        If operation = "prepare" OrElse operation = "prepare-desktop" Then
            postProcessTest = If(postProcessTest, If(operation = "prepare" OrElse operation = "prepare-desktop", ButtermilkPostProcess.DefaultProfile(trackId), "normal"))
            ButtermilkPostProcess.Validate(postProcessTest, trackId)
            start.ArgumentList.Add("--postprocess-test") : start.ArgumentList.Add(postProcessTest)
            start.ArgumentList.Add("--opponent-cars") : start.ArgumentList.Add(opponentCars)
            start.ArgumentList.Add("--opponents") : start.ArgumentList.Add(opponents.ToString(Globalization.CultureInfo.InvariantCulture))
            start.ArgumentList.Add("--car") : start.ArgumentList.Add(carCode)
            If trackId IsNot Nothing Then
                start.ArgumentList.Add("--track") : start.ArgumentList.Add(trackId)
            End If
        End If
        If operation = "remove-custom" AndAlso trackId IsNot Nothing Then
            CustomTracks.TrackPacks.Get(trackId)
            start.ArgumentList.Add("--track") : start.ArgumentList.Add(trackId)
        End If
        If quiet OrElse Environment.GetCommandLineArgs().Contains("--quiet") Then start.ArgumentList.Add("--quiet")
        Dim token = Guid.NewGuid().ToString("N")
        Dim resultPath = WorkerFailure.ResultPath(context, token)
        start.ArgumentList.Add("--worker-result") : start.ArgumentList.Add(token)
        Dim attempts As New List(Of WorkerFailure)
        Try
            For Each elevated In {False, True}
                ' Pre-create under the owning user; the elevated worker replaces only this result.
                Files.SaveJson(resultPath, New WorkerFailure)
                If elevated Then
                    ' Only this fixed-purpose worker elevates; it never starts the game.
                    start.UseShellExecute = True : start.Verb = "runas" : start.WindowStyle = ProcessWindowStyle.Hidden
                    start.ArgumentList.Add("--worker-elevated")
                End If
                Dim exitCode As Integer
                Try
                    Using child = Process.Start(start)
                        child.WaitForExit() : exitCode = child.ExitCode
                    End Using
                Catch ex As ComponentModel.Win32Exception
                    Dim failure = WorkerFailure.FromException(operation, elevated, ex)
                    failure.ExitCode = ex.NativeErrorCode : attempts.Add(failure)
                    Throw New IOException(WorkerFailure.FailureMessage(context, attempts), ex)
                End Try
                If exitCode = 0 Then Return
                attempts.Add(WorkerFailure.ReadResult(resultPath, operation, elevated, exitCode))
                If elevated OrElse exitCode <> 5 Then Throw New IOException(WorkerFailure.FailureMessage(context, attempts))
            Next
        Finally
            Try
                File.Delete(resultPath)
            Catch
                ' Preserve the operation's result if temporary-report cleanup fails.
            End Try
        End Try
    End Sub
End Module
