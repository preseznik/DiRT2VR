Public Module BuildDiagnostics
    Public Sub Run(context As InstallContext, kind As String, packId As String, source As String, build As Action)
        Dim enabled As Boolean
        Try
            enabled = VrSettings.Load(context).LoggingEnabled
        Catch
            ' Optional diagnostics must not prevent a build or hide its failure.
        End Try
        Dim started = DateTime.UtcNow
        Try
            build()
        Catch ex As OperationCanceledException
            Throw
        Catch ex As Exception
            Dim report As String = Nothing
            If enabled Then
                Try
                    Dim folder = Session.CreateLogFolder(context, True)
                    Dim path = IO.Path.Combine(folder, "custom-build-error.json")
                    Files.SaveJson(path, New With {
                        .SchemaVersion = 1, .Version = BuildInfo.Version, .Channel = BuildInfo.Channel,
                        .StartedUtc = started, .FailedUtc = DateTime.UtcNow,
                        .Kind = kind, .PackId = packId, .SourceFolder = source,
                        .ExceptionType = ex.GetType().FullName, .HResult = "0x" & ex.HResult.ToString("X8"),
                        .Message = ex.Message, .Details = ex.ToString()})
                    report = path
                Catch
                    ' Keep the original error if the log cannot be written.
                End Try
            End If
            If report IsNot Nothing Then Throw New IO.IOException(ex.Message & Environment.NewLine & "Build diagnostic: " & report, ex)
            Throw
        End Try
    End Sub
End Module
