' A stock car's liveries come from the embedded catalog: the game's vehicle_livery rows
' for that vehicle, in the game's own order. An external car's come from its pack's
' Car.Liveries, registered for the session as "<code>_00", "<code>_01", ... Direct
' starts give every car "<car>_00"; a chosen livery replaces it on the player's car
' only (src/car_livery.cpp).
Public Class PracticeLivery
    Public Property Code As String = ""
    ' The catalog's vehicle_livery id. 0 for an external car's: its rows exist only in the session database.
    Public Property Id As Integer
    Public Property Label As String = ""
    Public Overrides Function ToString() As String
        Return Label
    End Function
End Class

Public Module CarLiveries
    ' The livery every Direct start already gives the car.
    Public Function GameLivery(carCode As String) As String
        Return carCode & "_00"
    End Function
    ' Empty for the Ferrari F2004 and for external cars with one livery, which keep their own.
    Public Function Choices(carCode As String) As IReadOnlyList(Of PracticeLivery)
        If CarGrades.IsStock(carCode) Then Return RaceCatalog.Current.Car(carCode).Liveries
        Dim names = CustomCars.ExternalCars.Find(carCode)?.Manifest.Car.Liveries
        If names Is Nothing Then Return Array.Empty(Of PracticeLivery)()
        Return names.Select(Function(name, number) New PracticeLivery With {.Code = carCode & "_" & number.ToString("00", Globalization.CultureInfo.InvariantCulture), .Label = name}).ToArray()
    End Function
    Public Function Find(carCode As String, livery As String) As PracticeLivery
        Return Choices(carCode).FirstOrDefault(Function(l) l.Code = livery)
    End Function
    ' Whether a saved choice (car preferences, replays) is well formed. A stock car's must exist; an external
    ' car's may outlive a removed or changed pack, and then the car keeps its first livery (Effective).
    Public Function Saveable(carCode As String, livery As String) As Boolean
        If CarGrades.IsStock(carCode) Then Return Find(carCode, livery) IsNot Nothing
        Return CustomTracks.ExternalPacks.AvailableCarCode(If(carCode, "")) AndAlso livery IsNot Nothing AndAlso
            Text.RegularExpressions.Regex.IsMatch(livery, "^" & carCode & "_[0-9]{2}\z") AndAlso CInt(livery.Substring(4)) < CustomTracks.ExternalPacks.MaximumLiveries
    End Function
    ' The car's saved choice, or "" for the game's livery.
    Public Function ChosenFor(context As InstallContext, carCode As String) As String
        Dim livery As String = Nothing
        Return If(CustomCarPreferences.Load(context).Liveries.TryGetValue(carCode, livery), livery, "")
    End Function
    ' Nothing keeps the game's livery: other launch modes, other cars' liveries and "_00".
    Public Function Effective(settings As VrSettings) As PracticeLivery
        If Not settings.DirectMode OrElse settings.CarLivery = GameLivery(settings.CarCode) Then Return Nothing
        Return Find(settings.CarCode, settings.CarLivery)
    End Function
    ' Runs after the worker prepared the session: an external car's livery rows exist only in the session database.
    Public Sub Configure(start As ProcessStartInfo, context As InstallContext, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_CAR_LIVERY")
        Dim livery = Effective(settings)
        If livery Is Nothing Then Return
        Dim external = Not CarGrades.IsStock(settings.CarCode)
        Dim folder = IO.Path.Combine(context.GameRoot, "cars", settings.CarCode, "livery_" & livery.Code.Substring(livery.Code.Length - 2))
        Files.NoLinks(folder)
        If Not Directory.Exists(folder) Then Throw New IOException("The " & livery.Label & " livery for " & RaceCatalog.Current.Car(settings.CarCode).Label & " is missing. " &
            If(external, "Replace the complete pack, then Refresh.", "Verify your game files or choose another livery."))
        Dim id = If(external, CustomCars.CarRegistration.LiveryId(File.ReadAllBytes(CustomTracks.SafeFiles.Inside(context.GameRoot, "database/database.bin")), settings.CarCode, livery.Code), livery.Id)
        start.Environment("DIRT2VR_CAR_LIVERY") = id.ToString(Globalization.CultureInfo.InvariantCulture)
    End Sub
End Module
