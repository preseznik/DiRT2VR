Imports System.Globalization
Imports System.Text.Json
Imports System.Xml
Imports DiRT2VR.CustomTracks
Imports EgoEngineLibrary.Xml

Public Class FilterTransaction
    Inherits EffectTransaction
    Public Sub New(context As InstallContext)
        MyBase.New(context, "filter")
    End Sub
    Public Overloads Sub Prepare(preset As FilterPreset, vr As Boolean, Optional afterWrite As Action(Of Integer) = Nothing)
        preset.Validate()
        MyBase.Prepare(Function(relative, original) Patch(original, preset, relative, vr), afterWrite)
    End Sub
    Public Shared Function Patch(original As Byte(), preset As FilterPreset, relative As String, vr As Boolean) As Byte()
        preset.Validate()
        If preset.IsNeutral() OrElse relative = "frontend/persistentdatarender.xml" Then Return original
        If original.Length > 8 * 1024 * 1024 Then Throw New IOException("Filter effect file is too large.")
        Using input As New MemoryStream(original)
            Dim xml As New XmlFile(input), document = xml.Document
            Dim effects = document.SelectNodes("/PostProcessEffects/Effect[@id='2'][@chain='Type6']")
            If effects.Count = 0 Then Return original ' Special-purpose effects are not the normal scene.
            If effects.Count <> 1 Then Throw New IOException("Ambiguous normal-scene effect: " & relative)
            Dim effect = effects(0), adjustment = If(IO.Path.GetFileName(relative) = "night_effects.xml", preset.Night, preset.Day)
            Dim changed As Boolean
            For Each p In FilterParameters.All
                If vr AndAlso p.Key = "motionBlur" Then Continue For
                If preset.Day.Value(p) = p.DefaultValue AndAlso preset.Night.Value(p) = p.DefaultValue Then Continue For
                For Each name In p.XmlNames
                    ' Require explicit values on both variants. A partial override must not
                    ' accidentally inherit an already-adjusted daytime value at night.
                    Dim node = Parameter(effect, p.XmlGroup, name, relative), value = Number(node)
                    If adjustment.Value(p) = p.DefaultValue Then Continue For
                    Dim adjusted = If(p.Key = "brightness", value + adjustment.Value(p) / 1000.0, value * adjustment.Value(p) / 100.0)
                    If Not Double.IsFinite(adjusted) Then Throw New IOException("Non-finite filter result: " & relative)
                    If value <> adjusted Then node.InnerText = adjusted.ToString("R", CultureInfo.InvariantCulture) : changed = True
                Next
            Next
            If preset.Day.TintEnabled OrElse preset.Night.TintEnabled Then
                Dim colour = Parameter(effect, "ColourBalance", "tintColour", relative), amount = Parameter(effect, "ColourBalance", "tintAmount", relative)
                If adjustment.TintEnabled Then
                    Dim rgb = adjustment.TintRgb
                    colour.InnerText = String.Join(", ", Enumerable.Range(0, 3).Select(Function(i) Integer.Parse(rgb.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) & ", 255"
                    amount.InnerText = (adjustment.TintStrength / 100.0).ToString("R", CultureInfo.InvariantCulture) : changed = True
                End If
            End If
            If Not changed Then Return original
            Ordered(effect, "ToneMap", "minLuminance", "maxLuminance", relative)
            Ordered(effect, "BloomFromDsX4", "lowerThreshold", "upperThreshold", relative)
            Using output As New MemoryStream()
                xml.Write(output, xml.Type) : Return output.ToArray()
            End Using
        End Using
    End Function
    Private Shared Function Parameter(effect As XmlNode, group As String, name As String, relative As String) As XmlNode
        Dim nodes = effect.SelectNodes("ParameterGroup[@name='" & group & "']/Param[@name='" & name & "']")
        If nodes.Count <> 1 Then Throw New IOException("This filter cannot safely adjust an incomplete or duplicate " & group & "/" & name & " in " & relative & ". Choose Original to keep this track's effects.")
        Return nodes(0)
    End Function
    Private Shared Function Number(node As XmlNode) As Double
        Dim value As Double
        If Not Double.TryParse(node.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, value) OrElse Not Double.IsFinite(value) Then Throw New IOException("Invalid numeric filter parameter: " & node.Attributes("name").Value)
        Return value
    End Function
    Private Shared Sub Ordered(effect As XmlNode, group As String, low As String, high As String, relative As String)
        Dim minimum = Number(Parameter(effect, group, low, relative)), maximum = Number(Parameter(effect, group, high, relative))
        If minimum < 0 OrElse maximum <= 0 OrElse minimum > maximum Then Throw New IOException("Filter results invert the " & group & " limits in " & relative & ". Adjust the filter or choose Original.")
    End Sub
End Class
Public Class FilterSnapshot
    Public Property Version As Integer = 1
    Public Property Preset As FilterPreset
    Public Property Vr As Boolean
End Class
Public Module FilterLaunch
    Public Function SnapshotPath(context As InstallContext, token As String) As String
        Dim id As Guid
        If Not Guid.TryParseExact(token, "N", id) Then Throw New IOException("Invalid filter launch snapshot.")
        Return SafeFiles.Inside(context.UserRoot, "filter-launch/" & token & ".json")
    End Function
    Public Function Create(context As InstallContext, settings As VrSettings, vr As Boolean) As String
        Dim preset = New FilterStore(context).Load(If(vr, settings.VrFilterId, settings.DesktopFilterId))
        If preset.Id = "original" Then Return Nothing
        Dim token = Guid.NewGuid().ToString("N")
        SafeFiles.Atomic(SnapshotPath(context, token), JsonSerializer.SerializeToUtf8Bytes(New FilterSnapshot With {.Preset = preset, .Vr = vr}, FilterStore.Json))
        Return token
    End Function
    Private Function ReadSnapshot(context As InstallContext, token As String) As FilterSnapshot
        Dim path = SnapshotPath(context, token)
        If New FileInfo(path).Length > 128 * 1024 Then Throw New IOException("Filter launch snapshot is too large.")
        Dim snapshot As FilterSnapshot
        Try
            Dim bytes = File.ReadAllBytes(path)
            Using document = JsonDocument.Parse(bytes)
                If document.RootElement.ValueKind <> JsonValueKind.Object Then Throw New IOException("Invalid filter launch snapshot.")
                For Each fieldName In {"Version", "Preset", "Vr"}
                    Dim value As JsonElement
                    If Not document.RootElement.TryGetProperty(fieldName, value) Then Throw New IOException("Incomplete filter launch snapshot: " & fieldName)
                Next
            End Using
            snapshot = JsonSerializer.Deserialize(Of FilterSnapshot)(bytes, FilterStore.Json)
        Catch ex As JsonException
            Throw New IOException("Invalid filter launch snapshot.", ex)
        End Try
        If snapshot Is Nothing OrElse snapshot.Version <> 1 OrElse snapshot.Preset Is Nothing Then Throw New IOException("Invalid filter launch snapshot.")
        snapshot.Preset.Validate()
        Return snapshot
    End Function
    Public Sub ValidateFiles(context As InstallContext, token As String)
        If token Is Nothing Then Return
        context.RequireClosed()
        Dim snapshot = ReadSnapshot(context, token)
        If snapshot.Preset.IsNeutral() Then Return
        For Each relative In New FilterTransaction(context).Targets()
            Dim path = SafeFiles.Inside(context.GameRoot, relative)
            If New FileInfo(path).Length > 8 * 1024 * 1024 Then Throw New IOException("Filter effect file is too large: " & relative)
            FilterTransaction.Patch(File.ReadAllBytes(path), snapshot.Preset, relative, snapshot.Vr)
        Next
    End Sub
    Public Sub Prepare(context As InstallContext, token As String)
        Dim snapshot = ReadSnapshot(context, token)
        Call (New FilterTransaction(context)).Prepare(snapshot.Preset, snapshot.Vr)
    End Sub
End Module

