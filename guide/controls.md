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

**Binding wizard…** walks through the actions in order, automatically listening to all connected controllers. Center the wheel/stick, release pedals and handbrake, and wait for **Ready**. Move the requested control, hold an axis briefly, then return to rest; the wizard advances automatically. A nonzero resting handbrake axis or held switch does not count as a new input. Choose a specific device or **Axis only / Button only** if necessary. Steering, pedals and shifters may use separate devices. For keyboard assignments, select **Keyboard** in the wizard. **Skip** keeps the current assignment; **Back** revisits a step; **Cancel** discards the wizard's changes. Review, **Apply bindings**, then **Save driving controls**. Applying bindings enables launcher overrides.

During capture, turn the wheel clearly left/right and press pedals fully. Small pedal movements are ignored; these capture thresholds do not change driving sensitivity.

To change just one action, select it and choose **Bind keyboard…** or **Bind device…**. **Calibration…** adjusts direction, dead zone and saturation; saved values are shown beside each controller assignment. Enabled launcher assignments and calibration reapply on every launch, replacing in-game changes for those actions. To retain zero steering deadzone, set 0% for both **Steer Left** and **Steer Right**, then **Save driving controls**. Alternatively, disable launcher driving bindings and use the game's saved controls. The editor warns about identical left/right steering directions and inverted Xbox triggers; repair old assignments with the wizard or Xbox preset. Physical Fanatec handbrake validation remains pending.

**Live device input** shows the selected controller, wheel or pedals while you move them, including axes, buttons and POV hats. Choose **Refresh** after connecting a device. These are raw readings before calibration; a resting pedal at one end of its range is normal. Viewing inputs does not change bindings.

The editor includes steering, accelerator, brake, clutch, handbrake, sequential shifts and H-pattern gears. Select the appropriate transmission/assist settings in the game as well. **Use sequential and H-pattern shifts together** is an experimental option for manual transmission: the most recently used shift button or moved shifter controls the gear, so a parked H-shifter does not undo a sequential shift. Stock cars still require manual transmission selected in-game. Native H-pattern bindings cover gears 1–6 and reverse; use sequential shifts for seventh. Hardware acceptance is pending. It applies to Normal Launch, Practice, Race and HOST/JOIN, in desktop and VR modes, using the DX11 renderer. Unassigned actions retain game settings; assigning an action replaces that action's saved inputs, so assign both keyboard and controller inputs if wanted. **Use game binding** removes that action's launcher override. Disabling overrides does not undo bindings subsequently saved by the game.

Driving capture uses XInput for Xbox controllers and non-exclusive DirectInput for wheels/pedals. Wheel models and the combined LAN/VR binding path still need hardware acceptance. The game identifies DirectInput devices by product name; two identical models cannot be distinguished reliably. POV/hat capture and force-feedback tuning are not included.

## VR shortcuts

Controller shortcuts start unassigned. Choose **Bind…** in the desired action's row, press one button or two buttons together on the same device, then release them. Multiple devices may be assigned to each action. Select an assignment and choose **Remove selected** to clear it. The launcher rejects overlapping assignments. Choose **Save settings**; changes apply on the next launch.

**Assigned controller buttons still perform their normal game actions.** Choose buttons or combinations that avoid unwanted driving/menu actions. Shortcuts fire once per press and require release before firing again. Keep the game window focused.

Assignments stay attached to the selected device. A disconnected device is not replaced automatically; Xbox controller slot changes may require rebinding. Wheel shortcuts use the same non-exclusive DirectInput reader as driving bindings. Connect the wheel, choose Bind, then press and release a button or pair. Already-held switches are ignored. Existing HID and Xbox assignments remain supported. This updated VR shortcut path still needs physical Fanatec validation.

## VR seat position (Experimental)

In cockpit VR, stop the car and press **Tab**. Use **Up/Down** to raise/lower your seat and **Left/Right** to move back/forward. Hold **Shift** for sideways movement. **Enter** saves, **Escape** cancels, and **Tab** saves and closes. The race continues while the panel is open.

Xbox controllers use the D-pad, **X** as the sideways modifier, **A** to save and **B** to cancel. Assign a panel-opening button in **Controls**. Wheels can use their POV hat; assign the panel modifier/save/cancel buttons in Controls.

**Graphics → VR cockpit** also has position sliders and a per-car reset. Positions are saved separately for each car and survive recentering. Enable **Use universal seat position** for one shared position across all cars. The car picker is disabled in this mode; sliders, reset and in-game adjustments edit the shared position. Turn it off to restore your saved per-car positions. Save settings and relaunch to change modes. The seat panel is the recommended way to adjust your view. All nine optional shortcuts—six movements, Panel sideways modifier, Panel save and Panel cancel—are under **Controls → Seat → Individual seat bindings (optional)**, collapsed by default in both styles. Expand it to assign them. Fixed panel keyboard shortcuts are read-only. Extreme offsets can reveal missing interior geometry. Panel movement, save/cancel, recenter retention, relaunch persistence and universal seat mode have passed headset checks. Physical wheel navigation and broader rendering checks with adjusted seats remain unverified.

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

**Photo Mode (experimental):** pause an offline race or practice session and choose **PHOTO MODE** to move a free camera while the race stays frozen. It requires DX11 and is unavailable in network races and replay playback. It works independently of chase-camera Free look and the extended VR views settings.

- **Keyboard/mouse:** WASD moves, the mouse looks, E/Q rises/descends, and Shift moves faster.
- **Xbox controller:** left stick moves, right stick looks, right/left trigger rises/descends, and right bumper moves faster.
- **Esc**, controller **B** or **Start** returns to the pause menu. Choose Continue to resume driving. Moving the camera does not move your car. Switching away from the game returns to the pause menu too.

The camera has no collision, so you can fly through scenery.

VR Photo Mode uses stereo rendering, independent head tracking and smooth mouse/stick turning. Leaving it restores your driving view and seated reference. Desktop practice and AI Race, plus headset/controller practice, have passed bounded checks. Broader track coverage remains experimental.

## Replay controls

Open **Controls → Replay → Configure replay controls** to bind playback, camera changes, flashback resume and chase zoom. In chase replays, the Xbox left stick moves up to zoom in and down to zoom out; Page Up/Page Down and the mouse wheel also work. Right-stick orbit is unchanged. Enable **Controls → General → Chase camera → Free look** for orbit and zoom. Changes apply on the next launch. Custom zoom supports keyboard and Xbox inputs; native playback controls also support wheels. These zoom bindings do not affect driving.
