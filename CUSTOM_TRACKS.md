# Custom tracks

Add-on tracks available in DiRT2VR's Experimental builds. For installation and launch instructions, see [Custom tracks in the README](README.md#custom-tracks-experimental).

Build each pack from your own installed source game using **CUSTOM tracks → Build and install…** in the launcher. Conversion tools are included; original game assets are not distributed. Installed tracks work offline without the source game, but you need it again to rebuild or update them.

## Track packs

| Name | Source game | Short description | Special attributes |
| --- | --- | --- | --- |
| [Aspen](#aspen) | DiRT 3 Complete Edition | Ten snowy circuits, with Rallycross, Landrush and head-to-head layouts. | Snow and ice; morning, evening, night and overcast lighting. Buttermilk layouts have two-car grids. |
| [Smelter](#smelter) | DiRT 3 Complete Edition | Ten circuits around an industrial and wooded setting, with Rallycross, Landrush and head-to-head layouts. | Dry and wet lighting; morning and evening variants. Duel layouts have two-car grids. |
| [Nordschleife](#nordschleife) | Assetto Corsa — standard Nordschleife (`ks_nordschleife`) | The full 20.7 km circuit, with trackside scenery and distant landscape. | Three dry lighting presets: Daylight, Overcast and Evening. Eight timing checkpoints and a shared best practice lap across presets. |
| [Mizu Mountain](#mizu-mountain) | GRID 2 | A full 10.4 km mountain route from start to finish. | Daylight; point-to-point rather than laps; three timing checkpoints. |
| [Spa-Francorchamps](#spa-francorchamps) | GRID 2 with Spa-Francorchamps DLC | The full 7 km Grand Prix circuit. | Three timing sectors, replay cameras and the original eight starting-grid positions. Direct practice and Race on desktop or in VR. |
| [Misty Loch](#misty-loch) | Assetto Corsa with the Misty Loch 1.4 add-on (`rt_misty_loch`) | A full 6.45 km lakeside road circuit. | Daylight, Evening and Morning Fog; animated waterfall, lake waves and gently moving boats. One-lap Direct practice and multi-lap Race, on desktop or in VR. |
| [Pikes Peak](#pikes-peak) | DiRT Rally | Full 19.5 km mountain hill climb, in tarmac and vintage gravel layouts. | Daylight tarmac; Daylight, Lower sun and Overcast gravel. Direct practice, staggered Rally and Grid race; summit introductions and Full Replay. |

## Play modes

**All seven packs** offer solo **Direct practice** and experimental **Race**, on desktop or in VR, with installed stock cars. Race allows **1–7 AI opponents**, except the four head-to-head layouts, which allow **one AI opponent**, and Pikes Peak staggered Rally, which allows **up to three**. Circuit races support **1–20 laps**; Mizu and Pikes Peak are always one complete run. Nordschleife and Misty Loch Direct practice run for one lap.

Custom cars follow their own mode limits: Ferrari F2004 currently supports solo Direct practice only. Prepared community tracks appear under **Source → External** in the launcher; their available modes depend on the pack.

Older **Misty Loch** builds need **Manage… → Rebuild from source…** before racing, to add the eight-car starting grid. Existing builds remain usable for Direct practice, including VR.

**LAN is unavailable for all custom tracks.** Broader AI racing, car combinations and headset use still need player testing. Availability in the launcher does not mean every combination has been tested.

## Aspen

Source: **DiRT 3 Complete Edition**. All ten layouts use snow and ice, with handling adapted for DiRT 2.

| Layout | Course type | Default lighting / special attributes |
| --- | --- | --- |
| Lakeside | Rallycross circuit | Night |
| Lake View | Rallycross circuit | Morning sun |
| Snowmass Sprint | Rallycross circuit | Evening sun |
| Snowmass Loop | Rallycross circuit | Overcast |
| Eagle Hill Rise | Landrush circuit | Night |
| Eagle Hill Loop | Landrush circuit | Night |
| Brush Creek Sprint | Landrush circuit | Evening sun |
| Brush Creek Dash | Landrush circuit | Evening sun |
| Buttermilk Descent | Head-to-head, separate lanes | Overcast; two-car grid; reduced exposure to preserve snow detail |
| Buttermilk Climb | Head-to-head, separate lanes | Overcast; two-car grid; reduced exposure to preserve snow detail |

Snow spray and trackside effects are included. Full snowfall, deformable snow and ski-lift animation are unavailable. Snow handling differs from DiRT 3. Buttermilk still has an abrupt brightness border. Snowmass Sprint omits some decorative hillside light beams to avoid flickering.

## Smelter

Source: **DiRT 3 Complete Edition**.

| Layout | Course type | Default lighting / special attributes |
| --- | --- | --- |
| County Loop | Rallycross circuit | Morning sun |
| Portage Canal | Rallycross circuit | Morning sun |
| Houghton Sprint | Rallycross circuit | Wet lighting; no active rain |
| Waterfront Park | Rallycross circuit | Wet lighting; no active rain |
| Copper Run | Landrush circuit | Evening sun |
| Maple Woods | Landrush circuit | Evening sun |
| Atlantic Mill | Landrush circuit | Morning sun |
| Cole's Creek | Landrush circuit | Morning sun |
| Dredger Duel | Head-to-head, separate lanes | Evening sun; two-car grid |
| Furnace Duel | Head-to-head, separate lanes | Evening sun; two-car grid |

Some log piles remain missing at Maple Woods. Wet-weather handling and broader racing and VR performance still need testing.

The Aspen Buttermilk and Smelter Duel layouts run as lap races on separate lanes. DiRT 3's knockout head-to-head rules are unavailable; starts, timing and finishes with an opponent still need testing.

## Nordschleife

Source: **Assetto Corsa**, the standard circuit in `content/tracks/ks_nordschleife`. Other Assetto Corsa Nordschleife configurations are not included. All three lighting presets are built together.

**Build with high quality assets** is off by default. It adds source material lighting, RGB normal maps on supported materials, layered ground detail and scenery shadows while retaining the authored vegetation density. If a supported local CSP ambient patch is installed, its baked ambient shading is imported too; CSP is optional. Higher GPU and memory use is expected. Uncheck the option and rebuild to return to Standard. Course identities and lap records are retained. The player approved the 0.17.114 HQ test build.

Earlier Standard testing includes a complete Evening desktop practice lap and a short VR session. Broader headset coverage and AI Race finishes still need testing. Scenery detail and lighting remain in development.

## Mizu Mountain

Source: **GRID 2**. Desktop practice and Race have passed player checks; headset rendering still needs testing.

Some materials and lighting are approximations. Movable props and soft vegetation do not have matching collision; crowds and some animated effects are omitted.

## Spa-Francorchamps

Source: **GRID 2 with Spa-Francorchamps DLC installed**. The full GP circuit includes three timing sectors, replay cameras and desktop/VR Direct practice and Race. Race supports 1–7 AI opponents and 1–20 laps.

Use **Manage → Rebuild from source** after updating to apply the original GRID 2 starting-grid positions and improved AI corner braking. The reported Evo X Trailblazer cornering issue passed a player retest. The restored race start, other car combinations and full VR races still need wider testing.

## Misty Loch

Source: **Assetto Corsa with Misty Loch 1.4 installed** in `content/tracks/rt_misty_loch`. No other track, extra grass texture pack or CSP installation is needed.

Choose **Daylight**, **Evening** or **Morning Fog** under Conditions. Use **Manage… → Rebuild from source…** to add all three presets and subtle road highlights with mountain occlusion. Existing Daylight and Evening remain usable before rebuilding. The VR cockpit fog exposure fix has passed a player retest.

The port includes layered road and terrain textures, roadside grass and flowers, moving waterfall water, lake waves and gentle boat motion. Lighting and some source effects remain approximate; boats do not follow their original sailing routes.

Misty Loch's build dialog offers **Build with high quality assets**, off by default. It keeps the source texture resolutions and adds original RGB normal maps, material channels, baked ambient shading and authored material lighting strengths, plus denser grass with a longer draw distance. The same selection applies to all three lighting presets. This requires rebuilding and can increase GPU and memory use, particularly in VR. The installed quality is shown separately; uncheck the option and rebuild to return to Standard. Changing quality within the same track version preserves course identities, lap records and replay compatibility.

Current builds use smooth dry asphalt for roads and pit areas to reduce excessive road shaking. Rebuild an existing track to apply this physics change. Older saved replays require their original track build.

Both quality levels support **Road sheen (Experimental)** under **Graphics → Main**, with separate Desktop and VR settings. Rebuilt Misty Loch tracks use **10%** sheen by default in all three lighting conditions. Leave **Use track default** checked for that setting, or select 0–100%; both use the same reflection response as Nordschleife. Older builds need a rebuild to receive the new default. Mountain sun occlusion remains active at every setting. Slider changes apply on the next launch and the original files are restored after play.

Both quality levels use only DiRT 2 and Misty Loch assets. HQ is not an upscaled texture pack. DiRT 2's 32-bit process memory remains a limit; the option does not change your graphics settings or Large Address Aware preference. Desktop appearance is accepted; final HQ headset and repeated in-game restart checks remain pending.

For current setup advice and known limitations, see the [README](README.md#custom-tracks-experimental). Report track problems through [GitHub issues](https://github.com/preseznik/DiRT2VR/issues), including the layout, car, play mode and whether you used desktop or VR.

## Pikes Peak

Source: **DiRT Rally**. Choose **Source → DiRT Rally → Pikes Peak → Build and install…**, select the game folder and check the layouts you want. Conversion preserves the source scenery, detailed textures, shadow maps and lighting presets, with the Pikes Peak compatibility corrections. Tree trunks and guardrails have collision; traffic signs and hay bales can be knocked away.

| Layout | Condition |
| --- | --- |
| Tarmac · full course | Daylight |
| Gravel · full course | Daylight |
| Gravel · full course | Lower sun |
| Gravel · full course | Overcast |

Overcast changes the lighting; it does not add rain or a wet-surface simulation. **Direct practice**, staggered **Rally** (up to three opponents) and **Grid race** (up to seven opponents) work on desktop or in VR. Each race is one complete climb. Full Replay uses cameras along the course. The Depth of field checkbox remains available under **Graphics → Main → Supported track effects**. Uneven crowd lighting remains a known issue.

If conversion reports a missing DirectX compiler, install or repair the **DirectX End-User Runtimes (June 2010)** and rebuild.

Use **Manage… → Rebuild from source…** to add or update individual layouts. The source installation can be removed after conversion, but is needed again for rebuilds. Existing prepared Pikes packs and their replays remain separate under **Source → External**.

**Build performance version** is optional and starts unchecked. It keeps every tree and nearby scenery detail, uses simpler source trees in the distance, and limits eligible scenery textures to 2048 pixels. Roads, markings, readable signs, collision and mountain visibility are preserved. The option applies to the checked layouts on desktop and in VR; changing it requires rebuilding those layouts. Unchecked installed layouts keep their current assets. Each installed layout is labelled **Standard** or **Performance**.

Desktop and Virtual Desktop VR checks improved performance. A short tarmac forest drive and head turns passed appearance and tracking checks. Broader driving and gravel headset use still need testing; intermittent VR startup hangs remain unresolved.
