Public Class PackageManifest
    Public Property Version As String = ""
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
        Call (New AssetTransaction(context)).Recover()
        Dim receiptPath = IO.Path.Combine(context.ModRoot, "installation.json")
        Dim target = IO.Path.Combine(context.GameRoot, "d3d11.dll")
        Files.NoLinks(target) : Files.NoLinks(receiptPath)
        If File.Exists(target) Then
            If Not File.Exists(receiptPath) Then Throw New IOException("The proxy has no ownership receipt. It was preserved.")
            Dim receipt = Files.ReadJson(Of InstallationReceipt)(receiptPath)
            If receipt Is Nothing OrElse receipt.Version <> 1 OrElse Files.Hash(target) <> receipt.ProxyHash Then Throw New IOException("The installed proxy changed outside DiRT2VR. It was preserved.")
            File.Delete(target)
        End If
        If File.Exists(receiptPath) Then File.Delete(receiptPath)
    End Sub
End Class

Public Module Worker
    Public Sub Run(context As InstallContext, operation As String, Optional carCode As String = "sti", Optional trackId As String = Nothing, Optional opponents As Integer = 0, Optional opponentCars As String = "same")
        context.ValidateGame() : context.RequireClosed()
        Select Case operation
            Case "setup" : Call (New Installation(context)).Setup()
            Case "prepare", "prepare-desktop"
                Dim transaction As New AssetTransaction(context)
                transaction.Recover() : transaction.Prepare(carCode:=carCode, trackId:=trackId, configOnly:=operation = "prepare-desktop", opponents:=opponents, opponentCars:=opponentCars)
            Case "recover" : Call (New AssetTransaction(context)).Recover()
            Case "remove" : Call (New Installation(context)).RemoveProxy()
            Case Else : Throw New ArgumentException("Unknown file operation.")
        End Select
    End Sub
    Public Sub Invoke(context As InstallContext, operation As String, Optional carCode As String = "sti", Optional trackId As String = Nothing, Optional opponents As Integer = 0, Optional opponentCars As String = "same")
        Dim start As New ProcessStartInfo(Environment.ProcessPath) With {.UseShellExecute = False, .CreateNoWindow = True}
        For Each arg In {"--worker", operation, "--game", context.GameRoot, "--owner-base", IO.Path.GetDirectoryName(context.UserRoot)}
            start.ArgumentList.Add(arg)
        Next
        If operation = "prepare" OrElse operation = "prepare-desktop" Then
            start.ArgumentList.Add("--opponent-cars") : start.ArgumentList.Add(opponentCars)
            start.ArgumentList.Add("--opponents") : start.ArgumentList.Add(opponents.ToString(Globalization.CultureInfo.InvariantCulture))
            start.ArgumentList.Add("--car") : start.ArgumentList.Add(carCode)
            If trackId IsNot Nothing Then
                start.ArgumentList.Add("--track") : start.ArgumentList.Add(trackId)
            End If
        End If
        If Environment.GetCommandLineArgs().Contains("--quiet") Then start.ArgumentList.Add("--quiet")
        Using child = Process.Start(start)
            child.WaitForExit()
            If child.ExitCode = 0 Then Return
            If child.ExitCode <> 5 Then Throw New IOException("File operation failed: " & operation & ". See the worker error message; backups were preserved.")
        End Using
        ' Only this fixed-purpose worker elevates; it never starts the game.
        start.UseShellExecute = True : start.Verb = "runas" : start.WindowStyle = ProcessWindowStyle.Hidden
        Using child = Process.Start(start)
            child.WaitForExit()
            If child.ExitCode <> 0 Then Throw New IOException("File operation did not finish: " & operation & ". Recovery may still be pending.")
        End Using
    End Sub
End Module
