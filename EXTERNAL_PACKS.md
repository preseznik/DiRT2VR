# Prepared external packs (experimental)

This experimental format accepts prepared DiRT2VR packs. It does not add automatic conversion from other games. Track packs must already contain working DiRT 2 assets and registration data. Car packs supply prepared assets and use an installed stock donor for class and audio registration. Desktop integration has been checked with local test packs; each pack still needs its own driving, replay and VR checks before its author enables those modes.

## Installing and sharing

To move an installed track or car, select it and choose **Manage → Export pack ZIP…**. Close DiRT 2 first and restore original files if recovery is pending. The export verifies every listed file and includes the receipt or manifest unchanged. Track exports contain all currently installed layouts of the selected pack. Progress and Cancel are available; a failed or cancelled export leaves any existing ZIP untouched.

On the destination PC, close the game and launcher and extract the ZIP into the **DiRT 2 game folder containing `dirt2.exe`**. Keep the archive's folder structure; do not extract it into `DiRT2VR` or into an external folder manually. Open an up-to-date launcher and Refresh. Supported conversions retain their normal source categories; external packs remain External. The destination needs a compatible DiRT 2 installation, and existing mode/version/dependency checks still apply. No source-game rebuild is needed. Settings, careers, replays, source assets and unrelated packs are not exported. Only share assets you are entitled to distribute.

Put one complete pack in each folder:

```text
DiRT2VR/custom-tracks/external/<pack-id>/manifest.json
DiRT2VR/custom-tracks/external/<pack-id>/payload/...
DiRT2VR/custom-cars/external/<pack-id>/manifest.json
DiRT2VR/custom-cars/external/<pack-id>/payload/...
```

Use **CUSTOM tracks → External folders…** to open either location. **Refresh** rescans them; restarting the launcher also discovers packs. A valid track appears under **External**. The launcher keeps damaged packs visible and blocks their launch. It never executes pack scripts. Only share assets you are entitled to distribute.

Keep supported conversions in their existing locations. Copying a supported conversion requires its complete installed track files and its corresponding `DiRT2VR/custom-tracks/<supported-id>` folder, including the receipt. Copying only a receipt is insufficient. Existing version and compatibility checks still apply. Do not place supported receipts in an external folder or reuse their identities for an external pack.

The original game files are restored after each external session. Packs are not deleted by cache cleanup. Close the game before replacing or removing a pack. If a session was interrupted, choose **Restore original files** first. Recovery does not need the external pack to remain present.

## Track manifest, schema 1

The manifest is UTF-8 JSON with the exact property names below; unknown fields are rejected. See [the template](examples/external-track.manifest.json). It deliberately contains placeholders and cannot be launched unchanged.

| Field | Meaning |
|---|---|
| `Schema` | `1` |
| `Kind` | `track` |
| `Id` | Same as the folder name: lowercase letters, digits and hyphens, at most 48 characters |
| `Name`, `Author`, `Description` | Display text; no control characters |
| `Version` | Numeric `major.minor.patch`; change it when pack content changes |
| `MinimumLauncher` | First compatible launcher version; do not target an older build that lacks this format |
| `Modes` | `desktop-solo`, optionally `desktop-race`, `vr-solo`, `vr-race`; a VR mode also requires its corresponding desktop mode |
| `Replay` | Whether the pack supports full-race recording and saved playback; otherwise turn full-race recording off before launch |
| `Files` | Complete payload inventory: relative `Path`, exact `Bytes`, SHA-256 `Sha256` |
| `Dependencies` | Required original game files: relative `Path` and SHA-256 `Sha256` |
| `Layouts` | Prepared layouts and their session replacements, described below |
| `Car` | `null` for tracks |

The launcher ID is `external-track-<Id>/<layout-id>`. Saved selections retain this identity. Saved replay playback requires the same pack version and exact manifest contents used during recording. Keep an unchanged copy of a pack alongside any replays that depend on it.

Each layout supplies `Id`, `Name`, `Folder`, `Condition`, `Discipline`, `Circuit`, `MaximumOpponents` and `Session`. Its destination is `tracks/usa/<Folder>/route_0`; `Folder` must begin `d2ve_`, followed by lowercase letters, digits or underscores. Every folder must be unique across all external packs. The prepared database must register that exact folder. Country is currently `usa` and route is `route_0`.

