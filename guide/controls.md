# Controls

[← Back to README](../README.md)

This guide covers the **Controls** tab, the in-game seat panel, Photo Mode and replay controls.

Controls has **General**, **VR**, **Driving** and **Seat** subtabs. General contains cockpit animation and chase-camera settings. VR contains Toggle VR and Recenter; Seat contains the seat panel shortcut and optional individual bindings. The binding rows have **Action**, **Keyboard** and **Controller / wheel** columns. Click an action's keyboard binding to change it, optionally with Ctrl, Alt or Shift. Escape or switching tabs cancels capture without discarding other edits.

| Default | Action |
|---|---|
| **F9** | Toggle virtual screen / cockpit VR; also recenter |
| **F10** | Recenter |
| Normal game controls | Driving and menu navigation |

## Driving controls

**Driving controls:** Direct practice and Race now load the existing saved profile before the event, including its control settings. Save your profile after configuring controls in the game. No additional career save is created. Direct launches check sign-in before loading: accounts already signed in continue straight to the event; otherwise, use the game's GFWL sign-in prompt. Canceling sign-in or a reported career-load error ends the launch without creating a replacement save. If no saved career exists, the event uses the game's defaults. Fanatec pedal validation is pending.

For optional launcher assignments, open **Controls → Driving → Configure driving controls…**. For Xbox controllers, **Xbox preset** applies left-stick steering, RT accelerator, LT brake, A handbrake and B/X shifting, plus Start to pause/start, A to confirm, B to go back and D-pad menu navigation. Missing keyboard assignments are filled with the game's default keys; your custom keys are kept. If an earlier preset left keyboard controls blank, apply **Xbox preset** again. Choose **Save driving controls**, then launch again.

For a custom controller or wheel, bind **Pause** and **Menu Start Button** to your Start button. **Menu Select** confirms, **Menu Back** cancels, and **Menu Up/Down/Left/Right** navigate. You can also bind keyboard Escape; during keyboard capture, use the **Cancel** button to leave without assigning it.

**Binding wizard…** walks through the actions in order, automatically listening to all connected controllers. Center the wheel/stick, release pedals and handbrake, and wait for **Ready**. Move the requested control, hold an axis briefly, then return to rest; the wizard advances automatically. A nonzero resting handbrake axis or held switch does not count as a new input. A wheel's D-pad (POV hat) can be bound to **Menu Up/Down/Left/Right** and other button actions: press straight up, down, left or right, not diagonally. Choose a specific device or **Axis only / Button only** if necessary. Steering, pedals and shifters may use separate devices. For keyboard assignments, select **Keyboard** in the wizard. **Skip** keeps the current assignment; **Back** revisits a step; **Cancel** discards the wizard's changes. Review, **Apply bindings**, then **Save driving controls**. Applying bindings enables launcher overrides.

During capture, turn the wheel clearly left/right and press pedals fully. Small pedal movements are ignored; these capture thresholds do not change driving sensitivity.

To change just one action, select it and choose **Bind keyboard…** or **Bind device…**. **Calibration…** adjusts direction, dead zone and saturation; saved values are shown beside each controller assignment. Enabled launcher assignments and calibration reapply on every launch, replacing in-game changes for those actions. Newly bound wheel, pedal and Xbox trigger axes start with a 0% dead zone; Xbox thumbsticks start at 20% so a resting stick doesn't drift. Assignments saved before keep their values: to retain zero steering deadzone, set 0% for both **Steer Left** and **Steer Right**, then **Save driving controls**. Alternatively, disable launcher driving bindings and use the game's saved controls. The editor warns about identical left/right steering directions and inverted Xbox triggers; repair old assignments with the wizard or Xbox preset. Physical Fanatec handbrake validation remains pending.

