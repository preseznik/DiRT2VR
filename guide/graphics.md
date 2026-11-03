# Graphics

[← Back to README](../README.md)

This guide covers the **Graphics** tab and the graphics options under **Advanced → Graphics**. Choose **Save settings**; changes apply to the next session, including quick launch.

## Main and Filters

Graphics opens on **Main**, with the existing Desktop, VR rendering, Cockpit seat and VR HUD settings. **Filters** edits post-processing presets. Click a section title (▾) to collapse that section and again to expand it; the launcher remembers which sections you collapsed. Hover over a setting for a short explanation. Open **? → Instructions** for more detail and troubleshooting. Tabs adapt to wide and portrait windows; smaller windows scroll vertically while the action buttons remain available.

**Filters (experimental):** choose **New** or **Save as**, then edit the preset's **Day** and **Night** adjustments. Choosing a preset in the editor, creating one or importing one selects it for **both Desktop and VR**. You can still choose different presets with the two launch selectors afterward. Click **Save settings**, then relaunch to apply. **Original** preserves the track's look. The game chooses Day or Night using its effect files, not your computer's clock.

Adjust colour, bloom and exposure relative to each track: **100%** keeps its value, brightness offset **0** keeps brightness, and tint **Off** keeps its tint. **Reset adjustments** resets the displayed Day or Night variant. Main's **Bloom Off** overrides preset glow settings; VR motion blur stays disabled. Exposure controls scale the game's native values; their visual response still needs comparison testing. Headset appearance and differing presets in LAN remain unverified—select Original if you encounter problems.

Start with **Natural** for gentler colour and bloom with the image tint removed, **Vivid** for stronger colour, contrast and bloom, or **Muted** for reduced saturation, contrast and bloom. Each includes Day and Night adjustments; gamma and exposure remain unchanged. These are starting points relative to each track's look, not a guarantee of colour accuracy. **Original** remains the default and existing selections are kept. Use **Save as** to customize a built-in preset.

Presets are shared between installations on this Windows account. **Save settings** keeps adjustments and launch selections. Changing the edited preset offers Save, Discard or Cancel. Use **Rename**, **Delete**, **Import** and **Export** to manage your library; choose and save another preset before deleting one used for launching. Built-in presets are read-only; importing an exported built-in creates an editable copy. Import/export uses versioned **DiRT2VR preset JSON**, not game XML or Assetto Corsa ppfilters. A missing or invalid selected preset blocks launch with an explanation. Original effect files are restored after play; **Restore original files** recovers an interrupted session without discarding external edits.

**Bloom** adds a soft glow around bright areas. Desktop and VR rendering have separate Bloom toggles, both **On** by default. On keeps the game's existing effects; Off removes bloom without disabling colour grading, exposure adjustment or shadows. Save and relaunch to apply. This covers Normal Launch, Practice, Race, custom tracks and multiplayer HOST/JOIN. VR's choice also applies to its flat virtual screen. Original effect files are restored after play. Bloom does not override a custom track's separate lighting choice; for example, Buttermilk's lower exposure stays in effect.

**Depth of field** under **Graphics → Main → Supported track effects** controls distance blur on supported Pikes Peak layouts. It is **On** by default and applies to every car, including chase look-back. Uncheck it for a sharper scene, then save and relaunch. Other tracks retain their original effects. Original files are restored after play.

## Desktop display

**Borderless fullscreen** is off by default. Enable it to fill the primary monitor at its current desktop resolution without window borders. It applies to desktop Normal Launch, Practice, Race and multiplayer HOST/JOIN, including desktop quick launch. The separate **VSync** toggle defaults to **On** for desktop play, with or without borderless. Turn it off for uncapped rendering. Both settings are temporary and the original display settings are restored after play. Windows resolution/refresh rate and VR launches stay unchanged. Save and relaunch to apply; Restore defaults turns it off.

Borderless mode uses the game's windowed rendering path as a workaround for its exclusive-fullscreen frame-rate limitation. Driver limits, desktop composition and performance can still affect FPS; a frame rate above 60 is not guaranteed. Native desktop resolution may cost more GPU time than your previous game resolution. If Windows rejects the window change, the launcher reports a warning and leaves ordinary windowed play available. Alt+Tab and minimization remain available.

## VR settings

The following settings apply to VR:

| Setting | Default | Effect |
|---|---|---|
| VR render scale | 100% | How much detail the game draws for each eye, 50–300%. 100% (the green notch) gives the game image as many pixels as your headset's recommended eye image, read at each VR launch. The picture sent to the headset always uses its recommended size. Graphics shows the size it will render and your headset's size from the last VR launch. |
| Anti-aliasing (MSAA) | 2× | VR-only: Off, 2×, 4× or 8×. High values cost memory and may cause crashes; the launcher warns at 4× and 8×. Desktop MSAA is restored after play. |
| Field of view | 100% | Full view. Experimental 70–99% settings crop the periphery and reduce the scene resolution proportionally. |
| Car mirrors | Game setting | Optionally force mirrors on or off during VR sessions. |
| Tree detail | Game | Choose the game's Ultra low–Ultra vegetation detail presets. Higher values keep detailed vegetation farther away. |
| Object detail | Game | Choose the game's Ultra low–Ultra trackside-object detail presets. Higher values cost performance. |
| Follow view | Off | Keep the cockpit HUD fixed relative to the car at the selected distance. Enable to have it follow your head instead. |
| Distance (VR HUD) | 1 m | Move the cockpit HUD between 1–20 metres in 0.5 m steps. Its apparent size stays constant. Applies to fixed and follow-view modes. |
| Show HUD areas | Gauges off; others on | Show or hide gauges, lap/time, race position, route map and stage progress in the cockpit HUD. |

