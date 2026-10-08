Imports System.Drawing
Imports System.Windows.Forms

Public Class InstructionsView
    Inherits UserControl
    Private ReadOnly topics As New ListBox With {.BorderStyle = BorderStyle.None, .Name = "InstructionTopics"}
    Private ReadOnly picker As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Name = "InstructionTopicPicker", .AccessibleName = "Instruction topic"}
    Private ReadOnly article As New RichTextBox With {.ReadOnly = True, .BorderStyle = BorderStyle.None, .WordWrap = True, .ScrollBars = RichTextBoxScrollBars.Vertical, .DetectUrls = False, .Name = "InstructionArticle", .AccessibleName = "Instructions"}
    Private updating As Boolean
    Private Shared ReadOnly Titles As String() = {"Getting started", "Desktop graphics", "VR rendering", "VR HUD", "Controls", "Multiplayer", "Recovery & updates", "Advanced", "VR seat position", "Custom tracks", "Appearance", "Profiles"}
    Private Shared ReadOnly Pages As String() = {
        "Launch
Launch plays on your monitor. Launch VR uses SteamVR: start SteamVR and connect your headset first. VR enters races in cockpit view automatically; menus and pause screens use the virtual screen.

Direct practice and Race
Practice is solo; Race adds 1–7 AI opponents. Mixed opponents draw from all installed classes; Same class uses your car's game-defined class. Laps apply to circuits; point-to-point stages are one run. Custom events do not award career progress.

Finishing
Use Up/Down to select choices in the pause and finish menus. Restart repeats the event. Return to menus closes the game and reopens Normal Launch in the same Desktop or VR mode. Alt+F4 quits without reopening.

Saving settings
Save settings, then launch a new session. Quit the game normally so temporary files are restored. The background manager keeps running if you close the launcher window.

Startup options
Skip startup logo movies affects single-player launches. LAN ignores it to preserve the game's content checks. Skip introduction applies to LAN's opening movie/tutorial; it keeps profile creation and does not undo saved career progress.",
        "Borderless fullscreen
Fills the primary monitor at its current desktop resolution. It applies to desktop Normal Launch, Practice, Race and HOST/JOIN, not Launch VR. The VR mirror remains windowed. Windows resolution and refresh rate stay unchanged. A higher resolution may cost performance.

VSync
On by default for desktop play, independently of borderless. Turn it off to allow uncapped rendering. The preference is temporary and the original game setting is restored after play. VR keeps its own timing and VSync setup.

Bloom
Adds a soft glow around bright areas. On keeps the game's existing effects; Off removes the glow without disabling colour grading, exposure adjustment or shadows. Desktop and VR have separate choices, both On by default. Save and relaunch to apply. Works with Normal Launch, Practice, Race, custom tracks and HOST/JOIN. Original effect files are restored after play; use Restore original files if recovery is interrupted. Separate custom-track lighting choices are retained.

Filters
Graphics opens on Main; Filters contains Desktop and VR preset choices. Original keeps the track's look. Choose New or Save as, name the preset, then edit its Day and Night adjustments. Selecting a preset in the editor, creating one or importing one also selects it for both Desktop and VR. You can choose different presets with the two launch selectors afterward. Save settings and relaunch to apply; there is no live preview.

100% preserves each track's value; brightness offset 0 and tint Off preserve their original values. Reset adjustments resets the displayed Day or Night variant. Exposure controls scale native game values; their visual response still needs comparison testing. VR motion blur stays disabled. Main's Bloom Off overrides preset glow settings. Differing presets in LAN and headset appearance still need testing; use Original if problems occur.

Import and Export use DiRT2VR preset JSON, not game XML or Assetto Corsa ppfilters. Presets are shared by this Windows account. Rename and Delete manage custom presets; select and save another preset before deleting one used for launching. Original cannot be changed. Changing the edited preset prompts you to save or discard unsaved adjustments.

Frame rate
Driver settings, Windows and your monitor can affect frame rate and presentation. Borderless uses the game's windowed rendering path; it does not guarantee a particular frame rate.

First launch and recovery
If the game has not created a graphics file yet, use Normal Launch once before applying display overrides. If Windows rejects a borderless window change, the launcher reports a warning and keeps ordinary windowed play available. Alt+Tab and minimization remain available.",
        "Render resolution
100% requests 1600 × 1200 per eye at full field of view. The 50–300% slider scales width and height. A green notch marks the recommended 150%; the default remains 100%. 300% requests 4800 × 3600 per eye, nine times the baseline pixels, and needs substantially more memory and GPU time if applied. Graphics shows the requested scene size and the last VR launch's measured game and headset dimensions. A mismatch means the game did not retain the requested size; the apparent mirror window size alone is not proof. Enable diagnostic logging before a troubleshooting run to record startup size changes.

Headset texture scale
This is the size of the finished picture sent to your headset, not the quality of car or road textures. Render resolution controls how much detail the game draws first; Headset texture scale controls how much of that detail survives in the final picture. A low value can make even a high-resolution scene look soft.

100% uses the picture size SteamVR recommends. 50% halves both width and height, leaving one quarter as many pixels. For example, if SteamVR recommends 3000 × 3000, 50% sends 1500 × 1500. Lower values use less memory and may help performance; higher values can retain more detail but cannot create detail missing from the game render. Default: 100%. Existing saved values are kept; Reset graphics defaults selects 100%. This changes neither headset refresh rate nor the quality of the game's original textures. Change one slider at a time when comparing clarity. The floating HUD uses a separate image and is not scaled by this slider.

Anti-aliasing (MSAA)
VR uses its own Off / 2× / 4× / 8× setting. Default: 2×. Higher values smooth edges but cost memory and GPU time; 4× and especially 8× may make VR unstable in this 32-bit game. Reduce MSAA first if VR crashes. Your desktop MSAA is restored after play.

Field of view
100% retains the full view. Lower values crop peripheral vision and reduce render resolution proportionally, without stretching the image. Nondefault crops still need broader headset testing.

Mirrors and scenery
Car mirrors may be forced on or off. Tree detail and Object detail use the game's presets; Game preserves your settings. Higher levels keep detailed models farther away, at a performance cost. VR also extends short scenery ranges and keeps nearby cars detailed when you look sideways or behind. Distant cars retain normal detail reduction. VR also draws grass farther away and delays tree/bush detail changes. Trackside props such as tire stacks and flags also stay detailed farther away. This experimental change may cost performance. Some authored visibility limits remain; compare performance on the same section of track when increasing detail.

Headset refresh rate
Set refresh rate in SteamVR or your headset connection software before launching. The launcher can show the last reported rate, not a live measurement. Desktop VSync does not select headset Hz.

Shadows (experimental)
Off retains the current shadow-free VR rendering and performance. On enables shadows using the game's saved shadow quality. Includes corrected VR exposure metering. Shadows can brighten sunlit areas through the game's normal exposure adjustment. This remains experimental; turn it off if the appearance or performance is unsuitable. Shadows may also reduce performance. Save and relaunch to apply. Desktop play is unchanged.

Bloom
Adds a soft glow around bright areas. On keeps the game's existing effects; Off removes the glow in VR, including its flat virtual screen. This is separate from shadows and does not disable exposure adjustment or colour grading. Default: On. Save and relaunch VR to apply; the Desktop choice is independent. Works with all launch modes and custom tracks, retaining their separate lighting choices. Original effect files are restored after play.

Rendering baseline
VR keeps the current reduced-effects configuration for crowds, particles and motion blur. Temporary graphics changes, including shadows, are restored after play.",
        "Distance
Choose 1–20 metres in 0.5 m steps. This changes stereo depth while keeping text at the same apparent size. Default: 1 m.

Follow view
Off keeps the HUD fixed relative to the car. On keeps it in front of your head. Recenter resets your seated position and horizontal direction, without baking head tilt into the view. It keeps your saved seat adjustment and places the fixed HUD ahead. When looking almost straight up or down, it keeps the previous horizontal direction.

Show
Choose gauges (speedometer, gear and revs), lap/time, race position, route map and stage progress. Gauges default off; the other areas default on. These are masks over the standard race HUD, so overlapping elements in the same area are also hidden.

Scope
These options affect the cockpit HUD, not the centre of the view, desktop HUD, menus or virtual screen. Save and relaunch VR to apply. Alternative HUD layouts and nondefault distances still need broader headset testing.",
        "VR shortcuts
Click a keyboard binding, then press a key with optional Ctrl, Alt or Shift. In Modern, click a controller cell to replace it, + to add another, or × to remove it. In Classic, use Bind and Remove selected. Assign one button or a two-button combination on the same device. Release the buttons to finish. Escape cancels. Multiple devices may be assigned.

Button conflicts
Controller buttons still reach DiRT 2. Avoid combinations that also trigger driving or menu actions. Disconnected assignments are kept; Xbox slot changes may require selecting the controller again.

Driving controls
Direct practice and Race load the existing profile's controls. Already-signed-in accounts continue straight to the event; otherwise, sign in when GFWL asks. Canceling sign-in or a career-load error ends the launch without creating a replacement save. Live device input shows axes, pressed buttons and POV hats for the selected device without changing bindings. Refresh after connecting a device. These are raw readings before calibration; resting pedals can read at either end of an axis. Configure driving controls opens the optional editor for driving and menu actions. Bind Pause and Menu Start Button to Start; Menu Select confirms, Menu Back cancels and Menu Up/Down/Left/Right navigate. Xbox preset includes these assignments. Unassigned actions use the game's saved controls; an assigned action replaces its saved bindings. Keyboard capture accepts Escape; use Cancel to leave without assigning it.

Cockpit animation (experimental)
Remove artificial steering corrections targets extra wheel/hand twitch in desktop and VR cockpit views. Off by default. Desktop requires DX11; select cockpit view yourself. Keeps the original animation range and steering filter without changing handling or force feedback. It does not match 540 or 900 degrees. Desktop testing and the PC3080 VR check confirmed that wheel judder is removed. The previously reported PC1060 VR launch crash is marked resolved after the user could no longer reproduce it in 0.17.22. Missing hands or a static wheel on other setups remain under investigation. Save and relaunch to apply.

Steering comparison
Use the same car/event for three runs: main option off; main option on with Observe only checked; then Observe only unchecked. Observe only keeps the original animation values while running the diagnostic hooks. Enable Settings → diagnostic logging to record the last two runs. Hold centre, turn slowly left/right, then drive over rough ground. Save and relaunch between runs. Diagnostics are bounded and do not enable logging by themselves.

Deadzone resets
Enabled launcher assignments and calibration reapply on every launch. The editor shows dead zone and saturation beside each controller assignment. To retain zero steering deadzone with launcher bindings, set Calibration to 0% for both Steer Left and Steer Right, then Save driving controls. Alternatively, disable launcher bindings and use the game's saved controls. If a reset persists with overrides disabled, report the wheel model, launch mode and whether the game's Save Profile action retains the setting across two launches.

Analogue capture
Leave axes at rest before starting, then move the requested axis deliberately. Small movements are ignored. Follow the wizard prompts for steering directions and pedals.

Applying changes
Save settings and start a new session. Resizing the launcher keeps unsaved assignments and an active capture intact.",
        "HOST
Choose Desktop or VR, then create a lobby in the game's Multiplayer / LAN menu. The launcher lists this PC while its HOST game runs; that status does not confirm an open lobby or a player count.

JOIN
Select an available host and choose Desktop or VR. Finish joining in the game's Multiplayer / LAN menu. Automatic lobby entry is not available yet.

Network and career
Both PCs need the same local network. Allow DiRT 2 on the Windows Private network if prompted. LAN uses the career selected on Profile. Current game career uses your normal save; no import is needed. Graphics settings stay shared.

Startup movies
LAN ignores Skip startup logo movies to preserve the original multiplayer content checks. The separate Skip introduction option can bypass the opening tutorial while preserving profile setup.

Compatibility
Desktop two-PC racing through results has been confirmed. Broader multiplayer VR and wheel hardware coverage still need testing.",
        "Restore original files
Close the game, then choose Restore original files. Interrupted sessions are recovered before the next managed launch. Unexpected file edits are preserved and reported as conflicts.

Diagnostic logging
Off by default. Enable it in Settings only for troubleshooting and turn it off afterward. Open logs shows the log folder. Existing logs are not deleted automatically; recovery records remain available even when logging is off.

Updates
About shows the installed version, Stable/Experimental channel, build and GitHub update controls. Include experimental releases is off by default and saved immediately; it controls both startup notices and manual checks. Enabling it includes newer experimental and normal releases. Disabling it does not replace the installed build. Experimental builds offer Return to stable…: this asks before installing the latest normal release, even if older, and disables experimental updates. Launcher settings and bindings are backed up under updates/rollback-preferences in the installation user-data folder; your career is not reset. Close the game before updating. Downloads are verified before setup opens; settings are retained. Windows may ask for administrator approval. ZIP installs become installer-managed when updated through setup.

Documentation
The installed README contains setup, controls, recovery and current limitations. GitHub Releases contains published installers and ZIP packages.",
        "3D beyond the cockpit (Experimental)
Advanced → Graphics → 3D beyond the cockpit adds headset 3D and head tracking to supported bonnet, bumper and chase cameras. Off by default; save and relaunch VR to apply. Use the game's Change camera control to switch views. Toggle VR still returns to the virtual screen. Desktop play is unchanged.

This is the first stage of expanded VR views. Menus, dialogs, replay cameras and movies still use the virtual screen. Seat adjustments apply only inside the cockpit and are retained when you change cameras. Chase cameras retain the game's movement and may be less comfortable than the cockpit.

Experimental rewind
Advanced → Frame-rate-independent rewind → On (Experimental) is off by default. Save settings and launch a new session to apply it. It works in single-player Normal Launch, Direct practice and Race, for desktop and VR. LAN ignores this setting. Desktop play requires DirectX 11.

What it does
At high frame rates, the original game fills its rewind buffer faster. This option records up to 60 snapshots per second while the game continues at its normal frame rate. It does not cap gameplay FPS or change headset refresh rate, and it does not need diagnostic logging.

Current testing
Two desktop rewind/resume tests in the Subaru Group N on Croatia — Velebit Adventure retained 10 seconds instead of about 5 seconds at roughly 120 FPS. Duration can vary with the event. Other cars/events and VR still need testing.

If something looks wrong
Turn the option off, save and relaunch. Report the event, car and whether the problem happened during rewind or after resuming. The game executable on disk is unchanged.",
        "Adjusting your seat (Experimental)
In cockpit VR, press Tab to open Seat position. Stop the car before adjusting: the race continues while the panel is open. The panel stays where you opened it and works even with the racing HUD hidden.

Keyboard and Xbox
Up/Down raises or lowers the seat. Left/Right moves back or forward. Hold Shift (Xbox X) to move sideways instead. Enter (A) saves; Escape (B) cancels. Tab saves and closes. Recenter keeps your seat position.

Wheels
Bind Open seat adjustment in Controls. Your wheel POV hat navigates the panel; assign Panel sideways modifier, Panel save and Panel cancel to wheel buttons. Keyboard controls remain available. Assigned seat shortcut buttons are reserved in cockpit VR, including both parts of a pair. Panel navigation buttons are reserved while the panel is open; unrelated driving controls keep working.

Universal or per-car positions
Graphics → VR cockpit provides Height, Forward/back and Left/right sliders, plus Reset seat position for the selected car. Save settings before launching. Positions are saved per car, including cars selected through the normal game menus. Enable Use universal seat position for one shared position in every car. The car picker is disabled; launcher sliders/reset and in-game adjustments then change the shared position. Your individual car positions stay saved and resume when you turn the toggle off. Mode changes apply on the next launch. Positive values mean up, forward and right. Adjustment is limited to 50 cm each way; extreme positions can expose missing cockpit geometry.

Separate shortcuts
Controls also provides six optional seat movement bindings. These are unassigned by default and save automatically when movement stops. In the panel, Save keeps the preview; Cancel, leaving cockpit VR or changing cars discards it. In per-car mode, adjustment is disabled if the car cannot be identified. Universal mode does not need a car identity.

Current testing
Seat adjustment is experimental. Headset and physical-wheel checks are separate from automated tests.",
        "Custom tracks (Experimental)
Close DiRT 2. On Launcher, check CUSTOM tracks, choose a Track pack, then Build and install…. Select your detected DiRT 3 Complete Edition folder, paste its path, or use Browse. Check the layouts you want to build, using Select all or Select none as needed. Choose Build and install to check the source files and convert only the checked layouts. Unchecked layouts already installed are kept; your choices are remembered for each location. Manage → Rebuild from source uses the same checkboxes, so you can update or add individual layouts. Conversion tools are included with DiRT2VR; no extra download, SDK or separate .NET installation is needed. No game assets are distributed.

Play
For any Aspen or Smelter layout, choose Direct practice or Race and any installed Car. Choose Launch for desktop play or Launch VR with SteamVR and your headset ready. Practice is solo; Race supports same-car, mixed or same-class opponents. Use the sliders to choose opponents and 1–20 laps. Rallycross/Landrush allow up to seven AI opponents; Head-to-head courses allow one. Broader AI racing and headset rendering still need gameplay validation, especially duel-course timing. LAN and knockout Head-to-head rules are unavailable. Current installed tracks need no rebuild for these options; very old Aspen installs may request an AI-path update. Conversion happens before installation, never during game loading.

Buttermilk snow lighting
Buttermilk Climb and Descent use Lower exposure (default) to retain bright-snow detail. Climb has been visually checked; Descent still needs a check. The abrupt brightness border remains unresolved. No track rebuild is needed for installed layouts. Snow lighting also offers Original exposure (reference) and Bloom off (diagnostic). Exit between comparison sessions; changing layout or reopening the launcher returns to lower exposure. Original effects are restored after exit, and other tracks keep their existing exposure.

Additional Aspen layouts
All ten Aspen layouts offer Direct practice and Race, any installed car, and desktop or VR play. Rallycross and Landrush support 1–7 AI opponents; Buttermilk Climb/Descent have two-car grids and allow one AI. Your larger-grid opponent count is kept when switching layouts. The Buttermilk courses use separate starting lanes: AI timing and finishes still need testing, and knockout Head-to-head rules are unavailable. Current installed layouts do not need rebuilding for these options. Eagle Hill uses night lighting, Brush Creek evening sun, and Buttermilk overcast snow lighting. Broader AI, car and headset checks remain pending.

Smelter test
All ten Smelter layouts offer Direct practice and Race with any installed car, in desktop or VR. Dredger Duel and Furnace Duel allow one AI opponent; other courses allow up to seven. Race timing on the separate-lane duel courses needs testing; knockout Head-to-head rules are unavailable. Current installations need no rebuild to unlock modes/cars. Use Manage → Rebuild from source to add missing layouts or apply conversion fixes. Conditions follow source defaults, without active rain. Expanded AI racing and headset rendering still need gameplay checks.

Aspen layouts
Lakeside: night. Lake View: morning sun. Snowmass Sprint: evening sun. Snowmass Loop: overcast. These are Rallycross layouts. Ski-lift animation, full snowfall and deformable snow remain unsupported.

Nordschleife
Nordschleife uses your installed Assetto Corsa standard circuit. Select CUSTOM tracks → Nordschleife → Build and install…, then choose the Assetto Corsa folder containing `content\tracks\ks_nordschleife`. Choose Daylight, Overcast or Evening, an installed car, and Direct practice or Race. Race supports one to twenty laps on desktop or in VR, one to seven AI opponents, car choices and difficulty settings. Direct practice uses one lap. Existing solo installations need Manage… → Rebuild from source… for the separated race grid; Direct practice and previous best laps are retained. These are fixed dry lighting presets. Eight timing checkpoints divide the circuit into roughly 2.3 km sectors. Best lap shows your fastest completed Direct practice time and its car for the layout, across all lighting presets; Race results appear in game. The full trackside scenery and distant landscape are included. A complete evening practice lap has been player-tested; AI races are experimental, and lighting and visibility/LODs remain in development. For native VR, start SteamVR, connect your headset and choose Launch VR. Nordschleife VR is experimental; full-course headset rendering and performance still need player testing. Existing packs need no rebuild to enable VR.

Mizu Mountain
Build the full 10.4 km daylight route from your own GRID 2 installation: CUSTOM tracks → Mizu Mountain → Build and install…. Select the GRID 2 game folder, then Launch or Launch VR for Direct practice or experimental Race. Race supports one to seven opponents, opponent-car choices and difficulty. Existing installations need Manage → Rebuild from source for the starting grid and the MIZU pre-race label. Each session goes from start to finish once, with three timing checkpoints. Subaru STI is the tested practice car; other installed cars can be selected. VR is experimental; LAN is unavailable. Some materials and lighting are approximations; movable props and soft vegetation are visual-only, and crowds and some dynamic effects are omitted. Stop build safely cancels conversion. Installed tracks work offline; keep GRID 2 available for rebuilds.

Offline and original tracks
Installed tracks work offline without the source game. Keep the source installation for rebuilds and updates. Each pack keeps its own layout, car, race, difficulty and lap settings, separately from stock tracks. Race difficulty uses Easy, Casual, Serious, Savage, Extreme and Hardcore, as in DiRT 2. Use game setting keeps the saved game choice. It applies to stock and custom Race launches, without changing career or multiplayer difficulty. Turning CUSTOM tracks off restores your original event, track and car selections. Opening the list or selecting a layout does not download anything. Successful file checks are remembered while the launcher stays open; installed-file changes trigger a new check. Manage → Verify installed files always checks again.

Manage and recover
Manage offers file verification, rebuilding from source and uninstalling the selected pack. Unsupported source files are named: choose the correct folder or verify original files in Steam. The green status message shows checking and building progress. Stop build cancels these steps safely and keeps installed tracks. Stop is disabled during final installation; wait for it to finish. External edits are preserved; move them aside before retrying. After an interrupted session, close DiRT 2 and choose Restore original files.",
        "Modern interface
Settings → Appearance → Modern interface previews the modern compact layout immediately. Off restores Classic. Save settings keeps your choice. Existing settings start in Classic; fresh installations start in Modern. Both follow Windows light/dark appearance and high-contrast colours.

Controls
Click a keyboard cell to change it. In Modern, click a controller cell to replace that assignment, + to add another, or × to remove one. Hover a shortened cell to read the full device and button names, including disconnected devices. Classic keeps its Bind and Remove selected buttons. Assignments work the same in either layout.

Seat adjustment
Open seat adjustment is recommended instead of six separate movement bindings. Expand Individual seat bindings (optional) for all six directions and Panel sideways modifier, Panel save and Panel cancel. Fixed panel keyboard shortcuts are read-only. Collapsing that section or changing appearance cancels active binding capture.",
        "Choose a career
Profile lists Current game career and careers created in DiRT2VR. Select a row, then Use this profile. Selection is saved immediately, separately from Save settings, and applies to Normal Launch, Practice, Race and LAN in Desktop or VR. Return to menus keeps the same career. Close the game before switching or creating profiles.

Create a new career
Choose Create new profile, enter a name, then Fresh career or 100% completed career. Use 1–24 letters (A–Z) or numbers (0–9), without spaces or symbols. Creation finishes in the launcher and automatically makes the new career active. The active row is green and marked Active. Use this profile switches to an existing career. Existing names stay unchanged. There is no unlock option for an existing career.

Single-word names also appear on in-game surname displays. This works with existing launcher profiles and does not rewrite their saves.

Delete a career
Select a launcher-created career and Delete profile. Confirm its name before permanently deleting its saves from this Windows account. Choose another career for launching first. Current game career cannot be deleted here.

Completed careers
Includes stock career wins, cars, liveries, rewards, All-Star upgrades and teammate relationships. Achievements, personal-best times and external content are not included.

Save locations
Your current game career stays where it is. New careers are stored under %LOCALAPPDATA%\DiRT2VR\profiles and are shared between DiRT2VR installations on this Windows account. Each installation remembers its own selection. Normal launches outside DiRT2VR still use your original game career. Graphics settings and launcher bindings are shared between careers.

Details and recovery
Refresh reads the latest saved data. Unreadable details show Unavailable; intermediate completion percentages and the garage count are not yet supported. When the original game has several saved careers, choose between them inside its Load Profile menu. An unavailable selected career blocks launching until you explicitly select a valid one. Keep backups of your saves."
}
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