**Wheel rotation:** after binding **Steer Left** and **Steer Right** to your wheel, choose **Wheel rotation…**. Enter your wheel's rotation lock to lock, as set in its software or, on Fanatec wheelbases, its **SEN** value. Logitech and Thrustmaster wheels with a fixed range get their maximum suggested. Then choose how far you want to turn for full steering lock in DiRT 2. **Apply** sets the steering saturation to match: for example 540° on a 1080° wheel reaches full lock at half the wheel's travel. The cockpit wheel can't turn further than each car's animation, so lower DiRT 2's rotation until the cockpit wheel keeps up with your hands. If your wheel's software can set its rotation, setting it to the same value also stops the wheel physically at full lock. Save driving controls to apply it on the next launch; presets keep it. A driving check with a wheel is still pending.

**Transmission (experimental):** choose **Automatic**, **Manual sequential**, **Manual H-pattern** or **Manual H-pattern + clutch** to use that transmission for your car in every launcher mode, on desktop or in VR, without changing the setting saved in your career. The choices match the game's own options: sequential and H-pattern use an automatic clutch, while **+ clutch** uses your clutch pedal, as the game does when you pick H-pattern with a clutch assigned, so it needs a **Clutch** binding. H-pattern uses your **Gear 1–6** and **Gear Reverse** bindings. **Use game setting** (the default) leaves the transmission alone. The Ferrari F2004 always uses sequential. Opponents are not affected. This uses DX11; save and relaunch to apply. The choice is saved with presets. It has automated checks only; a driving check of each choice is still pending.

**Presets:** choose **Save preset…** to keep the current bindings, calibration, both checkboxes and the transmission choice as a named file, for example one for your wheel and one for a controller. **Load preset…** replaces the editor's bindings with a saved preset; choose **Save driving controls** to use it on the next launch, or **Cancel** to keep your previous controls. Presets are stored in `%LOCALAPPDATA%\DiRT2VR\driving-presets` and shared by your DiRT2VR installations on this Windows account. Bindings remember the exact device, so a preset made on another PC may need its devices rebound.

**Live device input** shows the selected controller, wheel or pedals while you move them, including axes, buttons and POV hats. Choose **Refresh** after connecting a device. These are raw readings before calibration; a resting pedal at one end of its range is normal. Viewing inputs does not change bindings. While you press a button, the D-pad or move an axis on the selected device, its row in the bindings list lights up in the accent colour, so you can see which action it is bound to.

The editor includes steering, accelerator, brake, clutch, handbrake, sequential shifts and H-pattern gears. Select the appropriate transmission/assist settings in the game, or use **Transmission** below. **Use sequential and H-pattern shifts together** is an experimental option for manual transmission: the most recently used shift button or moved shifter controls the gear, so a parked H-shifter does not undo a sequential shift. It needs a manual transmission, chosen in-game or with **Transmission**. Native H-pattern bindings cover gears 1–6 and reverse; use sequential shifts for seventh. Hardware acceptance is pending. It applies to Normal Launch, Practice, Race and HOST/JOIN, in desktop and VR modes, using the DX11 renderer. Unassigned actions retain game settings; assigning an action replaces that action's saved inputs, so assign both keyboard and controller inputs if wanted. **Use game binding** removes that action's launcher override. Disabling overrides does not undo bindings subsequently saved by the game.

Driving capture uses XInput for Xbox controllers and non-exclusive DirectInput for wheels/pedals. Wheel models and the combined LAN/VR binding path still need hardware acceptance. The game identifies DirectInput devices by product name; two identical models cannot be distinguished reliably. Force-feedback tuning is not included.

**Controls → Driving → Force feedback → Keep on** is on by default. For wheels DiRT 2 doesn't recognise, such as the Logitech G920, its force feedback option is off again at every launch, so turning it on in the game's Options didn't last. Keep on switches it back on at the start of each race in Normal Launch, Practice, Race and HOST/JOIN, desktop and VR, using the DX11 renderer. To keep force feedback off, turn Keep on off; the game's own choice then applies. Save and relaunch to apply. Not yet checked on every wheel.

## VR shortcuts

Controller shortcuts start unassigned. Choose **Bind…** in the desired action's row, press one button or two buttons together on the same device, then release them. Multiple devices may be assigned to each action. Select an assignment and choose **Remove selected** to clear it. The launcher rejects overlapping assignments. Choose **Save settings**; changes apply on the next launch.

