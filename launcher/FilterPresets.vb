Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports DiRT2VR.CustomTracks

Public Class FilterParameter
    Public ReadOnly Key As String, Caption As String, Group As String, XmlGroup As String, XmlNames As String(), Help As String
    Public ReadOnly Minimum As Integer, Maximum As Integer, DefaultValue As Integer
    Public Sub New(id As String, title As String, section As String, xmlSection As String, names As String(), low As Integer, high As Integer, initial As Integer, description As String)
        Key = id : Caption = title : Group = section : XmlGroup = xmlSection : XmlNames = names
        Minimum = low : Maximum = high : DefaultValue = initial : Help = description
    End Sub
End Class
Public Module FilterParameters
    Public ReadOnly All As FilterParameter() = {
        New FilterParameter("brightness", "Brightness offset", "Colour", "ColourBalance", {"brighness"}, -100, 100, 0, "Adds a small offset to the track's brightness. Zero keeps its original value."),
        New FilterParameter("contrast", "Contrast", "Colour", "ColourBalance", {"contrast"}, 0, 200, 100, "Adjusts the difference between light and dark areas. 100% keeps the track's setting."),
        New FilterParameter("saturation", "Saturation", "Colour", "ColourBalance", {"colour"}, 0, 200, 100, "Adjusts colour strength. 100% keeps the track's colour setting."),
        New FilterParameter("gamma", "Gamma", "Colour", "ColourBalance", {"gamma"}, 50, 200, 100, "Adjusts the game's gamma value relative to this track. Compare in-game after relaunching."),
        New FilterParameter("bloom", "Intensity", "Bloom", "BloomFromDsX4", {"intensity"}, 0, 200, 100, "Adjusts the glow around bright areas. Main's Bloom switch must also be On."),
        New FilterParameter("threshold", "Activation threshold", "Bloom", "BloomFromDsX4", {"lowerThreshold", "upperThreshold", "mediumBloomThreshold", "hugeBloomThreshold", "streakThreshold"}, 50, 200, 100, "Scales the brightness thresholds used by bloom. 100% keeps the track's thresholds."),
        New FilterParameter("medium", "Medium glow", "Bloom", "BloomFromDsX4", {"mixMedium"}, 0, 200, 100, "Adjusts the medium-sized glow contribution."),
        New FilterParameter("large", "Large glow", "Bloom", "BloomFromDsX4", {"mixLarge"}, 0, 200, 100, "Adjusts the large glow contribution."),
        New FilterParameter("huge", "Wide glow", "Bloom", "BloomFromDsX4", {"mixHuge"}, 0, 200, 100, "Adjusts the widest glow contribution."),
        New FilterParameter("streak", "Streak strength", "Bloom", "BloomFromDsX4", {"mixStreak"}, 0, 200, 100, "Adjusts bloom streaks from bright highlights."),
        New FilterParameter("luminance", "Target luminance", "Exposure", "ToneMap", {"targetLuminance"}, 50, 200, 100, "Adjusts the game's target luminance relative to this track. This is not a fixed exposure or EV control."),
        New FilterParameter("adaptation", "Adaptation strength", "Exposure", "ToneMap", {"adaptStrength"}, 0, 200, 100, "Scales the game's automatic exposure strength. Zero is not a verified auto-exposure Off switch."),
        New FilterParameter("damping", "Adaptation damping", "Exposure", "ToneMap", {"adaptDamping"}, 50, 200, 100, "Scales the game's exposure damping value. Compare transitions between bright and dark areas."),
        New FilterParameter("minLuminance", "Minimum luminance", "Luminance limits", "ToneMap", {"minLuminance"}, 50, 200, 100, "Scales the lower luminance limit. A limit already at zero remains zero."),
        New FilterParameter("maxLuminance", "Maximum luminance", "Luminance limits", "ToneMap", {"maxLuminance"}, 50, 200, 100, "Scales the upper luminance limit. Launch is refused if the resulting limits are inverted."),
        New FilterParameter("motionBlur", "Amount (desktop only)", "Motion blur", "MotionBlur", {"blurLength"}, 0, 200, 100, "Adjusts desktop motion blur. VR keeps its existing motion-blur suppression.")}
    Public Function ByKey(key As String) As FilterParameter
        Return All.SingleOrDefault(Function(p) p.Key = key)
    End Function