Optional `RaceStart` defaults to `grid`. Use `staggered` for a point-to-point Rally or Trailblazer layout with `desktop-race` support and authored individual starts. Supply `ai_track.xml`, `dev_ai_track.xml` and `ai_vehicle_track.xml` in the route. Its `RaceSession` must contain exactly four replacements for its own route: `grids.pssg`, `progress_track.xml`, `ai_track.xml` and `dev_ai_track.xml`, each backed by an inventoried file under `payload/session/<layout-id>/race/`. These replacements apply only in Race; Direct practice uses the normal route files. The launcher labels this mode **Rally (staggered)**.

A staggered layout can additionally offer **Grid race** by supplying `GridMaximumOpponents` (1–7) and `GridRaceSession`. The latter contains the same four route targets, with separate inventoried files under `payload/session/<layout-id>/grid-race/`. Grid race starts everyone together and uses this separate placement, timing and guidance. `MaximumOpponents` remains the Rally limit. Existing saved races retain the default format; Direct practice retains its original route files.

Disciplines are `Rally`, `Rallycross`, `Landrush`, `Trailblazer`, `Head-to-head` or `Point-to-point`. This label does not create native rules or AI paths. Point-to-point layouts use one complete run. A Race-capable pack must provide a working starting grid and AI route, with 1–7 opponents declared per layout. A Practice-only layout may declare zero opponents. LAN and career are outside this format.

Payload files for a layout live under `payload/tracks/usa/<Folder>/`. At minimum its `route_0` contains `track.jpk`, `routesplit.pssg`, `track.vis`, `grids.pssg` and `progress_track.xml`. Include every other resource the prepared track needs; these five files alone do not establish playability. `route_0.clm` is optional when the track does not use a baked shadow map.

**Borrowed stock files (optional, launcher 0.18.21 or later).** A layout that uses an original venue's resources (textures, trees, sky, objects, effects settings) can list them in `Borrow` instead of shipping copies: each entry's `Target` is a file in the layout's own `tracks/usa/<Folder>/` tree that the pack does not ship, and its `Source` is an original game file listed in `Dependencies`, for example `{ "Target": "tracks/usa/d2ve_example/land.pssg", "Source": "tracks/croatia/croatia_rally/land.pssg" }`. For each session the launcher copies the player's own file into place after checking its pinned hash, and removes it again with the rest of the track. Sources cannot be session targets or other external tracks' files; empty original files may be borrowed. At most 256 entries per layout. Set `MinimumLauncher` to 0.18.21 or later; older launchers reject the field.

**Spliced ranges (optional, launcher 0.18.21 or later).** When one of the pack's own files embeds a block copied from an original file (for example a `grass.grs` whose grass type library is the venue's), ship that block as zeros and list it in `Splice`: `{ "Target": "tracks/usa/d2ve_example/grass.grs", "Source": "tracks/croatia/croatia_rally/grass.grs", "Offset": 16, "Length": 3680 }`. For each session the launcher copies bytes `Offset` to `Offset + Length` of the player's own pinned `Source` over the same range of the packed `Target`. The target must be a packed file in the layout's folder, the range must lie inside it, ranges cannot overlap, a range is at most 64 KiB and a layout has at most 16.

**Lighting conditions (optional, launcher 0.18.29 or later).** A layout that borrows its venue's `lighting.xml` (a `Borrow` entry with `Target` `tracks/usa/<Folder>/lighting.xml`) can offer several times of day or weathers from one installation. List them in `Conditions`, two to eight entries, each with an `Id` (lowercase letters, digits and hyphens), a `Name` shown in the launcher, and optional `Lighting` values: `{ "Id": "evening", "Name": "Evening", "Lighting": { "heightangle": "10", "backlight_suncolour": "1.15 0.72 0.42" } }`. Each value replaces one parameter of the player's own borrowed `lighting.xml` for the session (one to four plain numbers; at most 64 values per condition); every other parameter keeps the venue's value, and the session stops if the file has no such parameter. A condition can also take some of the layout's borrowed files from other original files in `Borrow`, for example a cloudy sky: `{ "Target": "tracks/usa/d2ve_example/sky.pssg", "Source": "tracks/malaysia/malaysia_rally/sky.pssg" }`. Each `Target` must be one of the layout's own `Borrow` targets, each `Source` an original file listed in `Dependencies` with the same extension, and a condition with `Lighting` values cannot also swap `lighting.xml`; at most 16 per condition. The first condition is listed under the layout's own id and usually has no values, so it shows the venue's lighting. Each other condition is listed as its own layout, `<layout id>-<condition id>`, named "<layout name> · <condition name>", with its own best times and replays, over the same installed files. Those ids must stay within 48 characters and not repeat another layout's. Set `MinimumLauncher` to 0.18.29 or later; older launchers reject the field.