**Assigned controller buttons still perform their normal game actions.** Choose buttons or combinations that avoid unwanted driving/menu actions. Shortcuts fire once per press and require release before firing again. Keep the game window focused.

Assignments stay attached to the selected device. A disconnected device is not replaced automatically; Xbox controller slot changes may require rebinding. Wheel shortcuts use the same non-exclusive DirectInput reader as driving bindings. Connect the wheel, choose Bind, then press and release a button or pair. Already-held switches are ignored. Existing HID and Xbox assignments remain supported. This updated VR shortcut path still needs physical Fanatec validation.

## Seat position (Experimental)

In cockpit VR, stop the car and press **Tab**. Use **Up/Down** to raise/lower your seat and **Left/Right** to move back/forward. Hold **Shift** for sideways movement. **Enter** saves, **Escape** cancels, and **Tab** saves and closes. The race continues while the panel is open.

Xbox controllers use the D-pad, **X** as the sideways modifier, **A** to save and **B** to cancel. Assign a panel-opening button in **Controls**. Wheels can use their POV hat. To save and cancel, a wheel uses the buttons you assigned to **Menu Select** and **Menu Back** in **Configure driving controls**, unless you assign panel save/cancel buttons in Controls; assign the panel modifier there too.

**Graphics → Cockpit seat** also has position sliders and a per-car reset. Positions are saved separately for each car and survive recentering. Enable **Use universal seat position** for one shared position across all cars. The car picker is disabled in this mode; sliders, reset and in-game adjustments edit the shared position. Turn it off to restore your saved per-car positions. Save settings and relaunch to change modes. The seat panel is the recommended way to adjust your view. All nine optional shortcuts—six movements, Panel sideways modifier, Panel save and Panel cancel—are under **Controls → Seat → Individual seat bindings (optional)**, collapsed by default in both styles. Expand it to assign them. Fixed panel keyboard shortcuts are read-only. Extreme offsets can reveal missing interior geometry. Panel movement, save/cancel, recenter retention, relaunch persistence and universal seat mode have passed headset checks. Physical wheel navigation and broader rendering checks with adjusted seats remain unverified.

**Desktop cockpit view:** VR and desktop keep separate seat positions. Set **Positions for** to **Desktop** to edit the desktop cockpit (head) camera, per car or universal; **VR** edits the headset seat. In Desktop mode, **Field of view (desktop)** sets the cockpit view's vertical angle for every car: DiRT 2 uses 52°, and you can choose 40–90°. Raise it to see more of the cockpit, lower it to zoom in. VR ignores this setting and uses your headset's view. Both apply from the next launch in Game menus, Direct practice, Race and LAN games. The launcher restores the original camera files after the game closes.

**In-game desktop seat panel (experimental):** in desktop Direct practice and Race, press the seat panel shortcut (**Tab** by default) in the cockpit view. **Up/Down** raise or lower the seat, **Left/Right** move it back or forward, hold **Shift** for sideways, and **Page Up/Page Down** change the view angle by one degree. **Enter** saves, **Escape** cancels and **Tab** saves and closes. Saved changes go to the desktop seat positions and **Field of view (desktop)**, and the view follows immediately. With an Xbox controller, bind **Open seat adjustment** under **Controls → Seat** (as for VR); then the D-pad moves the seat, **X** is sideways, **A** saves, **B** cancels and **RB/LB** change the view angle. The panel is not available in Game menus mode or LAN games. While the panel is open, its keys do not reach the car.

## Cockpit animation

**Controls → General → Cockpit animation → Remove artificial steering corrections (Experimental)** is off by default. It removes the reported extra cockpit wheel judder on desktop and in VR while retaining the game's steering filter and original animation range. Handling and force feedback are unchanged. Desktop requires DX11; select cockpit view yourself. Save settings and relaunch to apply. The correction has passed desktop and headset checks, but compatibility with every wheel and setup is not established. It does not provide full 540°/900° wheel matching; the earlier missing-hands and static-wheel report remains unresolved.

