Imports System.Globalization
Imports System.Xml
Imports EgoEngineLibrary.Xml

' Only authored Pikes effects opt in. Camera geometry is never changed.
Public Class DepthOfFieldTransaction
    Inherits EffectTransaction
    Private trackFolder As String
    Private cameraPath As String
    Public Sub New(context As InstallContext)
        MyBase.New(context, "dof")
    End Sub
    Private Shared Function Folder(trackId As String) As String
        Select Case trackId
            Case "external-track-pikes-peak-lab/tarmac" : Return "d2ve_pikes_tarmac"
            Case "external-track-pikes-peak-lab/gravel-day" : Return "d2ve_pikes_gday"
            Case "external-track-pikes-peak-lab/gravel-alt" : Return "d2ve_pikes_galt"
            Case "external-track-pikes-peak-lab/gravel-wet" : Return "d2ve_pikes_gwet"
            Case "pikes-peak-tarmac" : Return "d2vr_pikes_tarmac"
            Case "pikes-peak-gravel-day" : Return "d2vr_pikes_gday"
            Case "pikes-peak-gravel-alt" : Return "d2vr_pikes_galt"
            Case "pikes-peak-gravel-wet" : Return "d2vr_pikes_gwet"
            Case Else : Return Nothing
        End Select
    End Function
    Public Shared Function Supports(trackId As String) As Boolean
        Return Folder(trackId) IsNot Nothing
    End Function
    Public Overloads Sub Prepare(trackId As String, Optional afterWrite As Action(Of Integer) = Nothing)
        trackFolder = Folder(trackId)
        If trackFolder Is Nothing Then Throw New IOException("This track does not support the depth-of-field setting.")
        MyBase.Prepare(Function(relative, original) Patch(original, relative = "postprocess/effects.xml"), afterWrite)
    End Sub
    Public Sub PrepareForCar(trackId As String, car As String, enabled As Boolean, Optional afterWrite As Action(Of Integer) = Nothing)
        trackFolder = Folder(trackId)
        If trackFolder Is Nothing OrElse Not Text.RegularExpressions.Regex.IsMatch(car, "^[a-z0-9]{3}$") Then Throw New IOException("Unsupported Pikes camera selection.")
        cameraPath = "cars/" & car & "/cameras.xml"
        MyBase.Prepare(Function(relative, original) If(relative = cameraPath, BindCamera(original), If(enabled, original, Patch(original, relative = "postprocess/effects.xml"))), afterWrite)
    End Sub
    Public Overrides Function Targets() As String()
        If trackFolder Is Nothing Then Throw New InvalidOperationException("Select a supported track before preparing depth of field.")
        Dim paths As New List(Of String) From {"postprocess/effects.xml", "tracks/usa/" & trackFolder & "/effects.xml"}
        If cameraPath IsNot Nothing Then paths.Add(cameraPath)
        Return paths.ToArray()
    End Function
    Public Shared Function BindCamera(original As Byte()) As Byte()
        Using input As New MemoryStream(original)
            Dim xml As New XmlFile(input), document = xml.Document
            For Each name In {"chase_close", "chase_far", "chase_close_reverse", "chase_far_reverse"}
                Dim views = document.SelectNodes("//View[@ident='" & name & "']")
                If views.Count <> 1 Then Throw New IOException("Unsupported Pikes chase camera: " & name)
                Dim view = DirectCast(views(0), XmlElement), chain = view.GetAttribute("postProcessChain")
                If chain <> "" AndAlso chain <> "PikesFocusProbe" Then Throw New IOException("The car's existing camera effect was preserved.")
                view.SetAttribute("postProcessChain", "PikesFocusProbe")
            Next
            Using output As New MemoryStream()
                xml.Write(output, xml.Type)
                Return output.ToArray()
            End Using
        End Using
    End Function
    Public Shared Function Patch(original As Byte(), globalEffects As Boolean) As Byte()
        If original.Length > 8 * 1024 * 1024 Then Throw New IOException("Depth-of-field effect file is too large; it was preserved.")
        Using input As New MemoryStream(original)
            Dim xml As New XmlFile(input), document = xml.Document
            If document.DocumentElement Is Nothing OrElse Not {"xml", "PostProcessEffects"}.Contains(document.DocumentElement.Name) Then Throw New IOException("Unsupported depth-of-field effect file; it was preserved.")
            ' The global camera effect has a unique name; all other global effects
            ' (menus, replays and other tracks) must retain their authored values.
            Dim selector = If(globalEffects, "//Effect[@name='PikesFocusProbe']", "") & "//ParameterGroup[@name='Dof']/Param[@name='blurAmount']"
            Dim changed As Boolean
            For Each node As XmlElement In document.SelectNodes(selector)
                Dim value As Double
                If Not Double.TryParse(node.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, value) OrElse Not Double.IsFinite(value) OrElse value < 0 Then Throw New IOException("Unsupported depth-of-field strength; file preserved.")
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