Numeric settings use sliders with the current value beside them, including AI opponents and circuit laps. Drag a slider or use the arrow keys for one-step adjustments. These percentages scale width and height, not total pixels: 80% VR render scale uses approximately 64% of the pixels of 100%. **Restore defaults** restores the VR baseline, turns both Bloom toggles and desktop VSync on and borderless off; save afterward.

The game image stays 4:3 because DiRT 2 also shows its menus on it in VR, so at 100% it has the headset eye's pixel count in a 4:3 shape: for example 2454 × 1840 for a Valve Index's 2016 × 2240. The game supports up to 4800 × 3600; if a scale would go over that on your headset, the launcher uses the largest size that fits and says so. Before the first VR launch, 100% is 1600 × 1200. Check the measured game resolution in Graphics after a VR launch. Higher scales also increase GPU and memory use.

For vegetation and object pop-in, try **Tree detail → Ultra** and **Object detail → Ultra**, save, then relaunch VR. **Game** keeps your existing game settings. These overrides are restored after play. VR extends short terrain and trackside-object draw distances and keeps nearby opponents detailed in every direction. Distant cars still use normal detail reduction. VR also extends grass range, allows more grass to be drawn in dense areas, and keeps trees and bushes detailed farther away. The corrected grass coverage passed a player headset check at Novigrad's off-road crest, including chase-camera zoom. VR also keeps trackside props such as tire stacks, flags and scaffolding detailed farther away, with room to draw more of them. User testing in 0.17.23 confirmed that the reported Hammada Circuit pop-in appears resolved. Performance impact has not been measured. Authored visibility and individual model transitions can still cause pop-in. Compare performance on the same section of track before keeping higher settings.

Refresh rate is controlled by **your headset software**. The launcher shows the rate reported at the last launch when available, clearly marked as a past reading. The desktop game's refresh setting does not set headset Hz. Lower resolution may help GPU performance, but a particular frame rate is not guaranteed.

**Graphics → Main → VR rendering → Shadows (experimental)** starts off. On enables VR shadows, including the fix for missing trackside shadows in cockpit view. Choose **Game**, **Low**, **Medium** or **High** shadow quality; Game retains your saved quality, while High is sharper but costs more performance. Off removes shadows, like the game's Ultra low option. **Detailed distance (experimental)** defaults to the recommended **14 metres**. You can increase it up to 60 metres to extend detailed road shadows, but longer distances can cause rapid shadow flickering on walls and scenery. Return to 14 metres if this happens. To extend the outer shadow range too, choose a **Desktop → Shadow distance** and check **Also apply in VR**. Save and relaunch VR to apply. Desktop play is unchanged, and original graphics settings are restored after play.

Field-of-view cropping keeps a narrower cockpit view rather than stretching the full image. Menus use the virtual screen by default, although a lower VR render scale also lowers their image detail. Cropping and nondefault graphics values have automated coverage but still await an in-headset check. Keep the defaults for the established setup.

### VR HUD

The cockpit HUD is a transparent panel showing the game's race information. **VR HUD → Distance** changes its depth without shrinking the text. **Recenter** places the fixed HUD ahead of your seated position at that distance. Save and relaunch VR to apply. **Follow view** is off by default; Restore defaults also resets distance to 1 m. Pause menus still use their separate virtual screen. Desktop HUD capture and automated rendering checks pass, but headset placement and distance changes still need testing.

Under **Follow view**, uncheck the HUD areas you want hidden. These controls mask areas of the standard race HUD, so another overlay in the same area is hidden too. They do not change menus, the virtual screen, desktop play or the centre of the HUD. Save and relaunch to apply. Restore defaults hides the speedometer/gear/revs area and shows the other areas. Existing saved choices are preserved on upgrade. Alternative HUD layouts and headset use still need validation.

## Road sheen, shadows and texture filtering

**Road sheen (Experimental)** under **Graphics → Main** has separate Desktop and VR controls. Leave **Use track default** checked to preserve the installed track look. For Nordschleife and Misty Loch, uncheck it and choose 0–100%: 0% is matte, 10–25% gives a milder highlight, and 100% uses the full-strength Nordschleife reflection response. Misty Loch retains its mountain sun occlusion. Older Misty Loch builds need a one-time rebuild; later slider changes only require saving settings and relaunching. The selected track or replay must support this control. Other tracks, cars, glass and water are unchanged. Original track files are restored after play; Graphics → Restore defaults also returns both controls to Use track default.