For a comparison, use the same car and event for three short runs: main option **off** (original behavior); main option **on** with **Observe only — no correction** checked (hooks active, original animation values retained); then Observe only **unchecked** (correction applied). Enable **Settings → Enable diagnostic logging** for the last two runs. Drive in cockpit view, hold the wheel centred, turn slowly both ways, then drive over rough ground. The log records a bounded set of steering values, wheel-animation positions and hand-blend weights. Observe only does not enable logging by itself.

## Chase camera

**Chase-camera free look (experimental):** enable **Controls → General → Chase camera → Free look**, save settings and relaunch, then select a chase view using the game's camera control. Free look starts **off**. Desktop play requires DX11. For VR driving, enable **Advanced → Graphics → 3D beyond the cockpit**; for replay 3D, use the separate **3D chase replays** switch. Desktop controls and VR chase-replay orbit/zoom have passed player checks.

- Move the **right stick** to orbit around your car, or use the mouse. Choose **Hold left**, **Hold right** (default) or **Always on** with the mouse-icon buttons.
- Hold the **right stick pressed in (R3)** to look behind. Release it to return to the normal chase angle, even while stopped.
- Scroll the **mouse wheel forward** to move closer or **backward** to move farther away. No mouse button is needed. **Controls → General → Chase camera → Maximum zoom out** sets the farthest distance from 180% to 1000% of normal, in 10% steps. The default is the original 180%; try 500% or 750% for a wider view. The closest distance stays at 60%. The same limit applies to replay stick/key zoom. Save and relaunch to apply. Zoom resets when changing chase views or starting a new session; field of view stays unchanged and obstructions can keep the camera closer.
- Adjust mouse/stick sensitivity and **Invert vertical** to suit your controls.

Your angle stays while stopped. When moving, the camera smoothly returns behind the car after you stop looking around. To keep your chosen angle relative to the car while driving or in chase replays, check **Keep camera angle** beside **Free look**, then save settings and relaunch. This option starts off; R3 look back still returns to the normal chase angle when released. Pause, menus and switching apps stop mouse look. The Windows pointer is hidden over the game picture while free look is active and returns for menus, other camera views and window borders.

## Photo Mode

**Photo Mode (experimental):** pause an offline race or practice session and choose **PHOTO MODE** to move a free camera while the race stays frozen. It requires DX11 and is unavailable in network races. It works independently of chase-camera Free look and the extended VR views settings.

**In replays:** while watching a full replay (from **Launcher → Replays**, or **Full Replay** after a race), press **Esc** and choose **PHOTO MODE** in the replay menu. The replay stays frozen at that moment and the camera starts from the view you were watching, including the trackside cameras. The replay controls are hidden in Photo Mode and in your photos. **Esc** returns to the replay menu, and **Continue** plays on from the same moment. After a normal launch (game menus), Esc while watching a race replay opens the same replay menu instead of leaving the replay straight away; **Exit replay** leaves it as before.

**Following other cars in replays:** while a replay plays, press **F6** to follow the next car with a chase camera, and **F5** for the previous one. After the last car, the replay's own cameras return. A short message shows which car you follow. Only cars that have been on screen recently can be chosen: DiRT2VR finds cars from what the game draws, so a car far out of view is not on the list until it appears. If the followed car leaves the picture, the replay's cameras return. Desktop only.

**Jump to car:** in Photo Mode, the **Jump to car** row in the Camera section puts the camera behind a car; left/right picks the next or previous one. The same rule applies: in a replay the cars seen recently, in a paused race the cars that were on screen while you drove.

