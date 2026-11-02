Imports System.IO.MemoryMappedFiles
Imports System.Text

' In-game Next event panel (ProJict 6289354e): the stock tracks and cars the
' launcher offers go to the DLL, and the event chosen in the game comes back.
' Lifetime is one game launch, like DirectReturnChannel.
Public NotInheritable Class NextEventChannel
    Implements IDisposable
    Public Const Size As Integer = 20480
    Private Const TrackTable As Integer = 256, TrackStride As Integer = 160, CarTable As Integer = 10496, CarStride As Integer = 128, MaxItems As Integer = 64
    Private ReadOnly mapping As MemoryMappedFile
    Private ReadOnly view As MemoryMappedViewAccessor
    Private ReadOnly tracks As PracticeTrack()
    Private ReadOnly cars As PracticeCar()
    Public Sub New(context As InstallContext, start As ProcessStartInfo, settings As VrSettings, enabled As Boolean)
        start.Environment.Remove("DIRT2VR_EVENT_CHANNEL")
        If Not enabled Then Return
        tracks = StockTracks(context) : cars = StockCars(context)
        If tracks.Length = 0 OrElse cars.Length = 0 Then Return
        Dim name = "Local\DiRT2VR.Event." & Guid.NewGuid().ToString("N")
        mapping = MemoryMappedFile.CreateNew(name, Size) : view = mapping.CreateViewAccessor()
        view.Write(0, &H544E5645) : view.Write(4, 1) : view.Write(8, Size)
        view.Write(12, tracks.Length) : view.Write(16, cars.Length)
        view.Write(20, If(settings.LaunchMode = "race", 1, 0))
        view.Write(24, Array.FindIndex(tracks, Function(t) t.Id = settings.TrackId))
        view.Write(28, Array.FindIndex(cars, Function(c) c.Code = settings.CarCode))
        view.Write(32, settings.Opponents) : view.Write(36, settings.Laps)
        For i = 0 To tracks.Length - 1
            Dim offset = TrackTable + i * TrackStride
            view.Write(offset, If(tracks(i).Circuit, 1, 0))
            WriteText(offset + 16, 24, tracks(i).Event) : WriteText(offset + 64, 48, tracks(i).Label)
        Next
        For i = 0 To cars.Length - 1
            WriteText(CarTable + i * CarStride, 48, cars(i).Label)
        Next
        start.Environment("DIRT2VR_EVENT_CHANNEL") = name
    End Sub
    ' The launcher's Direct choices without the prototype track, which runs
    ' only in desktop practice; External and custom content stay launcher-only.
    Public Shared Function StockTracks(context As InstallContext) As PracticeTrack()
        Return RaceCatalog.Current.Tracks.Where(Function(t) t.Id <> PrototypeTrack.Id AndAlso PrototypeTrack.Installed(t, context)).
            OrderBy(Function(t) t.Event, StringComparer.Ordinal).ThenBy(Function(t) t.Label, StringComparer.Ordinal).Take(MaxItems).ToArray()
    End Function
    Public Shared Function StockCars(context As InstallContext) As PracticeCar()
        Return RaceCatalog.Current.Cars.Where(Function(c) File.Exists(IO.Path.Combine(context.GameRoot, "cars", c.Code, "cameras.xml"))).
            OrderBy(Function(c) If(c.Code = "sti", "", c.Label), StringComparer.Ordinal).Take(MaxItems).ToArray()
    End Function
    Private Sub WriteText(offset As Integer, characters As Integer, text As String)
        Dim bytes = Encoding.Unicode.GetBytes(text.Substring(0, Math.Min(characters - 1, text.Length)))
        view.WriteArray(offset, bytes, 0, bytes.Length)
    End Sub
    ' The event started from the panel, or Nothing. Read after the game exits.
    Public Function Choice() As NextEvent
        If view Is Nothing OrElse view.ReadUInt32(40) = 0 Then Return Nothing
        Dim mode = view.ReadInt32(20), track = view.ReadInt32(24), car = view.ReadInt32(28), opponents = view.ReadInt32(32), laps = view.ReadInt32(36)
        If mode < 0 OrElse mode > 1 OrElse track < 0 OrElse track >= tracks.Length OrElse car < 0 OrElse car >= cars.Length OrElse
            opponents < 1 OrElse opponents > 7 OrElse laps < 1 OrElse laps > 20 Then Return Nothing
        Return New NextEvent With {.Mode = If(mode = 1, "race", "practice"), .TrackId = tracks(track).Id, .CarCode = cars(car).Code, .Opponents = opponents, .Laps = laps}
    End Function
    Public Sub Dispose() Implements IDisposable.Dispose
        view?.Dispose()
        mapping?.Dispose()
    End Sub
End Class
Public Class NextEvent
    Public Property Mode As String = "practice"
    Public Property TrackId As String = ""
    Public Property CarCode As String = ""
    Public Property Opponents As Integer = 7
    Public Property Laps As Integer = 1
    ' Car grade, opponent cars, difficulty and race start stay the launcher's;
    ' a staggered start falls back to Automatic where the track cannot stagger.
    Public Sub ApplyTo(settings As VrSettings)
        settings.LaunchMode = Mode : settings.TrackId = TrackId : settings.CarCode = CarCode
        settings.Opponents = Opponents : settings.Laps = Laps
        If settings.RaceStart = "staggered" AndAlso Not DirectRaceStart.SupportsStaggered(TrackId) Then settings.RaceStart = "auto"
    End Sub
End Class
