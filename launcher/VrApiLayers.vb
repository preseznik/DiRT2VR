Imports Microsoft.Win32
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.RegularExpressions

Public Class VrLayerBypass
    Public Property LayerName As String = ""
    Public Property ManifestPath As String = ""
    Public Property ManifestHash As String = ""
    Public Property LibraryPath As String = ""
    Public Property DisableVariable As String = ""

    Public ReadOnly Property DisplayName As String
        Get
            Select Case LayerName
                Case "XR_APILAYER_reshade" : Return "ReShade"
                Case "XR_APILAYER_NOVENDOR_OBSMirror" : Return "OpenXR OBS Mirror"
                Case Else : Return LayerName
            End Select
        End Get
    End Property
    Public ReadOnly Property Consequence As String
        Get
            Select Case LayerName
                Case "XR_APILAYER_reshade" : Return "ReShade OpenXR effects will be unavailable."
                Case "XR_APILAYER_NOVENDOR_OBSMirror" : Return "OBS Mirror's OpenXR capture will be unavailable."
                Case Else : Return "Features provided by this OpenXR layer will be unavailable."
            End Select
        End Get
    End Property
    Public Function SameIdentity(other As VrLayerBypass) As Boolean
        Return other IsNot Nothing AndAlso LayerName = other.LayerName AndAlso
            String.Equals(ManifestPath, other.ManifestPath, StringComparison.OrdinalIgnoreCase) AndAlso
            ManifestHash = other.ManifestHash AndAlso
            String.Equals(LibraryPath, other.LibraryPath, StringComparison.OrdinalIgnoreCase) AndAlso
            DisableVariable = other.DisableVariable
    End Function
    Public Overrides Function ToString() As String
        Return DisplayName
    End Function
End Class

Public Class VrLayerFailure
    Public Property LayerName As String
    Public Property LibraryPath As String
    Public Property WindowsError As Integer
End Class

