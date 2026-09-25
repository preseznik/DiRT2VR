Imports System.Drawing
Imports System.Windows.Forms

Public Class InstructionsView
    Inherits UserControl
    Private ReadOnly topics As New ListBox With {.BorderStyle = BorderStyle.None, .Name = "InstructionTopics"}
    Private ReadOnly picker As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Name = "InstructionTopicPicker", .AccessibleName = "Instruction topic"}
    Private ReadOnly article As New RichTextBox With {.ReadOnly = True, .BorderStyle = BorderStyle.None, .WordWrap = True, .ScrollBars = RichTextBoxScrollBars.Vertical, .DetectUrls = False, .Name = "InstructionArticle", .AccessibleName = "Instructions"}
    Private updating As Boolean
    Private Shared ReadOnly Titles As String() = {"Getting started", "Desktop graphics", "VR rendering", "VR HUD", "Controls", "Multiplayer", "Recovery & updates"}
    Private Shared ReadOnly Pages As String() = {
        "Launch
Launch plays on your monitor. Launch VR uses SteamVR: start SteamVR and connect your headset first. VR enters races in cockpit view automatically; menus and pause screens use the virtual screen.

Direct practice and Race
Practice is solo; Race adds 1–7 AI opponents. Mixed opponents draw from all installed classes; Same class uses your car's game-defined class. Laps apply to circuits; point-to-point stages are one run. Custom events do not award career progress.

Finishing
Restart repeats the event. Return to menus closes the game and reopens Normal Launch in the same Desktop or VR mode. Alt+F4 quits without reopening.

Saving settings
Save settings, then launch a new session. Quit the game normally so temporary files are restored. The background manager keeps running if you close the launcher window.

Startup options
Skip startup logo movies affects single-player launches. LAN ignores it to preserve the game's content checks. Skip introduction applies to LAN's opening movie/tutorial; it keeps profile creation and does not undo saved career progress.",
        "Borderless fullscreen
Fills the primary monitor at its current desktop resolution. It applies to desktop Normal Launch, Practice, Race and HOST/JOIN. Windows resolution and refresh rate stay unchanged. A higher resolution may cost performance.

VSync
On by default for desktop play, independently of borderless. Turn it off to allow uncapped rendering. The preference is temporary and the original game setting is restored after play. VR keeps its own timing and VSync setup.

Frame rate
Driver settings, Windows and your monitor can affect frame rate and presentation. Borderless uses the game's windowed rendering path; it does not guarantee a particular frame rate.

First launch and recovery
If the game has not created a graphics file yet, use Normal Launch once before applying display overrides. If Windows rejects a borderless window change, the launcher reports a warning and keeps ordinary windowed play available. Alt+Tab and minimization remain available.",
        "Render resolution
100% renders 1600 × 1200 per eye at full field of view. The 50–150% slider scales width and height. Lower values reduce pixel work and detail; 80% uses about 64% of the pixels.

Headset texture scale
25–100% of SteamVR's recommended width and height. Increasing this alone cannot add detail missing from the scene render. Default: 50%.

Field of view
100% retains the full view. Lower values crop peripheral vision and reduce render resolution proportionally, without stretching the image. Nondefault crops still need broader headset testing.

Mirrors and scenery
Car mirrors may be forced on or off. Tree detail and Object detail use the game's presets; Game preserves your settings. Higher levels keep detailed models farther away, at a performance cost. Track-specific draw-distance limits remain.

Headset refresh rate
Set refresh rate in SteamVR or your headset connection software before launching. The launcher can show the last reported rate, not a live measurement. Desktop VSync does not select headset Hz.

Rendering baseline
VR keeps the current reduced-effects configuration for crowds, particles, shadows and motion blur. Temporary graphics changes are restored after play.",
        "Distance
Choose 1–20 metres in 0.5 m steps. This changes stereo depth while keeping text at the same apparent size. Default: 1 m.

Follow view
Off keeps the HUD fixed relative to the car. On keeps it in front of your head. Recenter resets the seated reference used by the fixed HUD.

Show
Choose gauges (speedometer, gear and revs), lap/time, race position, route map and stage progress. Gauges default off; the other areas default on. These are masks over the standard race HUD, so overlapping elements in the same area are also hidden.

Scope
These options affect the cockpit HUD, not the centre of the view, desktop HUD, menus or virtual screen. Save and relaunch VR to apply. Alternative HUD layouts and nondefault distances still need broader headset testing.",
        "VR shortcuts
Click a keyboard binding, then press a key with optional Ctrl, Alt or Shift. Bind assigns one controller button or a two-button combination on the same device. Release the buttons to finish. Escape cancels. Multiple devices may be assigned.

Button conflicts
Controller buttons still reach DiRT 2. Avoid combinations that also trigger driving or menu actions. Disconnected assignments are kept; Xbox slot changes may require selecting the controller again.

Driving controls
Direct practice and Race load the existing profile's controls. Configure driving controls opens the optional editor and binding wizard for steering, pedals, clutch, handbrake and gears. Unassigned actions use the game's saved controls; an assigned action replaces its saved bindings.

Analogue capture
Leave axes at rest before starting, then move the requested axis deliberately. Small movements are ignored. Follow the wizard prompts for steering directions and pedals.

Applying changes
Save settings and start a new session. Resizing the launcher keeps unsaved assignments and an active capture intact.",
        "HOST
Choose Desktop or VR, then create a lobby in the game's Multiplayer / LAN menu. The launcher lists this PC while its HOST game runs; that status does not confirm an open lobby or a player count.

JOIN
Select an available host and choose Desktop or VR. Finish joining in the game's Multiplayer / LAN menu. Automatic lobby entry is not available yet.

Network and career
Both PCs need the same local network. Allow DiRT 2 on the Windows Private network if prompted. LAN uses your normal career and graphics settings; no separate save or import is needed.

Startup movies
LAN ignores Skip startup logo movies to preserve the original multiplayer content checks. The separate Skip introduction option can bypass the opening tutorial while preserving profile setup.

Compatibility
Desktop two-PC racing through results has been confirmed. Broader multiplayer VR and wheel hardware coverage still need testing.",
        "Restore original files
Close the game, then choose Restore original files. Interrupted sessions are recovered before the next managed launch. Unexpected file edits are preserved and reported as conflicts.

Diagnostic logging
Off by default. Enable it in Settings only for troubleshooting and turn it off afterward. Open logs shows the log folder. Existing logs are not deleted automatically; recovery records remain available even when logging is off.

Updates
About shows the installed version, build and GitHub update controls. Close the game before updating. Downloads are verified before setup opens; settings are retained. Windows may ask for administrator approval. ZIP installs become installer-managed when updated through setup.

Documentation
The installed README contains setup, controls, recovery and current limitations. GitHub Releases contains published installers and ZIP packages."}
    Public Sub New()
        Dock = DockStyle.Fill
        Controls.AddRange({topics, picker, article})
        topics.Items.AddRange(Titles) : picker.Items.AddRange(Titles)
        AddHandler topics.SelectedIndexChanged, Sub() SelectPage(topics.SelectedIndex)
        AddHandler picker.SelectedIndexChanged, Sub() SelectPage(picker.SelectedIndex)
        SelectPage(0)
    End Sub
    Public Sub ShowTopic(title As String)
        SelectPage(Math.Max(0, Array.IndexOf(Titles, title)))
    End Sub
    Private Sub SelectPage(index As Integer)
        If updating OrElse index < 0 Then Return
        updating = True
        Try
            topics.SelectedIndex = index : picker.SelectedIndex = index
            article.Text = Titles(index) & vbLf & vbLf & Pages(index).Replace(vbCrLf, vbLf)
            article.SelectAll() : article.SelectionFont = Font : article.SelectionColor = ForeColor
            Dim offset = 0
            For Each paragraph In article.Text.Split({vbLf & vbLf}, StringSplitOptions.None)
                Dim firstLine = paragraph.Split(vbLf)(0).TrimEnd(ChrW(13))
                article.Select(offset, firstLine.Length)
                Using heading As New Font(Font, FontStyle.Bold)
                    article.SelectionFont = heading
                End Using
                offset += paragraph.Length + 2
            Next
            article.Select(0, 0) : article.ScrollToCaret()
        Finally
            updating = False
        End Try
    End Sub
    Protected Overrides Sub OnBackColorChanged(e As EventArgs)
        MyBase.OnBackColorChanged(e)
        If article Is Nothing Then Return
        For Each control As Control In New Control() {topics, picker, article}
            control.BackColor = BackColor : control.ForeColor = ForeColor
        Next
    End Sub
    Protected Overrides Sub OnForeColorChanged(e As EventArgs)
        MyBase.OnForeColorChanged(e)
        If article Is Nothing Then Return
        For Each control As Control In New Control() {topics, picker, article}
            control.ForeColor = ForeColor
        Next
    End Sub
    Protected Overrides Sub OnLayout(e As LayoutEventArgs)
        MyBase.OnLayout(e)
        If article Is Nothing Then Return
        Dim narrow = Width < Px(Me, 720), gap = Px(Me, 20)
        topics.Visible = Not narrow : picker.Visible = narrow
        If narrow Then
            picker.SetBounds(0, 0, Width, picker.PreferredHeight)
            article.SetBounds(0, picker.Bottom + gap, Width, Math.Max(1, Height - picker.Bottom - gap))
        Else
            topics.SetBounds(0, 0, Px(Me, 185), Height)
            article.SetBounds(topics.Right + gap, 0, Math.Max(1, Width - topics.Right - gap), Height)
        End If
    End Sub
End Class
