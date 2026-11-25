' A stock car's liveries come from the embedded catalog: the game's vehicle_livery rows
' for that vehicle, in the game's own order. Direct starts give every car "<car>_00";
' a chosen livery replaces it on the player's car only (src/car_livery.cpp).
Public Class PracticeLivery
    Public Property Code As String = ""
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
    ' Empty for CUSTOM and external cars, which keep their own livery.
    Public Function Choices(carCode As String) As IReadOnlyList(Of PracticeLivery)
        If Not CarGrades.IsStock(carCode) Then Return Array.Empty(Of PracticeLivery)()
        Return RaceCatalog.Current.Car(carCode).Liveries
    End Function
    Public Function Find(carCode As String, livery As String) As PracticeLivery
        Return Choices(carCode).FirstOrDefault(Function(l) l.Code = livery)
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
    Public Sub Configure(start As ProcessStartInfo, context As InstallContext, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_CAR_LIVERY")
        Dim livery = Effective(settings)
        If livery Is Nothing Then Return
        Dim folder = IO.Path.Combine(context.GameRoot, "cars", settings.CarCode, "livery_" & livery.Code.Substring(livery.Code.Length - 2))
        Files.NoLinks(folder)
        If Not Directory.Exists(folder) Then Throw New IOException("The " & livery.Label & " livery for " & RaceCatalog.Current.Car(settings.CarCode).Label & " is missing. Verify your game files or choose another livery.")
        start.Environment("DIRT2VR_CAR_LIVERY") = livery.Id.ToString(Globalization.CultureInfo.InvariantCulture)
    End Sub
End Module
