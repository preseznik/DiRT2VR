Imports System.Text

' Launcher → Photos → View in VR: the packaged photo viewer shows photos in the headset,
' side-by-side (-SBS) photos in 3D. It uses the VR runtime chosen for the game.
Public NotInheritable Class PhotoVrViewer
    Private Const Relative As String = "DiRT2VR/payload/photo_viewer.exe"

    Public Shared Function Verified(packageRoot As String) As String
        Dim executable = IO.Path.Combine(packageRoot, Relative)
        Dim receipt = IO.Path.Combine(packageRoot, "DiRT2VR", "package.json")
        Files.NoLinks(receipt) : Files.NoLinks(executable)
        Dim manifest = Files.ReadJson(Of PackageManifest)(receipt)
        If Not File.Exists(executable) OrElse manifest?.Files Is Nothing OrElse Not manifest.Files.ContainsKey(Relative) OrElse Files.Hash(executable) <> manifest.Files(Relative) Then
            Throw New IOException("The VR photo viewer is missing or damaged. Extract the complete package again.")
        End If
        Return executable
    End Function

    ' Returns False when the player cancelled the VR runtime choice.
    Public Shared Function Show(context As InstallContext, settings As VrSettings, paths As IList(Of String), index As Integer) As Boolean
        If paths Is Nothing OrElse paths.Count = 0 Then Return False
        Dim executable = Verified(context.GameRoot)
        Dim runtime = VrRuntime.Choose(context, settings)
        If runtime Is Nothing Then Return False
        ' The list goes in a file: hundreds of paths would not fit on a command line.
        Dim list = IO.Path.Combine(IO.Path.GetTempPath(), "DiRT2VR-photos-" & Guid.NewGuid().ToString("N") & ".txt")
        File.WriteAllLines(list, paths, New UTF8Encoding(False))
        Dim start As New ProcessStartInfo(executable) With {.UseShellExecute = False, .WorkingDirectory = IO.Path.GetDirectoryName(executable)}
        start.ArgumentList.Add("--list") : start.ArgumentList.Add(list)
        start.ArgumentList.Add("--index") : start.ArgumentList.Add(Math.Clamp(index, 0, paths.Count - 1).ToString(Globalization.CultureInfo.InvariantCulture))
        runtime.Configure(start)
        Dim viewer = Process.Start(start)
        viewer.EnableRaisingEvents = True
        AddHandler viewer.Exited, Sub()
                                      Try
                                          File.Delete(list)
                                      Catch ex As IOException
                                      Catch ex As UnauthorizedAccessException
                                      End Try
                                  End Sub
        Return True
    End Function
End Class
