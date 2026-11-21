' Photo Mode's optional DLSS 5 Neural Rendering. The player supplies the NVIDIA runtime;
' DiRT2VR only ships the x64 helper that loads it (see tools/desktop-bridge).
Public NotInheritable Class NeuralRendering
    Public Const RuntimeFile As String = "nvngx_dlssnr.dll"
    Public Shared Function Valid(folder As String) As Boolean
        Return Not String.IsNullOrWhiteSpace(folder) AndAlso IO.Path.IsPathFullyQualified(folder.Trim()) AndAlso
            File.Exists(IO.Path.Combine(folder.Trim(), RuntimeFile))
    End Function
    Public Shared Function Describe(folder As String) As String
        If String.IsNullOrWhiteSpace(folder) Then Return "Off. Neural rendering needs your own DLSS 5 runtime."
        If Not IO.Path.IsPathFullyQualified(folder.Trim()) Then Return "Use a full folder path."
        If Not Valid(folder) Then Return RuntimeFile & " was not found in this folder."
        Return "Found " & RuntimeFile & ". Press N in Photo Mode."
    End Function
    Public Shared Function Helper(context As InstallContext) As String
        Return IO.Path.Combine(context.ModRoot, "payload", "dirt2vr-desktop-bridge.exe")
    End Function
    ' Desktop Photo Mode only; VR and LAN sessions never get these variables.
    Public Shared Sub Configure(start As ProcessStartInfo, context As InstallContext, settings As VrSettings, photoMode As Boolean, headset As Boolean)
        start.Environment.Remove("DIRT2VR_NEURAL_RUNTIME") : start.Environment.Remove("DIRT2VR_NEURAL_HELPER") : start.Environment.Remove("DIRT2VR_NEURAL_LIVE")
        If Not photoMode OrElse headset OrElse settings.LaunchMode = "lan" OrElse Not Valid(settings.NeuralRuntimeFolder) Then Return
        Dim exe = Helper(context)
        If Not File.Exists(exe) OrElse Not File.Exists(IO.Path.Combine(context.ModRoot, "payload", "nvngx.dll_dirt2vr.dll")) Then Return
        start.Environment("DIRT2VR_NEURAL_RUNTIME") = settings.NeuralRuntimeFolder.Trim()
        start.Environment("DIRT2VR_NEURAL_HELPER") = exe
        If settings.NeuralWhileDriving Then start.Environment("DIRT2VR_NEURAL_LIVE") = "1"
    End Sub
End Class
