# Custom tracks

Add-on tracks available in DiRT2VR's Experimental builds, checked against **0.17.78**. For installation and launch instructions, see [Custom tracks in the README](README.md#custom-tracks-experimental).

Build each pack from your own installed source game using **CUSTOM tracks → Build and install…** in the launcher. Conversion tools are included; original game assets are not distributed. Installed tracks work offline without the source game, but you need it again to rebuild or update them.

## Track packs

| Name | Source game | Short description | Special attributes |
| --- | --- | --- | --- |
| [Aspen](#aspen) | DiRT 3 Complete Edition | Ten snowy circuits, with Rallycross, Landrush and head-to-head layouts. | Snow and ice; morning, evening, night and overcast lighting. Buttermilk layouts have two-car grids. |
| [Smelter](#smelter) | DiRT 3 Complete Edition | Ten circuits around an industrial and wooded setting, with Rallycross, Landrush and head-to-head layouts. | Dry and wet lighting; morning and evening variants. Duel layouts have two-car grids. |
| [Nordschleife](#nordschleife) | Assetto Corsa — standard Nordschleife (`ks_nordschleife`) | The full 20.7 km circuit, with trackside scenery and distant landscape. | Three dry lighting presets: Daylight, Overcast and Evening. Eight timing checkpoints and a shared best practice lap across presets. |
| [Mizu Mountain](#mizu-mountain) | GRID 2 | A full 10.4 km mountain route from start to finish. | Daylight; point-to-point rather than laps; three timing checkpoints. |
| [Misty Loch](#misty-loch) | Assetto Corsa with the Misty Loch 1.4 add-on (`rt_misty_loch`) | A full 6.45 km lakeside road circuit. | Daylight; animated waterfall, lake waves and gently moving boats. One-lap desktop Direct practice only. |

## Play modes

**Aspen, Smelter, Nordschleife and Mizu Mountain** offer solo **Direct practice** and experimental **Race**, on desktop or in VR, with any installed car. Race allows **1–7 AI opponents**, except the four head-to-head layouts, which allow **one AI opponent**. Circuit races support **1–20 laps**; Mizu is always one complete run. Nordschleife Direct practice is one lap.

**Misty Loch** offers one-lap desktop Direct practice with any installed car. VR and AI races are unavailable for this pack.

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

A complete Evening desktop practice lap and a short VR session have been player-tested. Full-course headset performance and AI Race finishes still need testing. Scenery detail and lighting remain in development.

## Mizu Mountain

Source: **GRID 2**. Desktop practice and Race have passed player checks; headset rendering still needs testing.

Some materials and lighting are approximations. Movable props and soft vegetation do not have matching collision; crowds and some animated effects are omitted.

## Misty Loch

Source: **Assetto Corsa with Misty Loch 1.4 installed** in `content/tracks/rt_misty_loch`. No other track, extra grass texture pack or CSP installation is needed.

The port includes layered road and terrain textures, roadside grass and flowers, moving waterfall water, lake waves and gentle boat motion. Lighting and some source effects remain approximate; boats do not follow their original sailing routes.

For current setup advice and known limitations, see the [README](README.md#custom-tracks-experimental). Report track problems through [GitHub issues](https://github.com/preseznik/DiRT2VR/issues), including the layout, car, play mode and whether you used desktop or VR.