End Module
Public Class FilterVariant
    Public Property Values As New Dictionary(Of String, Integer)(StringComparer.Ordinal)
    Public Property TintEnabled As Boolean
    Public Property TintRgb As String = "FFFFFF"
    Public Property TintStrength As Integer
    Public Function Value(parameter As FilterParameter) As Integer
        Return If(Values.ContainsKey(parameter.Key), Values(parameter.Key), parameter.DefaultValue)
    End Function
    Public Sub Validate()
        If Values Is Nothing OrElse Values.Count > FilterParameters.All.Length Then Throw New IOException("Invalid filter adjustments.")
        For Each pair In Values
            Dim p = FilterParameters.ByKey(pair.Key)
            If p Is Nothing OrElse pair.Value < p.Minimum OrElse pair.Value > p.Maximum Then Throw New IOException("Unsupported filter adjustment: " & pair.Key)
        Next
        If TintRgb Is Nothing OrElse TintRgb.Length <> 6 OrElse Not TintRgb.All(Function(c) Uri.IsHexDigit(c)) OrElse TintStrength < 0 OrElse TintStrength > 100 Then Throw New IOException("Invalid filter tint.")
    End Sub
End Class
Public Class FilterPreset
    Public Property Format As String = "DiRT2VR.Filter"
    Public Property Version As Integer = 1
    Public Property Id As String = "original"
    Public Property Name As String = "Original"
    Public Property Day As New FilterVariant()
    Public Property Night As New FilterVariant()
    Public Shared Sub ValidateId(value As String)
        Dim parsed As Guid
        If value <> "original" AndAlso (Not Guid.TryParseExact(value, "N", parsed) OrElse value <> value.ToLowerInvariant()) Then Throw New IOException("Invalid filter preset ID. Select a preset in Graphics → Filters.")
    End Sub
    Public Sub Validate()
        ValidateId(Id)
        If Format <> "DiRT2VR.Filter" OrElse Version <> 1 OrElse String.IsNullOrWhiteSpace(Name) OrElse Name <> Name.Trim() OrElse Name.Length > 64 OrElse Name.Any(Function(c) Char.IsControl(c)) OrElse Day Is Nothing OrElse Night Is Nothing Then Throw New IOException("Unsupported filter preset. Import a DiRT2VR filter file, version 1.")
        Day.Validate() : Night.Validate()
        If Id = "original" AndAlso (Name <> "Original" OrElse Not IsNeutral()) Then Throw New IOException("The Original filter cannot be changed. Create a new preset first.")
    End Sub
    Public Function IsNeutral() As Boolean
        Return {Day, Night}.All(Function(v) Not v.TintEnabled AndAlso FilterParameters.All.All(Function(p) v.Value(p) = p.DefaultValue))
    End Function
    Public Function Copy() As FilterPreset
        Return JsonSerializer.Deserialize(Of FilterPreset)(JsonSerializer.SerializeToUtf8Bytes(Me, FilterStore.Json), FilterStore.Json)
    End Function
    Public Overrides Function ToString() As String
        Return Name
    End Function