| Action | Keyboard/mouse | Xbox controller |
|---|---|---|
| Move | WASD | Left stick |
| Look | Mouse | Right stick |
| Rise / descend | E / Q | Right / left trigger |
| Faster | Shift | Right bumper |
| Slower, for precise framing | Ctrl | Left bumper |
| Zoom in / out (desktop) | Mouse wheel, Page Up / Page Down | D-pad up / down (panel hidden) |
| Tilt the camera left / right (desktop) | Z / C, the keys either side of X (Y / C on German-style keyboards) | D-pad left / right (panel hidden) |
| Clear zoom and tilt | Middle mouse button | Right-stick click |
| Back to the starting view | R | Y |
| Save a photo (desktop) | P | A |
| Choose a setting in the Photo panel (desktop) | Up / Down arrows | D-pad up / down |
| Change the chosen setting | Left / Right arrows | D-pad left / right |
| Hide / show the Photo panel (desktop) | H | View (Back) |
| Depth of field on / off (desktop) | F | X |
| Focus nearer / farther | 1 / 2 (top row) | Photo panel |
| Less / more blur | 3 / 4 (top row) | Photo panel |
| Autofocus on the picture centre | Right mouse button | Left-stick click |
| Next / previous filter (desktop) | G / Shift+G | Photo panel |
| Darker / brighter | 5 / 6 (top row) | Photo panel |
| Letterbox: off, 2.39:1, 1.85:1 | L | Photo panel |
| Rule-of-thirds grid | T | Photo panel |
| Return to the pause menu | Esc | B or Start |

On desktop, the **Photo panel** on the right lists every Photo Mode setting: camera (field of view, tilt, move speed), depth of field, look and, when set up, DLSS 5. Use the **arrow keys** (or the **D-pad**) to choose a setting and change it; the shortcut key for each setting is shown next to it. **Move speed** sets how fast the camera travels before Shift or Ctrl. A bar along the bottom lists the main controls for your keyboard or controller, depending on what you used last. Press **H** (or **View**) to hide the panel and the bar for a clear view; they stay hidden until you show them again. While the panel is hidden, the D-pad zooms and tilts.

**Saving photos:** press **P** (or **A**) to save the current picture as a PNG at the game's full rendering resolution. The Photo panel and messages are never included in the photo. Photos go to **Pictures\DiRT2VR** with the date and time in the file name; a short message confirms the save. Photo saving and the panel are desktop-only for now.

**Depth of field:** press **F** (or tap **X**) to blur what is nearer or farther than the focus point, like a camera lens. It starts with autofocus on whatever is at the centre of the picture, so aim at your subject. Use **1 / 2** to set the focus distance yourself and **3 / 4** to change the amount of blur (1–10), or change them in the Photo panel, which also shows the current focus distance. The right mouse button (or left-stick click) returns to autofocus. Saved photos include the blur. Depth of field uses the game's shadow pass to find distances, so it needs in-game shadows switched on; the panel says when it is unavailable. It is desktop-only.

**Filters:** press **G** (Shift+G to go back) to choose a look: Vivid, Warm, Cool, Black & white, Sepia, Film, Noir or Cinematic; **Off** shows the original picture. **5 / 6** adjust brightness. **L** adds cinema letterbox bars (2.39:1, then 1.85:1, then off), and **T** shows a rule-of-thirds grid to help framing. Filters, brightness and letterbox are saved in your photos; the grid is only on screen. With a controller, change them in the Photo panel. Filters are desktop-only.

**Your own filters:** the presets from **Graphics → Filters** (the built-in Natural, Vivid and Muted, and any you made or imported) also appear in the filter list, after the Photo Mode looks. A name that matches a Photo Mode look gets "(DiRT2VR)" added. Photo Mode applies their colour settings on top of the picture you see: brightness, contrast, saturation, gamma and tint, using the daytime values. Bloom, exposure and motion-blur settings only take effect when chosen for launch in Graphics → Filters, and so does removing a track's own tint. The result is close to, but not exactly, what the filter does when chosen for launch. If a launch filter is already active, the Photo Mode version is added on top of it.

