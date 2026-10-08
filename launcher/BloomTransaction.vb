Imports System.Globalization
Imports System.Xml
Imports EgoEngineLibrary.Xml

Public Class BloomTransaction
    Inherits EffectTransaction
    Public Sub New(value As InstallContext)
        MyBase.New(value, "bloom")
    End Sub
    Public Overloads Sub Prepare(Optional afterWrite As Action(Of Integer) = Nothing)
        MyBase.Prepare(Function(relative, original) Patch(original), afterWrite)
    End Sub
    Public Shared Function Enabled(settings As VrSettings, start As ProcessStartInfo) As Boolean
        Return If(start.Environment.ContainsKey("DIRT2VR_HEADSET") AndAlso start.Environment("DIRT2VR_HEADSET") = "1", settings.VrBloom, settings.DesktopBloom)
    End Function
    Public Shared Function Patch(original As Byte()) As Byte()
        If original.Length > 8 * 1024 * 1024 Then Throw New IOException("Bloom effect file is too large; it was preserved.")
        Using input As New MemoryStream(original)
            Dim xml As New XmlFile(input), document = xml.Document
            If document.DocumentElement Is Nothing OrElse Not {"xml", "PostProcessEffects"}.Contains(document.DocumentElement.Name) Then Throw New IOException("Unsupported bloom effect file; it was preserved.")
            ' Keep tone mapping, colour grading, thresholds and luminance reductions intact.
            ' Partial track/frontend groups override the base effect, so include every group.
            Dim changed As Boolean
            For Each node As XmlElement In document.SelectNodes("//ParameterGroup[@name='FinalMix']/Param[@name='bloomAmount'] | //ParameterGroup[@name='BloomFromDsX4']/Param[@name='intensity' or @name='mixMedium' or @name='mixLarge' or @name='mixHuge' or @name='mixStreak']")
                Dim value As Double
                If Not Double.TryParse(node.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, value) OrElse Not Double.IsFinite(value) OrElse value < 0 Then Throw New IOException("Unsupported bloom parameter: " & node.GetAttribute("name"))
                If value = 0 Then Continue For
                node.InnerText = "0.000000" : changed = True
            Next
            If Not changed Then Return original
            Using output As New MemoryStream()
                xml.Write(output, xml.Type)
                Return output.ToArray()
            End Using
        End Using
    End Function

End Class
