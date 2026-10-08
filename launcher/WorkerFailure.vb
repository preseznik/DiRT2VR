Public Class WorkerFailure
    Public Property Operation As String = ""
    Public Property Elevated As Boolean
    Public Property ExitCode As Integer
    Public Property ExceptionType As String = ""
    Public Property HResult As String = ""
    Public Property Message As String = ""
    Public Property Target As String = ""
    Public Property Attributes As String = ""
    Public Property FileOperation As String = ""
    Public Property WindowsErrorCode As Integer?
    Public Property CleanupFailure As String = ""
    Public Property Details As String = ""
    Public Property UpdatedUtc As DateTime = DateTime.UtcNow

    Public Shared Function FromException(operation As String, elevated As Boolean, ex As Exception) As WorkerFailure
        Dim result As New WorkerFailure With {
            .Operation = operation, .Elevated = elevated, .ExitCode = If(TypeOf ex Is UnauthorizedAccessException, 5, 1),
            .ExceptionType = ex.GetType().FullName, .HResult = "0x" & ex.HResult.ToString("X8"),
            .Message = ex.Message, .Target = TryCast(ex.Data("DiRT2VR.Target"), String), .Details = ex.ToString()}
        result.FileOperation = TryCast(ex.Data("DiRT2VR.FileOperation"), String)
        result.CleanupFailure = TryCast(ex.Data("DiRT2VR.Cleanup"), String)
        If TypeOf ex Is ComponentModel.Win32Exception Then
            result.WindowsErrorCode = DirectCast(ex, ComponentModel.Win32Exception).NativeErrorCode
        ElseIf (ex.HResult And &HFFFF0000) = &H80070000 Then
            result.WindowsErrorCode = ex.HResult And &HFFFF
        End If
        If result.Target IsNot Nothing Then
            Try
                result.Attributes = File.GetAttributes(result.Target).ToString()
            Catch
                ' A missing/inaccessible target must not obscure the original error.
            End Try
        End If
        Return result
    End Function

    Public Shared Function ResultPath(context As InstallContext, token As String) As String
        Dim id As Guid
        If Not Guid.TryParseExact(token, "N", id) Then Throw New IOException("Invalid worker result identifier.")
        Return IO.Path.Combine(context.UserRoot, "worker-result-" & id.ToString("N") & ".json")
    End Function

    Public Shared Function ReadResult(path As String, operation As String, elevated As Boolean, exitCode As Integer) As WorkerFailure
        Try
            Dim value = Files.ReadJson(Of WorkerFailure)(path)
            If value IsNot Nothing AndAlso value.Operation = operation AndAlso value.Elevated = elevated AndAlso value.ExceptionType <> "" Then
                value.ExitCode = exitCode
                Return value
            End If
        Catch
            ' Includes native crashes and runtime startup failures before Main.
        End Try
        Return New WorkerFailure With {.Operation = operation, .Elevated = elevated, .ExitCode = exitCode,
            .Message = "The file worker exited without an error report. Check Windows Application errors for DiRT2VR.exe."}
    End Function

    Public Shared Function FailureMessage(context As InstallContext, attempts As List(Of WorkerFailure)) As String
        Dim failure = attempts.Last()
        Dim message = "File operation failed: " & failure.Operation & If(failure.Elevated, " (administrator retry)", "") &
            ". Exit code " & failure.ExitCode.ToString() & " (0x" & failure.ExitCode.ToString("X8") & ")." & Environment.NewLine & failure.Message
        If Not String.IsNullOrEmpty(failure.ExceptionType) Then message &= Environment.NewLine & failure.ExceptionType & " " & failure.HResult
        If Not String.IsNullOrEmpty(failure.Target) Then message &= Environment.NewLine & "File: " & failure.Target & If(failure.Attributes = "", "", " [" & failure.Attributes & "]")
        If Not String.IsNullOrEmpty(failure.FileOperation) Then message &= Environment.NewLine & "Action: " & failure.FileOperation
        If failure.WindowsErrorCode.HasValue Then
            message &= Environment.NewLine & "Windows error " & failure.WindowsErrorCode.Value.ToString() & ": " & New ComponentModel.Win32Exception(failure.WindowsErrorCode.Value).Message
            If {32, 33}.Contains(failure.WindowsErrorCode.Value) Then message &= " Close the application using this file, then retry recovery."
        End If
        If Not String.IsNullOrEmpty(failure.CleanupFailure) Then message &= Environment.NewLine & "Cleanup or protection restoration also failed; details are included in the report."
        Dim report = IO.Path.Combine(context.UserRoot, "worker-error.json")
        Try
            ' One bounded failure report, even when optional rendering logs are off.
            Files.SaveJson(report, attempts)
            message &= Environment.NewLine & "Details: " & report
        Catch
            message &= Environment.NewLine & "The diagnostic report could not be saved."
        End Try
        Return message & Environment.NewLine & "Backups were preserved. Use Restore original files before retrying."
    End Function
End Class
