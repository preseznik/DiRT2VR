# Custom tracks (experimental)

[← Back to README](../README.md)

See the [custom track catalogue](../CUSTOM_TRACKS.md) for source games, course descriptions, every available layout and special attributes. This guide explains how to build, install and play each pack.

Build tracks from your own installed source games. Conversion tools are included; game assets are not distributed. No SDK or separate .NET installation is required. Once installed, custom tracks work offline without their source game; keep it available for future rebuilds.

| Track pack | Required source game | Courses |
| --- | --- | --- |
| Mizu Mountain | GRID 2 | One 10.4 km daylight route |
| Nordschleife | Assetto Corsa, standard Nordschleife circuit | Full circuit in Daylight, Overcast and Evening |
| Spa-Francorchamps | GRID 2 with Spa-Francorchamps DLC | Full 7 km GP circuit, Direct practice and Race on desktop or in VR |
| Misty Loch | Assetto Corsa with Misty Loch 1.4 (`rt_misty_loch`) | Full 6.45 km circuit, Direct practice and Race on desktop or in VR |
| Aspen | DiRT 3 Complete Edition | Ten layouts |
| Smelter | DiRT 3 Complete Edition | Ten layouts |
| Pikes Peak | DiRT Rally | Full tarmac and gravel hill climb; four lighting/layout choices |

All seven packs offer **Direct practice** and experimental **Race** on desktop or in VR with stock cars. Custom cars follow their own mode limits. Custom-track LAN is unavailable.

## Choosing tracks

Use the **Source** dropdown above CUSTOM tracks to check one or more source games, or choose **All source games**. **Installed tracks only** hides uninstalled layouts and source games with no installed packs. It checks converted DiRT2VR tracks, not whether the original source game is still installed. Tracks needing repair remain visible. Filters keep your saved track choice; if it is hidden, select a visible track or choose **Show all tracks** before launching. **Save settings** remembers your filters for this installation.

## Mizu Mountain

Choose **CUSTOM tracks → Mizu Mountain → Build and install…**, select your GRID 2 game folder, then choose an installed car and **Direct practice** or **Race**. Use **Launch** for desktop or **Launch VR** with your selected runtime and headset ready. The full route is 10.4 km, with three timing checkpoints and a single run from start to finish. Race supports **1–7 AI opponents**, opponent-car choices and race difficulty.

Older Mizu packs need **Manage… → Rebuild from source…** to add the separated starting grid and the **MIZU** pre-race label. Current race packs need no rebuild to enable VR. Desktop practice and Race have passed player checks; headset rendering still needs testing. Some materials and lighting are approximations, movable props and soft vegetation do not have matching collision, and crowds and some animated effects are omitted.

## Nordschleife

After updating, use **Manage… → Rebuild from source…** to correct overly shiny asphalt in all three lighting presets. Updating the launcher alone does not regenerate installed track assets.

The **Nordschleife** pack uses your installed **Assetto Corsa** standard circuit. Select **CUSTOM tracks → Nordschleife → Build and install…**, then choose the Assetto Corsa folder containing `content\tracks\ks_nordschleife`. All three lighting presets are built together. Choose **Daylight**, **Overcast** or **Evening**, an installed car, and **Direct practice** or **Race**. Race supports **1–20 laps** and **1–7 AI opponents** on desktop or in VR, with car choices and difficulty settings. Direct practice uses one lap. Older solo packs need **Manage… → Rebuild from source…** to add the race grid; previous best laps are kept.

These are fixed dry lighting presets. Eight timing checkpoints divide the circuit into roughly 2.3 km sectors. **Best lap** shows your fastest completed Direct practice time and its car across all lighting presets, as on every other track; Race results appear in game. The stage progress marker follows your position around the circuit. One-lap sessions show checkpoint divisions; multi-lap races show lap divisions. Existing packs need no rebuild for the progress correction.

