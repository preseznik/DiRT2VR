Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports Microsoft.Win32

Public Module Files
    Public ReadOnly JsonOptions As New JsonSerializerOptions With {.WriteIndented = True}
    Public Function Hash(filename As String) As String
        Using stream = File.OpenRead(filename)
            Return Convert.ToHexString(SHA256.HashData(stream))
        End Using
    End Function
    Public Sub AtomicWrite(filename As String, bytes As Byte())
        Directory.CreateDirectory(IO.Path.GetDirectoryName(filename))
        Dim temporary = filename & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            Using stream As New FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                stream.Write(bytes)
                stream.Flush(True)
            End Using
            File.Move(temporary, filename, True)
        Finally
            If File.Exists(temporary) Then File.Delete(temporary)
        End Try
    End Sub
    Public Sub SaveJson(Of T)(filename As String, value As T)
        AtomicWrite(filename, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions))
    End Sub
    Public Function ReadJson(Of T)(filename As String) As T
        Return JsonSerializer.Deserialize(Of T)(File.ReadAllText(filename), JsonOptions)
    End Function
    Public Sub NoLinks(filename As String)
        Dim item = IO.Path.GetFullPath(filename)
        While Not String.IsNullOrEmpty(item)
            If (File.Exists(item) OrElse Directory.Exists(item)) AndAlso (File.GetAttributes(item) And FileAttributes.ReparsePoint) <> 0 Then
                Throw New IOException("Linked paths are not supported for game modifications: " & item)
            End If
            item = IO.Path.GetDirectoryName(item)
        End While
    End Sub
End Module