End Class
Public Class FilterStore
    Public Shared ReadOnly Json As New JsonSerializerOptions With {.WriteIndented = True, .MaxDepth = 12, .UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow}
    Private ReadOnly folder As String
    Public Sub New(context As InstallContext)
        folder = SafeFiles.Inside(IO.Path.GetDirectoryName(context.UserRoot), "filters")
    End Sub
    Private Function Filename(id As String) As String
        FilterPreset.ValidateId(id)
        If id = "original" Then Throw New IOException("Original is built in and cannot be overwritten or deleted.")
        Return SafeFiles.Inside(folder, id & ".d2vrfilter.json")
    End Function
    Public Shared Function ReadPreset(path As String) As FilterPreset
        SafeFiles.NoLinks(path)
        If New FileInfo(path).Length > 64 * 1024 Then Throw New IOException("Filter preset is too large.")
        Try
            Dim bytes = File.ReadAllBytes(path)
            Using document = JsonDocument.Parse(bytes)
                If document.RootElement.ValueKind <> JsonValueKind.Object Then Throw New IOException("Invalid filter preset object.")
                For Each fieldName In {"Format", "Version", "Id", "Name", "Day", "Night"}
                    Dim value As JsonElement
                    If Not document.RootElement.TryGetProperty(fieldName, value) Then Throw New IOException("Incomplete filter preset: " & fieldName)
                Next
            End Using
            Dim preset = JsonSerializer.Deserialize(Of FilterPreset)(bytes, Json)
            If preset Is Nothing Then Throw New IOException("Empty filter preset.")
            preset.Validate() : Return preset
        Catch ex As JsonException
            Throw New IOException("Invalid filter JSON. Import a DiRT2VR filter file, not game XML.", ex)
        End Try
    End Function
    Public Function Load(id As String) As FilterPreset
        FilterPreset.ValidateId(id)
        If id = "original" Then Return New FilterPreset()
        Dim path = Filename(id)
        If Not File.Exists(path) Then Throw New IOException("The selected filter is missing. Choose Original or another preset in Graphics → Filters.")
        Dim preset = ReadPreset(path)
        If preset.Id <> id Then Throw New IOException("Filter preset ID does not match its file.")
        Return preset
    End Function
    Public Function List(ByRef warnings As String) As List(Of FilterPreset)
        Dim presets As New List(Of FilterPreset) From {New FilterPreset()}, errors As New List(Of String)
        SafeFiles.NoLinks(folder)
        If Directory.Exists(folder) Then
            Dim paths = Directory.GetFiles(folder, "*.d2vrfilter.json")
            If paths.Length > 256 Then Throw New IOException("Too many filter presets. Keep at most 256 in the filter library.")
            For Each path In paths
                Try
                    presets.Add(Load(IO.Path.GetFileName(path).Replace(".d2vrfilter.json", "")))
                Catch ex As IOException
                    errors.Add(IO.Path.GetFileName(path) & ": " & ex.Message)
                End Try
            Next
        End If
        warnings = String.Join(Environment.NewLine, errors)
        Return presets.Take(1).Concat(presets.Skip(1).OrderBy(Function(p) p.Name, StringComparer.CurrentCultureIgnoreCase)).ToList()
    End Function
    Public Sub Save(preset As FilterPreset)
        preset.Validate()
        Dim path = Filename(preset.Id)
        If Not File.Exists(path) AndAlso Directory.Exists(folder) AndAlso Directory.GetFiles(folder, "*.d2vrfilter.json").Length >= 256 Then Throw New IOException("The filter library is full. Delete an unused preset before creating another.")
        SafeFiles.Atomic(path, JsonSerializer.SerializeToUtf8Bytes(preset, Json))
    End Sub
    Public Sub Delete(id As String)
        File.Delete(Filename(id))
    End Sub
    Public Function Import(path As String) As FilterPreset
        Dim preset = ReadPreset(path)
        If preset.Id = "original" Then preset.Name = "Original copy"
        preset.Id = Guid.NewGuid().ToString("N") : Save(preset) : Return preset
    End Function
    Public Shared Sub Export(path As String, preset As FilterPreset)
        preset.Validate()
        SafeFiles.Atomic(path, JsonSerializer.SerializeToUtf8Bytes(preset, Json))
    End Sub
End Class