The full trackside scenery and distant landscape are included. A complete evening desktop practice lap and a short native VR session have been player-tested. Full-course headset performance and AI Race finishes still need testing; scenery detail and lighting remain in development. For VR, start your selected headset software, connect your headset and choose **Launch VR**. Existing packs need no rebuild to enable VR.

If source checking fails, choose the correct installation or verify its original files in Steam. Modified or unsupported source files are reported by name. If an installed file fails verification, choose **Manage… → Rebuild from source…**. The installer preserves external edits instead of overwriting them; move your modified files aside before retrying. After an interrupted game session, close DiRT 2 and choose **Restore original files** before playing again.

## Spa-Francorchamps

Choose **CUSTOM tracks → Source: GRID 2 → Spa-Francorchamps → Build and install…**, then select your GRID 2 installation with the Spa-Francorchamps DLC installed. The full GP circuit supports **Direct practice and Race** on desktop or in VR, with 1–20 laps, three timing sectors and replay cameras. Race supports 1–7 opponents, car choices and difficulty settings. Existing Spa packs need **Manage → Rebuild from source** once to add the separated starting grid for Race; VR practice needs no rebuild. VR and AI races are experimental and awaiting driving/headset acceptance. Track assets are built locally from your games and are not included in the installer.

## Misty Loch

**Updating an existing Misty Loch build:** choose **Manage… → Rebuild from source…** to apply smooth dry asphalt physics and reduce excessive road shaking. Daylight, Evening and Morning Fog are built together. Existing tracks remain usable until you rebuild. Older saved replays need their original track build because the road physics changed. The fix for flickering black road bands is preserved.

Choose **CUSTOM tracks → Misty Loch → Build and install…** and select the Assetto Corsa folder containing `content\tracks\rt_misty_loch`. Only your installed DiRT 2 files and Misty Loch 1.4 folder are used; no other track, extra grass atlas or CSP installation is required. Original game assets are not included in DiRT2VR downloads.