**Neural rendering (DLSS 5, experimental):** if you have your own copy of NVIDIA's DLSS 5 Neural Rendering runtime (`nvngx_dlssnr.dll`), you can use it for a neural-rendered look in Photo Mode.
- **Setup:** set its folder under **Graphics → DLSS 5 neural rendering (desktop) → DLSS 5 runtime folder**, save, and relaunch. The launcher shows whether it found the file.
- **Using it:** in Photo Mode press **N** (or choose **Neural rendering** in the Photo panel) to turn it on or off. The panel shows "Starting…" for a moment, then "On". Depth of field, filters and saved photos all build on the neural-rendered picture.
- **Options:** the launcher section and the Photo panel offer the same settings. Changes in the Photo panel last until you quit the game; the launcher sets the starting values, which are also used while driving.
  - **Style:** Natural (default), Standard or Cinematic. Changing it restarts the effect, which takes a moment.
  - **Strength:** how strongly the neural look is applied (0–200%).
  - **Local tone** and **Local detail:** local contrast and lighting changes, and fine texture detail (0–200%).
  - **Passes:** 1–3. Each extra pass runs the effect again on its own result for a stronger, more stylised look, and costs another full pass of GPU time.
  - **Mix with original:** blends the result with the game's own picture; lower it for a subtler look.
  - **Resolution:** 50–100%. Runs the effect on a smaller picture and adds its changes to your full-resolution frame, so the picture stays sharp, but the neural look gets softer. Lower is faster. Changing it restarts the effect for a moment.
  - **Foveated centre:** Off, or 30–80% of the width and height. Keeps the centre of the picture at full resolution while the edges use the Resolution above, like the foveated option in Assetto Corsa's Custom Shaders Patch. It only works when Resolution is below 100%. It adds a second, smaller pass, so it saves time mainly at high resolutions. In a test at 3840×2160 on an RTX 4080 SUPER, one pass took about 18 ms at 100%, 7 ms at 50%, and 12 ms at 50% with a 50% centre. At 1280×720 the savings were small.
- **Requirements:** an NVIDIA RTX graphics card with a current driver, and DX11. Each frame takes extra time, so Photo Mode may feel slower while it is on.
- **Not included:** DiRT2VR does not include or download the runtime. Desktop only, and not available in VR, LAN or replays.
- **While driving (experimental):** tick **Also while driving** under the runtime folder to apply it during races too. The HUD is drawn on top unprocessed, so it stays sharp. It is heavy on the graphics card, lowers frame rate and adds one frame of delay, so try it on a fast RTX card and turn it off if driving feels sluggish. To keep the picture steady, its changes are averaged over a few frames wherever the picture is not moving (for example the cockpit or the sky); moving areas always use the current frame. Desktop only.
- **Changing it while driving:** press **F8** to open the DLSS 5 panel. Use the **arrow keys** (or the **D-pad**) to choose and change a setting, and **F8** or **Esc** to close it. **Neural rendering** turns the effect off and on without leaving the race; the other settings are the same as in the Photo panel. The race keeps running while the panel is open, and the game does not get the arrow keys, Esc or the D-pad, so stop first if you steer with the arrow keys. Changes last until you quit the game. F8 is always taken by the panel while driving with DLSS 5.
- **If the panel says "unavailable":** the runtime could not start on your graphics card or driver. Turn on diagnostic logging and check `neural-helper.log` in the session log folder.

Looking slows down while zoomed in, so close-up framing stays steady. On desktop the camera eases in and out of movement. Slower mode also makes zoom and tilt finer.

Choose Continue in the pause menu to resume driving. Moving the camera does not move your car. Switching away from the game returns to the pause menu too. If you choose **PHOTO MODE** again before resuming the race, the camera continues from your last shot; once you resume driving, the next Photo Mode starts behind the car again.

The camera has no collision, so you can fly through scenery.

VR Photo Mode uses stereo rendering, independent head tracking and smooth mouse/stick turning. Zoom and tilt are not available in VR, where your headset sets the view, and movement starts and stops immediately for comfort. Leaving it restores your driving view and seated reference. Desktop practice and AI Race, plus headset/controller practice, have passed bounded checks. Broader track coverage remains experimental.

## Replay controls

Open **Controls → Replay → Configure replay controls** to bind playback, camera changes, flashback resume and chase zoom. In chase replays, the Xbox left stick moves up to zoom in and down to zoom out; Page Up/Page Down and the mouse wheel also work. Right-stick orbit is unchanged. Enable **Controls → General → Chase camera → Free look** for orbit and zoom. Changes apply on the next launch. Custom zoom supports keyboard and Xbox inputs; native playback controls also support wheels. These zoom bindings do not affect driving.