Public Class InstallContext
    Public Const SupportedHash As String = "49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48"
    Public ReadOnly GameRoot As String
    Public ReadOnly ModRoot As String
    Public ReadOnly UserRoot As String
    Public ReadOnly Id As String
    Public ReadOnly GraphicsPath As String
    Public Sub New(root As String, Optional userBase As String = Nothing, Optional graphics As String = Nothing)
        GameRoot = IO.Path.TrimEndingDirectorySeparator(IO.Path.GetFullPath(root))
        ModRoot = IO.Path.Combine(GameRoot, "DiRT2VR")
        Id = Convert.ToHexString(SHA256.HashData(Text.Encoding.UTF8.GetBytes(GameRoot.ToUpperInvariant()))).Substring(0, 24)
        UserRoot = IO.Path.Combine(If(userBase, IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiRT2VR")), Id)
        GraphicsPath = If(graphics, IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games\DiRT2\hardwaresettings\hardware_settings_config.xml"))
    End Sub
    Public Sub ValidateGame()
        Files.NoLinks(GameRoot)
        Dim executable = IO.Path.Combine(GameRoot, "dirt2_game.exe")
        If Not File.Exists(executable) OrElse Files.Hash(executable) <> SupportedHash Then Throw New IOException("Unsupported DiRT 2 executable. Select the supported version 1.1.0.0 game folder.")
        If Not File.Exists(IO.Path.Combine(GameRoot, "dirt2.exe")) Then Throw New IOException("dirt2.exe is missing.")
    End Sub
    Public Function GameRunning() As Boolean
        ' Conservative across installs: Documents graphics settings are shared.
        Dim processes = Process.GetProcessesByName("dirt2").Concat(Process.GetProcessesByName("dirt2_game")).ToArray()
        Dim active = processes.Length > 0
        For Each item In processes
            item.Dispose()
        Next
        Return active
    End Function
    Public Sub RequireClosed()
        If GameRunning() Then Throw New IOException("Close DiRT 2 before setup, recovery or removal.")
    End Sub
    Public ReadOnly Property PreferencesPath As String
        Get
            Return IO.Path.Combine(UserRoot, "settings.json")
        End Get
    End Property
End Class

Public Class ControllerBinding
    Public Property Action As Integer
    Public Property Source As String = "xinput"
    Public Property Device As String = "0"
    Public Property Label As String = ""
    Public Property Buttons As New List(Of Integer)
    Public Overrides Function ToString() As String
        Return If(Action = 0, "Toggle VR", "Recenter") & " — " & Label & " — " & String.Join(" + ", Buttons.Select(Function(b) ControllerNames.ButtonName(Source, b)))
    End Function
End Class

Public Class VrSettings
    Public Property Version As Integer = 3
    Public Property LoggingEnabled As Boolean = False
    Public Property SkipIntroduction As Boolean = False
    Public Property SkipStartupMovies As Boolean = False
    Public Property Runtime As String = Discovery.RuntimePath()
    Public Property ToggleKey As Integer = 120
    Public Property ToggleModifiers As Integer
    Public Property RecenterKey As Integer = 121
    Public Property RecenterModifiers As Integer
    Public Property Bindings As New List(Of ControllerBinding)
    Public Property BorderlessDesktop As Boolean = False
    Public Property DesktopVSync As Boolean = True
    Public Property RenderScale As Integer = 100
    Public Property HeadsetScale As Integer = 50
    Public Property FieldOfView As Integer = 100
    Public Property HudFollowView As Boolean = False
    Public Property HudDistance As Decimal = 1D
    Public Property HudGauges As Boolean = False
    Public Property HudLapTime As Boolean = True
    Public Property HudPosition As Boolean = True
    Public Property HudMap As Boolean = True
    Public Property HudProgress As Boolean = True
    <Serialization.JsonIgnore>
    Public ReadOnly Property HiddenHudElements As Integer
        Get
            Return If(HudGauges, 0, 1) Or If(HudLapTime, 0, 2) Or If(HudPosition, 0, 4) Or If(HudMap, 0, 8) Or If(HudProgress, 0, 16)
        End Get
    End Property
    Public Property Mirrors As String = "game"
    ' Zero preserves the game's setting; 1..5 match its native quality presets.
    Public Property TreeDetail As Integer = 0
    Public Property ObjectDetail As Integer = 0
    Public Property LaunchMode As String = "menus"
    Public Property TrackId As String = "127"
    Public Property CarCode As String = "sti"
    Public Property Opponents As Integer = 7
    Public Property OpponentCars As String = "same"
    Public Property Laps As Integer = 1
    <Serialization.JsonIgnore>
    Public ReadOnly Property DirectMode As Boolean
        Get
            Return LaunchMode = "practice" OrElse LaunchMode = "race"
        End Get
    End Property
    <Serialization.JsonIgnore>
    Public ReadOnly Property SessionLaps As Integer
        Get
            Return If(DirectMode AndAlso RaceCatalog.Current.Track(TrackId).Circuit, Laps, 1)
        End Get
    End Property
    <Serialization.JsonIgnore>
    Public ReadOnly Property GridOpponents As Integer
        Get
            Return If(LaunchMode = "race", Opponents, 0)
        End Get
    End Property
    <Serialization.JsonIgnore>
    Public ReadOnly Property RenderWidth As Integer
        Get
            Return CInt(Math.Round(1600.0 * RenderScale * FieldOfView / 10000))
        End Get
    End Property
    <Serialization.JsonIgnore>
    Public ReadOnly Property RenderHeight As Integer
        Get
            Return CInt(Math.Round(1200.0 * RenderScale * FieldOfView / 10000))
        End Get
    End Property
    Public Sub Validate()
        HudDistance = Math.Round(Math.Clamp(HudDistance, 1D, 20D) * 2D, MidpointRounding.AwayFromZero) / 2D
        If Version <> 3 Then Throw New IOException("Unsupported settings version.")
        If Not {"menus", "practice", "race", "lan"}.Contains(LaunchMode) Then Throw New IOException("Unknown launch mode.")
        If Opponents < 1 OrElse Opponents > 7 Then Throw New IOException("Choose between one and seven race opponents.")
        If Not {"same", "mixed", "class"}.Contains(OpponentCars) Then Throw New IOException("Unknown opponent car selection.")
        If Laps < 1 OrElse Laps > 20 Then Throw New IOException("Choose between one and twenty laps.")
        RaceCatalog.Current.Track(TrackId) : RaceCatalog.Current.Car(CarCode)
        If RenderScale < 50 OrElse RenderScale > 150 OrElse HeadsetScale < 25 OrElse HeadsetScale > 100 OrElse FieldOfView < 70 OrElse FieldOfView > 100 OrElse Not {"game", "on", "off"}.Contains(Mirrors) Then Throw New IOException("Invalid VR graphics settings.")
        If TreeDetail < 0 OrElse TreeDetail > 5 OrElse ObjectDetail < 0 OrElse ObjectDetail > 5 Then Throw New IOException("Invalid scenery detail settings.")
        If Not ValidKey(ToggleKey) OrElse Not ValidKey(RecenterKey) OrElse ToggleModifiers < 0 OrElse ToggleModifiers > 7 OrElse RecenterModifiers < 0 OrElse RecenterModifiers > 7 Then Throw New IOException("Choose valid keyboard shortcuts.")
        If ToggleKey = RecenterKey AndAlso ToggleModifiers = RecenterModifiers Then Throw New IOException("Toggle VR and recenter must have different shortcuts.")
        If Bindings Is Nothing OrElse Bindings.Count > 32 Then Throw New IOException("Invalid controller bindings.")
        For i = 0 To Bindings.Count - 1
            Dim a = Bindings(i)
            If a Is Nothing OrElse a.Action < 0 OrElse a.Action > 1 OrElse Not {"xinput", "hid", "dinput"}.Contains(a.Source) OrElse String.IsNullOrEmpty(a.Device) OrElse a.Buttons Is Nothing OrElse a.Buttons.Count < 1 OrElse a.Buttons.Count > 2 OrElse a.Buttons.Distinct().Count() <> a.Buttons.Count Then Throw New IOException("Invalid controller binding.")
            If a.Source = "xinput" AndAlso (Not {"0", "1", "2", "3"}.Contains(a.Device) OrElse a.Buttons.Any(Function(b) Not ControllerNames.XButtons.Contains(b))) Then Throw New IOException("Invalid Xbox binding.")
            If a.Source = "hid" AndAlso a.Buttons.Any(Function(b) b < 1 OrElse b > 65535) Then Throw New IOException("Invalid HID button.")
            Dim deviceGuid As Guid
            If a.Source = "dinput" AndAlso (Not Guid.TryParse(a.Device, deviceGuid) OrElse a.Buttons.Any(Function(b) b < 1 OrElse b > 128)) Then Throw New IOException("Invalid wheel button binding.")
            For j = 0 To i - 1
                Dim b = Bindings(j)
                If a.Source = b.Source AndAlso a.Device = b.Device AndAlso (a.Buttons.All(Function(k) b.Buttons.Contains(k)) OrElse b.Buttons.All(Function(k) a.Buttons.Contains(k))) Then Throw New IOException("Controller assignments overlap. Choose distinct buttons or pairs.")
            Next
        Next
    End Sub
    Public Shared Function ValidKey(key As Integer) As Boolean
        Return (key >= 48 AndAlso key <= 90) OrElse (key >= 96 AndAlso key <= 135) OrElse {8, 9, 13, 19, 32, 33, 34, 35, 36, 37, 38, 39, 40, 45, 46}.Contains(key)
    End Function
    Public Shared Function Load(context As InstallContext) As VrSettings
        Dim settings = If(File.Exists(context.PreferencesPath), Files.ReadJson(Of VrSettings)(context.PreferencesPath), New VrSettings())
        If settings Is Nothing Then Throw New IOException("Settings are empty.")
        If settings.Version = 1 OrElse settings.Version = 2 Then settings.Version = 3 ' New fields retain defaults; existing controls/graphics are preserved.
        settings.Validate()
        Return settings
    End Function
End Class

Public Module Discovery
    Public Function SteamLibraries() As List(Of String)
        Dim roots As New List(Of String)
        Using key = Registry.CurrentUser.OpenSubKey("Software\Valve\Steam")
            Dim steam = TryCast(key?.GetValue("SteamPath"), String)
            If Not String.IsNullOrEmpty(steam) Then roots.Add(steam.Replace("/", "\"))
        End Using
        roots.Add(IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"))
        For Each root In roots.ToArray()
            Dim vdf = IO.Path.Combine(root, "steamapps\libraryfolders.vdf")
            If File.Exists(vdf) Then
                For Each match As Match In Regex.Matches(File.ReadAllText(vdf), """path""\s+""([^""]+)""")
                    roots.Add(match.Groups(1).Value.Replace("\\", "\"))
                Next
            End If
        Next
        Return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    End Function
    Public Function RuntimePath() As String
        For Each root In SteamLibraries()
            Dim candidate = IO.Path.Combine(root, "steamapps\common\SteamVR\steamxr_win32.json")
            If File.Exists(candidate) Then Return candidate
        Next
        Return ""
    End Function
    Public Function GamePath() As String
        If File.Exists(IO.Path.Combine(AppContext.BaseDirectory, "dirt2_game.exe")) Then Return AppContext.BaseDirectory
        For Each root In SteamLibraries()
            Dim candidate = IO.Path.Combine(root, "steamapps\common\Dirt 2")
            If File.Exists(IO.Path.Combine(candidate, "dirt2_game.exe")) Then Return candidate
        Next
        Return AppContext.BaseDirectory
    End Function
End Module