Each `Session` entry pairs a game `Target` with a prepared `File` under `payload/session/`. The file must appear in `Files`, and its original target hash must appear in `Dependencies`. `database/database.bin` registration is required. The launcher shows a world record for the selected car's class when this database has a `world_record` row for the layout's track model and that class; without rows it shows none. Only these shared targets are currently permitted:

- `database/database.bin`, `surface_materials.xml`
- `language/language_eng.lng`, `language/language_fre.lng`, `language/language_ger.lng`, `language/language_ita.lng`, `language/language_jpn.lng`, `language/language_pol.lng`, `language/language_rus.lng`, `language/language_spa.lng`, `language/language_use.lng`
- `effects/pfx_kickup_data_set.xml`, `effects/pfx_pssg_dataset.xml`
- `tracks/light_definitions.xml`, `tracks/waterdefs.xml`, `tracks/ornament_system_settings.xml`, `tracks/tree_system_settings.xml`
- `dx11/shaderpack/shaderpack.pssg`, `dx11/shaderpack/postprocess.pssg`, `postprocess/effects.xml`

**Launch-time registration (optional, launcher 0.18.21 or later).** Instead of shipping modified copies of `database/database.bin`, the nine `language_*.lng` files and `tracks/tree_system_settings.xml`, a layout can give a `Registration` object; the launcher then makes the same additions to the player's own files for the session and restores them afterwards. Fields: `LocationDonor`, `TrackDonor` and `ModelDonor` (stock row ids to clone: location, track and track model), `LocationKey`/`ModelKey` (string ids, lowercase letters, digits and underscores, up to 24 characters; added as `db_<key>` to every language file) with `LocationName`/`ModelName` (display text, up to 60 characters), `Nationality`, `Length` (metres, 100–100,000), `RecordScale` (the donor model's world records and AI split times are multiplied by it, 0.01–100), and optionally `TreeStyle` with `TreeBase` (a stock style to copy, normally `test`), `TreeInstances` and `TreeBillboards` (1–12,000). The new track model uses the layout's `Folder`, `usa` and `route_0`. Every registered target must be pinned in `Dependencies` and must not also appear in `Session`; a registered layout may leave `Session` empty. External cars compose with the registered database as they do with a shipped one.

The two `*_system_settings.xml` files hold the tree and trackside-object draw budgets by named style. A pack with more trees or objects than the stock styles allow can add its own style to a session copy and select it in its route's `route_overrides.xml`; the launcher restores the original file after the session.

All other game destinations are blocked. Shared files are accepted only when their original hashes match. Existing files in an external track's destination are never overwritten. Two packs claiming the same folder are both blocked.

Bounds: 256 folders per content kind, 32 layouts per pack, 4,096 payload files, 512 dependencies, 768 MiB per payload file, 64 MiB per shared replacement and 4 GiB total payload. Manifests are limited to 2 MiB. Paths must be relative, use `/`, and cannot contain traversal, device names, links or alternate data streams. Only recognised game-data extensions are accepted; executables and scripts are rejected.

The prepared `pikes-peak-lab` pack retains its existing identity and has larger scenery limits: 1.5 GiB per payload file and 12 GiB total. Version **1.0.8** enables desktop and native VR on all four layouts, including their authored practice, staggered and grid starts. Version **1.0.7** adds Full Replay recording and playback. Older Pikes packs need the updated pack as well as a current launcher. The prepared pack is installed separately from the DiRT2VR installer. For a new installation, use the bundled converter under **CUSTOM tracks → Source: DiRT Rally → Pikes Peak → Build and install…** with your own source game. This creates a separate managed pack and preserves existing external-pack replays. The prepared pack's collision archive must match a supported Pikes build. Uneven crowd lighting remains a known issue.

## Car manifest, schema 1

