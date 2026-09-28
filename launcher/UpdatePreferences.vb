Imports System.Text.Json

Public Class UpdatePreferences
    Public Property Version As Integer = 1
    Public Property IncludeExperimentalReleases As Boolean = False
    Public Shared Function Load(context As InstallContext) As UpdatePreferences
        Dim path = IO.Path.Combine(context.UserRoot, "update-preferences.json")
        Try
            If Not File.Exists(path) Then Return New UpdatePreferences()
            Dim value = Files.ReadJson(Of UpdatePreferences)(path)
            If value IsNot Nothing AndAlso value.Version = 1 Then Return value
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is UnauthorizedAccessException
            ' Unreadable preferences must never opt the user into experimental releases.
        End Try
        Return New UpdatePreferences()
    End Function
    Public Shared Sub Save(context As InstallContext, includeExperimental As Boolean)
        Dim path = IO.Path.Combine(context.UserRoot, "update-preferences.json")
        Files.NoLinks(path)
        Files.SaveJson(path, New UpdatePreferences With {.IncludeExperimentalReleases = includeExperimental})
    End Sub
    Public Shared Function PrepareRollback(context As InstallContext, token As CancellationToken) As String
        token.ThrowIfCancellationRequested()
        Dim backup = IO.Path.Combine(context.UserRoot, "updates", "rollback-preferences", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") & "-" & Guid.NewGuid().ToString("N"))
        Files.NoLinks(backup)
        Directory.CreateDirectory(backup)
        For Each name In {"settings.json", "driving-controls.json", "driving-controls.xml", "update-preferences.json"}
            token.ThrowIfCancellationRequested()
            Dim source = IO.Path.Combine(context.UserRoot, name)
            Files.NoLinks(source)
            If File.Exists(source) Then File.Copy(source, IO.Path.Combine(backup, name))
        Next
        token.ThrowIfCancellationRequested()
        Save(context, False)
        Return backup
    End Function
End Class