The optional **Build with high quality assets** checkbox improves material fidelity and adds denser, longer-distance grass for all three presets. It is off by default and requires rebuilding. It can use more GPU and memory; it preserves source texture resolution and your graphics settings. The launcher shows the installed quality separately. Uncheck it and rebuild to restore Standard. See [Misty Loch asset quality](../CUSTOM_TRACKS.md#misty-loch) for current validation status and limitations.

Choose **Daylight**, **Evening** or **Morning Fog**, an installed car and use **Launch** or **Launch VR**. **Direct practice** runs one complete lap; **Race** offers 1–7 AI opponents and 1–20 laps. If Misty Loch was built with an older launcher, use **Manage… → Rebuild from source…** to add the eight-car grid before racing. Existing builds still support Direct practice. VR and AI racing remain experimental. Morning Fog's corrected cockpit appearance has passed a headset check; the final HQ build still needs headset and repeated in-game restart checks. Longer sessions and wider car combinations also need testing. Custom-track LAN is unavailable. The port includes layered road/terrain textures, roadside grass and flowers, moving waterfall water, lake waves and gentle boat motion. Assetto Corsa lighting and some effects remain approximate; the boats do not follow their original sailing routes.

## Aspen and Smelter

Aspen and Smelter each add ten layouts built from your own **DiRT 3 Complete Edition** installation. Conversion tools are included with DiRT2VR; no additional download is needed and no game assets are distributed. You also need the supported original DiRT 2 files. No SDK or separate .NET installation is required.

1. Close DiRT 2. In **Launcher**, check **CUSTOM tracks (Experimental)**.
2. Choose a **Track pack**, then **Build and install…**. Select your detected DiRT 3 folder, paste its path, or use **Browse**. Choose the DiRT 3 Complete Edition game folder, wherever it is installed.
3. Check the **layouts to build**, using **Select all** or **Select none** as needed. Your choices are remembered for each location. Choose **Build and install** and leave the launcher open while it checks source files and converts only those layouts. Unchecked layouts already installed are kept. **Stop build**, beside the green progress message, stops checking or conversion safely and keeps installed tracks. Once final installation begins, Stop is disabled until it finishes.
4. Select a layout, **Direct practice** or **Race**, and any installed car. Choose **Launch** for desktop play, or **Launch VR** with your selected runtime and headset ready. Building does not start the game. Installation is performed once, before play; nothing is converted during game loading.

| Layout | Default lighting |
| --- | --- |
| Lakeside | Night |
| Lake View | Morning sun |
| Snowmass Sprint | Evening sun |
| Snowmass Loop | Overcast |

All twenty Aspen and Smelter layouts offer experimental **desktop and VR** play with any installed car. **Direct practice** is solo; **Race** adds AI opponents, using the same car, mixed cars or cars in your class. Rallycross and Landrush courses allow **1–7 AI opponents**. The four Head-to-head courses have two-car starting grids, so their Race mode allows **one AI opponent**. Race timing on these separate-lane courses still needs testing; DiRT 3's knockout Head-to-head rules are not included. Choose **Race difficulty** from Easy, Casual, Serious, Savage, Extreme or Hardcore, as in DiRT 2. **Use game setting** keeps your saved difficulty. Use the sliders to choose opponents and **1–20 laps**. Your normal opponent count is remembered when moving to and from a two-car course.

**No rebuild is needed to unlock these options on current installed layouts.** Very old Aspen installs may request an AI-path rebuild before racing; Direct practice remains available. Expanded AI racing, car combinations and headset rendering need gameplay validation. LAN is unavailable. Trackside effects and snow spray are included; ski-lift animation, full snowfall and deformable snow remain unsupported. Snow handling is an adaptation for DiRT 2, not an exact recreation of DiRT 3 physics.

Snowmass Sprint omits the decorative beams on the ski hillside where they flickered with viewing angle or distance. The towers, bright lamp faces and ground lighting remain, along with the other beams around the course. The other three layouts are unchanged by this workaround.

Each pack keeps its own layout, car, race and lap selections, separately from stock tracks. Turn **CUSTOM tracks** off to restore your original track, car and event selections. Installed custom layouts work **offline**, without their source game. Keep the source installation available if you want to rebuild or update a pack. Successful file checks are remembered while the launcher stays open, so switching locations does not repeat the scan. Installed-file changes trigger a new check. Use **Manage… → Verify installed files** to check again at any time. The **Manage…** menu also offers rebuilding and removal. Rebuild uses the same layout checkboxes: select just the courses you want to update or add. Choose Select all to update the whole location. The list shows which layouts are already installed, and the launcher reports the installed count. Conversion-tool updates arrive with DiRT2VR updates.

### Aspen Landrush and head-to-head layouts

Aspen includes these six courses alongside the four Rallycross layouts listed above. To add missing layouts or update existing ones, choose **Aspen → Manage… → Rebuild from source…** and select the courses you want. Other installed layouts remain available.

| Layouts | Conditions |
| --- | --- |
| Eagle Hill Rise, Eagle Hill Loop | Night |
| Brush Creek Sprint, Brush Creek Dash | Evening sun |
| Buttermilk Descent, Buttermilk Climb | Overcast snow lighting |

These courses offer the same modes and car selection as Rallycross, with the two-car limit on Buttermilk. Time of day follows source defaults, with reduced exposure on Buttermilk to retain snow detail; full snowfall remains unsupported. Broader racing and VR checks are still needed, including snow handling and night headlights.

**Buttermilk Climb and Descent use lower exposure by default** to retain detail in bright snow. Climb's improved snow detail has been confirmed in desktop testing; Descent still needs a visual check. The abrupt brightness border remains unresolved. **No track rebuild is needed** for installed layouts. Under **Snow lighting**, choose **Original exposure (reference)** or **Bloom off (diagnostic)** for comparisons; exit the game between profiles. Comparison choices reset to **Lower exposure (default)** when changing layout or reopening the launcher. Original post-processing files are restored after exit; interrupted sessions use the usual recovery, preserving external edits. Other tracks keep their existing exposure.

### Smelter — ten layouts

Select **Smelter** in CUSTOM tracks and choose **Build and install…** using your DiRT 3 Complete Edition folder. Select a layout, mode and car, then **Launch** or **Launch VR**. Dredger Duel and Furnace Duel allow one AI opponent; the other layouts allow up to seven.

| Layouts | Conditions |
| --- | --- |
| County Loop, Portage Canal | Morning sun |
| Houghton Sprint, Waterfront Park | Wet lighting, without active rain |
| Copper Run, Maple Woods | Evening sun |
| Atlantic Mill, Cole's Creek | Morning sun |
| Dredger Duel, Furnace Duel | Evening sun |

Use **Smelter → Manage… → Rebuild from source…** after updating to apply scenery updates and add missing layouts. Updating the launcher alone does not rebuild tracks. Existing tracks remain usable until the rebuild completes. General Maple Woods scenery flicker has improved in testing, but some log piles remain missing. Other layouts and performance still need wider testing.

Wet-weather handling, expanded AI races and VR still need gameplay validation. The four Head-to-head courses particularly need start, timing and finish checks with an opponent. The concrete-barrier compatibility test remains visually unverified.

## Pikes Peak

Build **Pikes Peak** from your own **DiRT Rally** installation: choose **CUSTOM tracks → Source: DiRT Rally → Pikes Peak → Build and install…**, select the source game folder, and check the layouts to build. Tarmac has Daylight lighting; gravel offers Daylight, Lower sun and Overcast. Installed layouts work offline; keep DiRT Rally available for rebuilds. The installer includes the converter, not the original game assets.

Pikes Peak supports **Direct practice**, **Rally** with up to three AI opponents, and **Grid race** with up to seven, on desktop or in native VR. Each session runs from start to finish once. Enable **Record full replays** under **Replays** for finish-menu and saved-replay playback. **Manage… → Rebuild from source…** updates selected layouts while keeping the others. LAN is unavailable; uneven crowd lighting remains a known issue. Distance blur is controlled by **Depth of field** in [Graphics](graphics.md#main-and-filters).

Existing prepared Pikes Peak packs remain under **Source → External** and retain their saved replays. Building from DiRT Rally creates a separate installation and does not replace them. See [prepared external packs](../EXTERNAL_PACKS.md).

## External track packs

Prepared external track packs go in `DiRT2VR/custom-tracks/external/<pack-id>` inside the game installation. Choose **External folders… → Open external tracks folder**, copy the complete pack, then **Refresh**. They appear under **Source → External**. Incomplete or incompatible packs stay visible with an explanation. These must be prepared DiRT2VR packs; copying raw tracks from another game does not convert them. See the [external pack guide](../EXTERNAL_PACKS.md).

External tracks support only the Direct practice, Race, VR and replay modes declared by their pack. They do not integrate with career or LAN. The launcher checks the payload before temporarily activating it, then restores the original files after the session. Keep the pack while playing. Close the game before replacing or removing it; launcher cache cleanup leaves external packs alone. Complete supported conversions keep their usual locations, categories and verification rules.

External cars are covered in [Playing → External cars](playing.md#external-cars).

## Moving built tracks or cars to another PC

Select the custom pack and choose **Manage → Export pack ZIP…** with the game closed. Track exports include all installed layouts. On the other PC, close DiRT 2 and the launcher, then extract the ZIP into the game folder containing `dirt2.exe`. Open an up-to-date launcher and Refresh. No rebuilding is needed; the destination must still meet the pack's compatibility requirements. Settings and careers are not included. See [pack portability](../EXTERNAL_PACKS.md#installing-and-sharing).

If a build fails, see [Troubleshooting → Custom track/car build failed](troubleshooting.md#troubleshooting).