Use `Kind: "car"`, an empty `Layouts` array and a `Car` object containing `Code`, `Donor` and `ClassName`. See [the car template](examples/external-car.manifest.json). Its placeholders must be replaced before use.

`Code` is a unique three-character lowercase letter/digit code. Stock codes and `f04` are reserved even when not installed. `Donor` must be a supported stock car code. `ClassName` is a display label; the actual class and upgrade record structure are inherited from the donor, and so is the audio unless the pack brings its own engine sound (below). Packs cannot supply executable registration code or replace existing cars.

Files are confined to `payload/cars/<code>/` and `payload/interiors/<code>/`, plus the optional engine sound files below. Include all resources needed by the car, with internal names and references prepared for the chosen code. At minimum: `cameras.xml`, `<code>.ctf`, `<code>.nd2`, `<code>_highLOD.pssg`, `<code>_lowLOD.pssg`, `int_<code>.pssg` and `int_<code>.xml` in the appropriate folders. Include default-livery textures, animations and every handling file referenced by the donor's upgrade records. Renaming a raw car folder is not a conversion.

Pin these original files in `Dependencies`: `database/database.bin`, `cars/class_mappings.xml`, `audio/carsetup.bin`, `audio/carsetup_ai.bin` and `audio/dictionaries.bin`. Include any further donor assets the prepared payload depends on. Registration adds new car records without changing existing records. It composes with the selected track database; incompatible source versions are rejected.

**Own engine sound (optional).** Add `"Audio": { "Bank": "eng_<code>", "Samples": <frames>, "Turbo": false }` to `Car` and ship DiRT 2 ramp-engine audio: `payload/audio/dictionaries/d2ve_<code>/NESSamples.dic` with the bank `payload/audio/dictionaries/d2ve_<code>/eng_<code>.wip`, and the engine description `payload/audio/cars/<code>engine.ini`. The bank holds the four 16-bit mono ramps `up_en`, `up_ex` (full throttle) and `dn_en`, `dn_ex` (off throttle), each `Samples` frames long; the INI's `[Idle]`/`[Throttle]` sections give their length and rev/time tables. For the player's car the launcher registers the dictionary and points the car's engine at this bank and INI instead of the donor's; `Turbo: false` silences the donor's turbo sounds. Gear, mechanical, exhaust and AI sounds stay the donor's. Packs with `Audio` need a launcher that supports it; older launchers report the manifest as invalid.

The game plays the ramps through its legacy engine controller, which DiRT2VR selects for that car: it picks the position in the ramp for the current revs from the INI's tables. Build each ramp with its pitch rising in proportion to the revs along it, as a recorded engine run-up does; for a synthesized four-cylinder four-stroke sweep, keep the firing frequency at rpm / 30. Use `RevProp=1.0` in each `[Loop]` section, as the GoRacer Kestrel and F2004 ramps do. Launchers up to 0.18.22 play the ramps without following the revs.

**Several liveries (optional, launcher 0.18.25 or later).** Add `"Liveries": ["Club Blue", "Club Red", "Heritage Green"]` to `Car`: 2–16 display names in order, each 1–32 characters, unique ignoring case, without control characters or leading and trailing spaces. Entry *k*, counting from 0, is the folder `payload/cars/<code>/livery_<kk>/`, laid out like a stock car's: `textures_high/<code>_tex_high_<kk>.pssg` and `textures_low/<code>_tex_low_<kk>.pssg`, both listed in `Files`. For each session the launcher registers one livery per entry for the car, each copied from the donor's default livery. `livery_00` stays the car's default, which Direct starts give every car, AI opponents included; the player's **Livery** picker offers the others by these names, and saved replays keep the livery they were recorded with. Without `Liveries` the car has the one livery `livery_00`. Set `MinimumLauncher` to 0.18.25 or later; older launchers report the manifest as invalid.

Enable only modes tested by the pack author. The launcher requires both the track and car to support a mode. Career and LAN are unavailable. Declaring `Replay: true` requires full-race recording/playback compatibility; saved recordings pin the car pack's identity, version and exact manifest hash. Close the game, restore interrupted sessions, then replace a pack and Refresh. Invalid and missing selections remain visible rather than silently reverting to another car.

Supported Ferrari imports retain their separate accepted restrictions. An external manifest cannot bypass them or claim the Ferrari's code.
