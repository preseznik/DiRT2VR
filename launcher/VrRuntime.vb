Imports Microsoft.Win32
Imports System.Text.Json
Imports System.Windows.Forms

Public Class VrRuntimeInfo
    Public ReadOnly Mode As String
    Public ReadOnly ManifestPath As String
    Public ReadOnly Name As String

    Public Sub New(mode As String, manifestPath As String, name As String)
        Me.Mode = mode : Me.ManifestPath = manifestPath : Me.Name = name
    End Sub

    Public Sub Configure(start As ProcessStartInfo)
        ' Pin both the probe and game to the same runtime for this launch only.
        start.Environment("XR_RUNTIME_JSON") = ManifestPath
    End Sub
End Class

Public Module VrRuntime
    Public ReadOnly Modes As String() = {"ask", "openxr", "steamvr"}
    Public ReadOnly Labels As String() = {"Ask every time", "OpenXR (system default)", "SteamVR"}

    Public Function SystemManifest() As String
        ' The launcher is x64; the game and probe need the x86 registration.
        Try
            Using root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32),
                  key = root.OpenSubKey("SOFTWARE\Khronos\OpenXR\1")
                Return TryCast(key?.GetValue("ActiveRuntime"), String)
            End Using
        Catch ex As Exception When TypeOf ex Is Security.SecurityException OrElse TypeOf ex Is UnauthorizedAccessException
            Throw New IOException("Cannot read the system's 32-bit OpenXR runtime. Check access to the OpenXR registration, or choose SteamVR.", ex)
        End Try
    End Function

    Public Function Resolve(mode As String, steamManifest As String) As VrRuntimeInfo
        Select Case mode
            Case "openxr"
                Dim path = SystemManifest()
                If String.IsNullOrWhiteSpace(path) Then Throw New IOException("No 32-bit OpenXR runtime is registered. Select VDXR in Virtual Desktop Streamer, or choose SteamVR in Settings → VR runtime.")
                Return ReadManifest(mode, path)
            Case "steamvr"
                If String.IsNullOrWhiteSpace(steamManifest) OrElse Not File.Exists(steamManifest) OrElse Not String.Equals(IO.Path.GetFileName(steamManifest), "steamxr_win32.json", StringComparison.OrdinalIgnoreCase) Then Throw New IOException("SteamVR runtime not found. Use Browse beside SteamVR runtime in Settings to select steamxr_win32.json.")
                Return ReadManifest(mode, steamManifest)
            Case Else
                Throw New IOException("Choose OpenXR or SteamVR before launching VR.")
        End Select
    End Function

    Public Function ReadManifest(mode As String, path As String) As VrRuntimeInfo
        Try
            If Not IO.Path.IsPathFullyQualified(path) Then Throw New IOException("The runtime manifest path must be absolute.")
            Using document = JsonDocument.Parse(File.ReadAllText(path))
                Dim runtime = document.RootElement.GetProperty("runtime")
                Dim library = runtime.GetProperty("library_path").GetString()
                If String.IsNullOrWhiteSpace(library) Then Throw New IOException("The runtime manifest has no library path.")
                library = IO.Path.GetFullPath(IO.Path.Combine(IO.Path.GetDirectoryName(path), library))
                Using stream = File.OpenRead(library), reader As New BinaryReader(stream)
                    If stream.Length < 64 OrElse reader.ReadUInt16() <> &H5A4D Then Throw New IOException("The runtime library is not a Windows DLL.")
                    stream.Position = &H3C
                    Dim pe = reader.ReadInt32()
                    If pe < 64 OrElse pe > stream.Length - 6 Then Throw New IOException("The runtime library has an invalid PE header.")
                    stream.Position = pe
                    If reader.ReadUInt32() <> &H4550UI Then Throw New IOException("The runtime library has an invalid PE signature.")
                    If reader.ReadUInt16() <> &H14C Then Throw New IOException("DiRT 2 requires a 32-bit (x86) OpenXR runtime. The selected runtime library is not x86.")
                End Using
                Dim nameElement As JsonElement
                Dim name = If(mode = "steamvr", "SteamVR", "System OpenXR")
                If runtime.TryGetProperty("name", nameElement) AndAlso nameElement.ValueKind = JsonValueKind.String AndAlso Not String.IsNullOrWhiteSpace(nameElement.GetString()) Then name = nameElement.GetString()
                Return New VrRuntimeInfo(mode, IO.Path.GetFullPath(path), name)
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is KeyNotFoundException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is ArgumentException
            Throw New IOException("Cannot use the selected " & If(mode = "steamvr", "SteamVR", "OpenXR") & " runtime. " & ex.Message, ex)
        End Try
    End Function

    Public Function SystemDescription() As String
        Try
            Return "System OpenXR: " & Resolve("openxr", "").Name & ". VDXR support is experimental."
        Catch ex As IOException
            Return ex.Message
        End Try
    End Function

    Public Sub Remember(context As InstallContext, mode As String)
        If mode <> "openxr" AndAlso mode <> "steamvr" Then Throw New ArgumentException("Invalid VR runtime choice.")
        ' Session settings may contain temporary track, replay or LAN overrides.
        Dim saved = VrSettings.Load(context)
        saved.RuntimeMode = mode
        Files.SaveJson(context.PreferencesPath, saved)
    End Sub

    Public Function Choose(context As InstallContext, settings As VrSettings) As VrRuntimeInfo
        Dim mode = settings.RuntimeMode
        If mode = "ask" Then
            Using dialog As New VrRuntimeForm()
                If dialog.ShowDialog() <> DialogResult.OK Then Return Nothing
                mode = dialog.SelectedMode
                Dim selected = Resolve(mode, settings.Runtime)
                If dialog.RememberChoice Then Remember(context, mode)
                Return selected
            End Using
        End If
        Return Resolve(mode, settings.Runtime)
    End Function
End Module
