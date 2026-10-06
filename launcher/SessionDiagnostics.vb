Imports System.Text.Json
Imports System.Text.Json.Nodes

Public Module SessionDiagnostics
    ' Snapshot effective in-memory settings, including launch-only overrides.
    ' Machine paths and device instance identifiers are unnecessary in a shared report.
    Public Sub Save(context As InstallContext, folder As String, settings As VrSettings, driving As DrivingControls, vr As Boolean)
        If folder Is Nothing Then Return
        Dim snapshot = JsonSerializer.SerializeToNode(settings)
        snapshot("Runtime") = IO.Path.GetFileName(settings.Runtime)
        For Each binding In snapshot("Bindings").AsArray()
            binding.AsObject().Remove("Device")
        Next
        Dim controls = JsonSerializer.SerializeToNode(driving)
        For Each binding In controls("Bindings").AsArray()
            binding.AsObject().Remove("DeviceId")
        Next
        Files.SaveJson(IO.Path.Combine(folder, "dirt2vr-settings.json"), New With {
            .SchemaVersion = 1, .CapturedUtc = DateTime.UtcNow, .Version = BuildInfo.Version,
            .FullVersion = BuildInfo.FullVersion, .Channel = BuildInfo.Channel, .BuildUtc = BuildInfo.BuildDate,
            .Mode = If(vr, "VR", "Desktop"), .Settings = snapshot, .DrivingControls = controls,
            .SeatPositionsAtLaunch = SeatPositions.Load(context),
            .RequestedVrResolution = If(vr, New Integer() {settings.RenderWidth, settings.RenderHeight}, Nothing),
            .ConfiguredTrack = If(settings.DirectMode, RaceCatalog.Current.Track(settings.TrackId), Nothing),
            .ConfiguredCar = If(settings.DirectMode, RaceCatalog.Current.Car(settings.CarCode), Nothing),
            .ContextNote = "Configured selections apply only to Direct practice/Race. Observed route loads and player cars are recorded as 'event context:' in trace.log when the VR observer is active. Catalog discipline is not the career series title. Runtime directories and controller instance IDs are omitted; this is a diagnostic snapshot, not an importable settings backup."})
    End Sub
End Module
