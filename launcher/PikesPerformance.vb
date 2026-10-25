Imports DiRT2VR.CustomTracks

Public Module PikesPerformance
    Public Sub Configure(context As InstallContext, start As ProcessStartInfo, settings As VrSettings)
        Const variable = "DIRT2VR_PIKES_PERFORMANCE_ASSETS"
        start.Environment.Remove(variable)
        If Not settings.DirectMode OrElse Not TrackPacks.Pikes.IsLayout(settings.TrackId) Then Return
        ' The session preparation already verifies asset hashes. Read installed
        ' layout metadata here; a saved build preference never changes a launch.
        Dim receipt = TrackPacks.Pikes.Read(context.GameRoot, False)
        Dim selected = receipt.Sessions.Single(Function(s) s.LayoutId = settings.TrackId)
        If selected.PerformanceAssets Then start.Environment(variable) = TrackPacks.Pikes.GetLayout(settings.TrackId).Folder
    End Sub
End Module