**Shadow distance** under **Graphics → Main → Desktop** offers **Game** (default) and **160–640 m** in **80 m** steps. Longer ranges extend distant shadows while preserving the near shadow cascade. Overrides require DX11 and shadows enabled in the game. Start with **160 m**; higher distances can reduce distant sharpness and performance. Hover over the setting for a short explanation. Check **Also apply in VR** to use the same outer range in VR; it starts unchecked and requires **VR rendering → Shadows (experimental)** to be on. **Game** keeps the native outer range in both modes. VR **Detailed distance** still controls nearby detail separately. Save and relaunch to apply.

**Anisotropic filtering** under **Graphics → Main** has independent Desktop and VR choices: **Game**, **Off**, **2×**, **4×**, **8×** and **16×**. Game is the default and preserves native filtering. Higher levels aim to keep repeating road and scenery textures clearer at shallow angles; Off uses ordinary trilinear filtering for these surfaces. Point sampling, shadow-comparison sampling and clamped screen-space effects remain unchanged. Save and relaunch to apply. Overrides require DX11 and apply to menus, driving and replays in that launch mode, including VR's flat screen. No track rebuild or driver-profile change is needed. Appearance and performance still need in-game acceptance.

## Advanced graphics options

### 3D beyond the cockpit (Experimental)

**Advanced → Graphics → 3D beyond the cockpit** is off by default. Save and relaunch VR, then use the game's Change camera control to try supported bonnet, bumper and chase views with stereo depth and head tracking. Toggle VR still returns to the virtual screen. Desktop play is unchanged.

Trailer menus and replay 3D have separate switches below. Loading screens, dialogs and movies stay on the virtual screen. Seat adjustments apply only in the cockpit and are retained when switching cameras. Chase views retain the game's camera movement and may be less comfortable. Leaning can expose geometry omitted from the original fixed-camera view. The external views passed the user headset check. Some camera switches briefly return to the virtual screen while the game blends between views; this remains a known limitation.

### 3D menus (Experimental)

Enable **Advanced → Graphics → 3D menus**, save and relaunch VR for stereo depth and head tracking in the interactive trailer interior and exterior. It starts **off**, independently of the driving and replay 3D options. Navigate with the usual game controls; **Toggle VR** (F9 by default) returns to the flat virtual screen. Loading screens, movies, pause screens and unsupported views remain flat. Seat adjustments and racing HUD masks do not affect the trailer.

The game's menu camera animations still move the view; disable this option if uncomfortable. Interior and exterior menus, text readability and window reflections have been checked in VDXR. Returning from a race still needs acceptance.

**Paddock zoom (Experimental):** with 3D menus on, enable **Advanced → Graphics → Paddock zoom (wheel or stick)**, save and relaunch VR. In the paddock outside the RV, scroll the mouse wheel forward, or push the Xbox controller's right stick up, to move your view closer to your car, up to 3 m (25 cm per wheel notch; the stick moves faster the further you push it). Scroll back or pull the stick down to return. The view moves along the game's own camera, so you keep looking where the game's shot looks. It resets when the game cuts to another shot and when you go back into the RV. Desktop play and the flat virtual screen are not affected. The game window must be in front. It has automated checks only; a headset check, including whether DiRT 2 uses the mouse wheel or right stick for anything in the paddock, is still pending.

### Extended grass distance

**Advanced → Graphics → Extended grass distance** lets dense grass reach its normal distance instead of stopping abruptly a short way from the camera. It is on by default for desktop and VR and uses some extra memory. If you notice stuttering or crashes, turn it off, save and relaunch to use DiRT 2's original grass budget.

### FPS + GPU usage (Experimental)

**Advanced → Graphics → FPS + GPU usage (VR)** shows a small panel just below the centre of your view in the headset, updated once a second, in races, replays and menus. **FPS** counts the frames the game submits to the headset, not the headset's refresh rate or reprojected frames. **GPU** is the busiest engine on the graphics card the game uses, including other apps; **N/A** means Windows could not provide a reading. It starts off; save and relaunch VR to apply. Desktop play ignores it. The panel follows your head and does not change the game's picture or HUD. It has automated checks only; a headset check is still pending.

### Chase replays (Experimental)

Enable **Advanced → Graphics → 3D chase replays**, save and relaunch VR for stereo depth and head tracking in replay chase views. It starts off and works independently of **3D beyond the cockpit**. Cinematic replay angles stay on the flat screen; Toggle VR also returns to the flat screen.

For orbit and zoom on desktop or in VR, enable **Controls → General → Chase camera → Free look**. The same mouse activation, sensitivity and invert settings apply. Orbit and zoom work during playback and while the replay is paused. Pausing playback holds the angle rather than returning behind the car; opening the game menu or switching away suspends input.

Use the game's **replay camera next/previous** controls to choose a chase view or return to cinematic angles (default **F8/F7**, or Xbox **RB/LB**). These are the replay controls, not the driving Change camera action. Choose **Full Replay** after finishing Direct Practice or Race, or play a saved recording from **Launcher → Replays**. Only chase replay cameras support 3D; cinematic angles remain on the flat screen. See also [Controls → Replay controls](controls.md#replay-controls).
