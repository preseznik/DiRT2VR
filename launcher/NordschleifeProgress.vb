Imports System.Xml
Imports DiRT2VR.CustomTracks
Imports EgoEngineLibrary.Xml

Public Module NordschleifeProgress
    Public Sub Configure(start As ProcessStartInfo, settings As VrSettings)
        start.Environment.Remove("DIRT2VR_NORDSCHLEIFE_CHECKPOINTS")
        If settings.DirectMode AndAlso TrackPacks.Nordschleife.IsLayout(settings.TrackId) AndAlso settings.SessionLaps = 1 Then start.Environment("DIRT2VR_NORDSCHLEIFE_CHECKPOINTS") = "1"
    End Sub
    Public Function Patch(original As Byte()) As Byte()
        Using input As New MemoryStream(original)
            Dim binary As New XmlFile(input), document = binary.Document
            Dim track = TryCast(document.SelectSingleNode("/progress_track_data/track"), XmlElement)
            Dim route = TryCast(document.SelectSingleNode("/progress_track_data/routes[@num_routes='1']/route[@id='0'][@num_splits='10']"), XmlElement)
            Dim gates = document.SelectNodes("/progress_track_data/gates/gate")
            Dim splits = document.SelectNodes("/progress_track_data/routes/route/split")
            If track Is Nothing OrElse track.GetAttribute("type") <> "point_to_point" OrElse route Is Nothing OrElse gates.Count < 100 OrElse gates.Count > 256 OrElse splits.Count <> 10 OrElse
                DirectCast(splits(0), XmlElement).GetAttribute("type") <> "finish" OrElse DirectCast(splits(0), XmlElement).GetAttribute("gate") <> "1" OrElse
                DirectCast(splits(9), XmlElement).GetAttribute("type") <> "finish" OrElse DirectCast(splits(9), XmlElement).GetAttribute("gate") <> (gates.Count - 1).ToString(Globalization.CultureInfo.InvariantCulture) OrElse
                splits.Cast(Of XmlElement)().Skip(1).Take(8).Any(Function(s) s.GetAttribute("type") <> "time") OrElse
                gates(1).SelectSingleNode("left")?.InnerText <> gates(gates.Count - 1).SelectSingleNode("left")?.InnerText OrElse
                gates(1).SelectSingleNode("right")?.InnerText <> gates(gates.Count - 1).SelectSingleNode("right")?.InnerText Then
                Throw New IOException("Unsupported Nordschleife progress route; files preserved. Rebuild this pack from source.")
            End If
            ' A closed lap has one finish identity. The duplicate terminal finish on
            ' the open route makes native player progress clamp to 100 percent.
            track.SetAttribute("type", "circuit")
            route.RemoveChild(splits(9)) : route.SetAttribute("num_splits", "9")
            Using output As New MemoryStream()
                binary.Write(output, XmlType.BinXml)
                Return output.ToArray()
            End Using
        End Using
    End Function
End Module