Public Module VrApiLayers
    Private Const RegistryPath As String = "SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit"
    Public Function SafeDisableVariable(value As String) As Boolean
        ' Never let a manifest turn a bypass into PATH, runtime, or other process configuration.
        Return value IsNot Nothing AndAlso Regex.IsMatch(value, "\ADISABLE_(?:XR_|OPENXR_)[A-Za-z0-9_]{1,180}\z")
    End Function
    Public Function ReadManifest(path As String) As VrLayerBypass
        Try
            If Not IO.Path.IsPathFullyQualified(path) Then Return Nothing
            Using stream = File.OpenRead(path)
                If stream.Length > 65536 Then Return Nothing
                Dim bytes(CInt(stream.Length) - 1) As Byte
                stream.ReadExactly(bytes)
                Using document = JsonDocument.Parse(bytes)
                    Dim layer = document.RootElement.GetProperty("api_layer")
                    Dim name = layer.GetProperty("name").GetString()
                    Dim library = layer.GetProperty("library_path").GetString()
                    Dim variable = layer.GetProperty("disable_environment").GetString()
                    If name Is Nothing OrElse Not Regex.IsMatch(name, "\AXR_APILAYER_[A-Za-z0-9_]{1,200}\z") OrElse
                        String.IsNullOrWhiteSpace(library) OrElse Not SafeDisableVariable(variable) Then Return Nothing
                    Return New VrLayerBypass With {.LayerName = name, .ManifestPath = IO.Path.GetFullPath(path),
                        .ManifestHash = Convert.ToHexString(SHA256.HashData(bytes)),
                        .LibraryPath = IO.Path.GetFullPath(IO.Path.Combine(IO.Path.GetDirectoryName(path), library)), .DisableVariable = variable}
                End Using
            End Using
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse
            TypeOf ex Is Security.SecurityException OrElse TypeOf ex Is JsonException OrElse TypeOf ex Is ArgumentException OrElse
            TypeOf ex Is KeyNotFoundException OrElse TypeOf ex Is InvalidOperationException OrElse TypeOf ex Is NotSupportedException
            Return Nothing
        End Try
    End Function
    Public Function Discover() As List(Of VrLayerBypass)
        Dim paths As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each hive In {RegistryHive.LocalMachine, RegistryHive.CurrentUser}
            Try
                ' The launcher is x64; the probe and game use the 32-bit view of both hives.
                Using root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32), key = root.OpenSubKey(RegistryPath)
                    If key Is Nothing Then Continue For
                    For Each path In key.GetValueNames()
                        If key.GetValueKind(path) = RegistryValueKind.DWord AndAlso CInt(key.GetValue(path)) = 0 Then paths.Add(path)
                    Next
                End Using
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.SecurityException
                ' An incomplete inventory must not authorize any bypass.
                Return New List(Of VrLayerBypass)()
            End Try
        Next
        Return paths.Select(Function(path) ReadManifest(path)).Where(Function(layer) layer IsNot Nothing).ToList()
    End Function
    Public Function Failures(exitCode As Integer, report As String) As List(Of VrLayerFailure)
        Dim result As New List(Of VrLayerFailure)()
        If exitCode = 0 OrElse Not Regex.IsMatch(report, "(?m)^xrCreateInstance=-32\r?$") OrElse
            Not report.Contains("Failed loading layer information", StringComparison.Ordinal) Then Return result
        ' Loader text is from the pinned SDK. Match numeric Windows errors, not localized descriptions.
        For Each match As Match In Regex.Matches(report, "ApiLayerInterface::LoadApiLayers skipping layer (?<name>XR_APILAYER_[A-Za-z0-9_]+), failed to load with message ""Failed to open dynamic library (?<path>[^\r\n]+?) with error (?<error>[0-9]+):")
            Dim code As Integer
            If Not Integer.TryParse(match.Groups("error").Value, code) Then Continue For
            Dim path As String
            Try
                If Not IO.Path.IsPathFullyQualified(match.Groups("path").Value) Then Continue For
                path = IO.Path.GetFullPath(match.Groups("path").Value)
            Catch ex As ArgumentException
                Continue For
            End Try
            If result.Any(Function(item) item.LayerName = match.Groups("name").Value AndAlso String.Equals(item.LibraryPath, path, StringComparison.OrdinalIgnoreCase)) Then Continue For
            result.Add(New VrLayerFailure With {.LayerName = match.Groups("name").Value, .LibraryPath = path, .WindowsError = code})
        Next
        Return result
    End Function
    Public Function Resolve(failures As IReadOnlyList(Of VrLayerFailure), catalog As IReadOnlyList(Of VrLayerBypass)) As List(Of VrLayerBypass)
        Dim result As New List(Of VrLayerBypass)()
        If failures.Count = 0 OrElse failures.Count > 16 Then Return result
        For Each failure In failures
            Dim matches = catalog.Where(Function(layer) layer.LayerName = failure.LayerName).ToArray()
            If matches.Length <> 1 OrElse Not String.Equals(matches(0).LibraryPath, failure.LibraryPath, StringComparison.OrdinalIgnoreCase) OrElse
                Not SafeDisableVariable(matches(0).DisableVariable) Then Return New List(Of VrLayerBypass)()
            ' A shared switch would also disable a layer not named by the failure.
            If catalog.Any(Function(layer) String.Equals(layer.DisableVariable, matches(0).DisableVariable, StringComparison.OrdinalIgnoreCase) AndAlso
                layer.LayerName <> matches(0).LayerName) Then Return New List(Of VrLayerBypass)()
            result.Add(matches(0))
        Next
        Return result
    End Function
    Public Function Revalidate(saved As IEnumerable(Of VrLayerBypass), catalog As IReadOnlyList(Of VrLayerBypass)) As List(Of VrLayerBypass)
        Dim result As New List(Of VrLayerBypass)()
        For Each entry In saved.Where(Function(item) item IsNot Nothing)
            Dim resolved = Resolve({New VrLayerFailure With {.LayerName = entry.LayerName, .LibraryPath = entry.LibraryPath}}, catalog)
            If resolved.Count = 1 AndAlso resolved(0).SameIdentity(entry) AndAlso Not result.Any(Function(item) item.SameIdentity(entry)) Then result.Add(resolved(0))
        Next
        Return result
    End Function
    Public Sub Configure(start As ProcessStartInfo, bypasses As IEnumerable(Of VrLayerBypass))
        For Each layer In bypasses
            If Not SafeDisableVariable(layer.DisableVariable) Then Throw New IOException("Invalid OpenXR layer disable switch.")
            start.Environment(layer.DisableVariable) = "1"
        Next
    End Sub
End Module

Public Class VrLayerPreferences
    Public Property Version As Integer = 1
    Public Property Bypasses As New List(Of VrLayerBypass)()
    Public Shared Function Load(context As InstallContext) As VrLayerPreferences
        Try
            Dim path = IO.Path.Combine(context.UserRoot, "vr-layer-bypasses.json")
            If File.Exists(path) Then
                Dim saved = Files.ReadJson(Of VrLayerPreferences)(path)
                If saved IsNot Nothing AndAlso saved.Version = 1 AndAlso saved.Bypasses IsNot Nothing Then
                    saved.Bypasses = saved.Bypasses.Where(Function(item) item IsNot Nothing).Take(64).ToList()
                    Return saved
                End If
            End If
        Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is JsonException
            ' Invalid preferences grant no permission to bypass a layer.
        End Try
        Return New VrLayerPreferences()
    End Function
    Public Sub Save(context As InstallContext)
        Dim path = IO.Path.Combine(context.UserRoot, "vr-layer-bypasses.json")
        Files.NoLinks(path)
        Files.SaveJson(path, Me)
    End Sub
    Public Shared Sub Remember(context As InstallContext, layers As IEnumerable(Of VrLayerBypass))
        Dim saved = Load(context)
        For Each layer In layers
            saved.Bypasses.RemoveAll(Function(item) item.LayerName = layer.LayerName)
            saved.Bypasses.Add(layer)
        Next
        saved.Save(context)
    End Sub
    Public Shared Sub Reenable(context As InstallContext, layerName As String)
        Dim saved = Load(context)
        saved.Bypasses.RemoveAll(Function(item) item.LayerName = layerName)
        saved.Save(context)
    End Sub
End Class
