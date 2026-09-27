Imports System.IO
Imports DiRT2VR

Public Module WorkerFailureTests
    Public Sub Run(repo As String, context As InstallContext, check As Action(Of Boolean, String))
        Dim camera = Path.GetFullPath(Path.Combine(context.GameRoot, "cars/sti/cameras.xml"))
        Dim effects = Path.GetFullPath(Path.Combine(context.GameRoot, "postprocess/effects.xml"))
        Dim cameraHash = Files.Hash(camera), effectsHash = Files.Hash(effects)
        Dim attributes = File.GetAttributes(effects)
        Dim attempts As New List(Of WorkerFailure)
        Try
            File.SetAttributes(effects, attributes Or FileAttributes.ReadOnly)
            Try
                Worker.Run(context, "prepare")
                Throw New Exception("Read-only effect should fail preparation")
            Catch ex As UnauthorizedAccessException
                Dim failure = WorkerFailure.FromException("prepare", False, ex)
                check(failure.Target = effects AndAlso failure.Attributes.Contains("ReadOnly") AndAlso failure.ExitCode = 5, "prepare reports exact read-only target and access-denied code")
                check(failure.HResult = "0x80070005" AndAlso failure.ExceptionType.Contains("UnauthorizedAccessException"), "worker preserves exception type and HRESULT")
                attempts.Add(failure)
            End Try
            check(New AssetTransaction(context).Pending AndAlso Files.Hash(camera) <> cameraHash, "failed preparation retains journal after first asset changes")
            Worker.Run(context, "recover")
            check(Files.Hash(camera) = cameraHash AndAlso Files.Hash(effects) = effectsHash, "partial preparation restores original bytes with read-only untouched asset")

            Dim executable = Path.Combine(repo, "launcher/bin/Release/net10.0-windows/win-x64/DiRT2VR.exe")
            check(File.Exists(executable), "built launcher available for real worker IPC test")
            For Each elevatedLabel In {False, True}
                Dim token = Guid.NewGuid().ToString("N")
                Dim resultPath = WorkerFailure.ResultPath(context, token)
                Files.SaveJson(resultPath, New WorkerFailure)
                Dim start As New ProcessStartInfo(executable) With {.UseShellExecute = False, .CreateNoWindow = True}
                For Each arg In {"--worker", "prepare", "--game", context.GameRoot, "--owner-base", Path.GetDirectoryName(context.UserRoot), "--worker-result", token, "--quiet"}
                    start.ArgumentList.Add(arg)
                Next
                ' Exercise both report stages without requesting UAC in automated tests.
                If elevatedLabel Then start.ArgumentList.Add("--worker-elevated")
                Using child = Process.Start(start)
                    If Not child.WaitForExit(30000) Then Throw New Exception("Worker IPC test timed out")
                    check(child.ExitCode = 5, "real worker returns access-denied without blocking dialog")
                    Dim report = WorkerFailure.ReadResult(resultPath, "prepare", elevatedLabel, child.ExitCode)
                    check(report.Target = effects AndAlso report.Elevated = elevatedLabel, "real worker transfers target and retry stage in Unicode/spaced installation")
                    If elevatedLabel Then attempts.Add(report)
                End Using
                File.Delete(resultPath)
                Worker.Run(context, "recover")
            Next
            Dim message = WorkerFailure.FailureMessage(context, attempts)
            check(message.Contains(effects) AndAlso message.Contains("administrator retry") AndAlso message.Contains("0x80070005"), "parent failure explains file, elevation and actual error")
            Dim saved = Files.ReadJson(Of List(Of WorkerFailure))(Path.Combine(context.UserRoot, "worker-error.json"))
            check(saved.Count = 2 AndAlso Not saved(0).Elevated AndAlso saved(1).Elevated, "bounded failure report retains both attempts")
        Finally
            File.SetAttributes(effects, attributes)
            Worker.Run(context, "recover")
        End Try
        check(Files.Hash(camera) = cameraHash AndAlso Files.Hash(effects) = effectsHash, "worker IPC failures preserve original game assets")
        Dim missing = WorkerFailure.ReadResult(Path.Combine(context.UserRoot, "missing.json"), "prepare", True, -1073741819)
        check(missing.Message.Contains("without an error report") AndAlso missing.ExitCode = -1073741819, "abrupt native exit retains actual exit code")
        Dim rejected As Boolean
        Try
            WorkerFailure.ResultPath(context, "../elsewhere")
        Catch ex As IOException
            rejected = True
        End Try
        check(rejected, "worker result identifiers cannot choose arbitrary paths")
    End Sub
End Module
