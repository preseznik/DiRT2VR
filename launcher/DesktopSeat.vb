Imports System.Globalization
Imports System.Xml
Imports DiRT2VR.CustomTracks
Imports EgoEngineLibrary.Xml

' Desktop cockpit seat and field of view. VR moves the headset eye instead, so
' this layer only changes each car's head-cam view for desktop launches.
Public Class DesktopSeatTransaction
    Inherits EffectTransaction
    Public Const GameFov As Integer = 52
    Public Const MinimumFov As Integer = 40
    Public Const MaximumFov As Integer = 90
    Private ReadOnly context As InstallContext
    Public Sub New(value As InstallContext)
        MyBase.New(value, "seat")
        context = value
    End Sub
    Public Shared Function Applies(seats As SeatPositions, fov As Integer) As Boolean
        Dim used = If(seats.UseUniversal, {seats.Universal}, seats.Cars.Values.ToArray())
        Return fov <> GameFov OrElse used.Any(Function(p) p.X <> 0 OrElse p.Y <> 0 OrElse p.Z <> 0)
    End Function
    Public Overrides Function Targets() As String()
        Dim cars = SafeFiles.Inside(context.GameRoot, "cars")
        Dim result As New List(Of String)
        For Each folder In Directory.EnumerateDirectories(cars).OrderBy(Function(p) p, StringComparer.OrdinalIgnoreCase)
            Dim code = IO.Path.GetFileName(folder)
            If Not Text.RegularExpressions.Regex.IsMatch(code, "^[a-z0-9]{3}$") Then Continue For
            SafeFiles.NoLinks(folder)
            If File.Exists(SafeFiles.Inside(folder, "cameras.xml")) Then result.Add("cars/" & code & "/cameras.xml")
        Next
        Return result.ToArray()
    End Function
    Public Overloads Sub Prepare(Optional afterWrite As Action(Of Integer) = Nothing)
        Dim seats = SeatPositions.Load(context, desktop:=True), fov = VrSettings.Load(context).DesktopCockpitFov
        MyBase.Prepare(Function(relative, original) Patch(original, If(seats.UseUniversal, seats.Universal, seats.GetPosition(relative.Split("/"c)(1))), fov), afterWrite)
    End Sub
    Public Shared Function Patch(original As Byte(), seat As SeatPosition, fov As Integer) As Byte()
        seat.Validate()
        If fov < MinimumFov OrElse fov > MaximumFov Then Throw New IOException("Choose a desktop cockpit field of view between 40° and 90°.")
        If seat.X = 0 AndAlso seat.Y = 0 AndAlso seat.Z = 0 AndAlso fov = GameFov Then Return original
        Using input As New MemoryStream(original)
            Dim xml As New XmlFile(input), document = xml.Document
            ' Leave cars with unusual cockpit cameras exactly as they are.
            Dim views = document.SelectNodes("//View[@ident='head-cam']")
            If views.Count <> 1 Then Return original
            Dim position = TryCast(views(0).SelectSingleNode("Parameter[@name='position']"), XmlElement)
            Dim angle = TryCast(views(0).SelectSingleNode("Parameter[@name='fov']"), XmlElement)
            Dim x, y, z As Double
            If position Is Nothing OrElse angle Is Nothing OrElse Not Number(position, "x", x) OrElse Not Number(position, "y", y) OrElse Not Number(position, "z", z) Then Return original
            ' Car space is x left, y up, z forward; seat offsets are right, up and back.
            position.SetAttribute("x", (x - seat.X).ToString("0.000000", CultureInfo.InvariantCulture))
            position.SetAttribute("y", (y + seat.Y).ToString("0.000000", CultureInfo.InvariantCulture))
            position.SetAttribute("z", (z - seat.Z).ToString("0.000000", CultureInfo.InvariantCulture))
            If fov <> GameFov Then angle.SetAttribute("value", fov.ToString("0.0000", CultureInfo.InvariantCulture))
            Using output As New MemoryStream()
                xml.Write(output, xml.Type)
                Return output.ToArray()
            End Using
        End Using
    End Function
    Private Shared Function Number(element As XmlElement, name As String, ByRef value As Double) As Boolean
        Return Double.TryParse(element.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, value) AndAlso Double.IsFinite(value)
    End Function
End Class
