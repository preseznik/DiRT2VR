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

Each `Session` entry pairs a game `Target` with a prepared `File` under `payload/session/`. The file must appear in `Files`, and its original target hash must appear in `Dependencies`. `database/database.bin` registration is required. Only these shared targets are currently permitted:

- `database/database.bin`, `surface_materials.xml`
- `language/language_eng.lng`, `language/language_fre.lng`, `language/language_ger.lng`, `language/language_ita.lng`, `language/language_jpn.lng`, `language/language_pol.lng`, `language/language_rus.lng`, `language/language_spa.lng`, `language/language_use.lng`
- `effects/pfx_kickup_data_set.xml`, `effects/pfx_pssg_dataset.xml`
- `tracks/light_definitions.xml`, `tracks/waterdefs.xml`, `tracks/ornament_system_settings.xml`
- `dx11/shaderpack/shaderpack.pssg`, `dx11/shaderpack/postprocess.pssg`, `postprocess/effects.xml`

All other game destinations are blocked. Shared files are accepted only when their original hashes match. Existing files in an external track's destination are never overwritten. Two packs claiming the same folder are both blocked.

Bounds: 256 folders per content kind, 32 layouts per pack, 4,096 payload files, 512 dependencies, 768 MiB per payload file, 64 MiB per shared replacement and 4 GiB total payload. Manifests are limited to 2 MiB. Paths must be relative, use `/`, and cannot contain traversal, device names, links or alternate data streams. Only recognised game-data extensions are accepted; executables and scripts are rejected.

The prepared `pikes-peak-lab` pack retains its existing identity and has larger scenery limits: 1.5 GiB per payload file and 12 GiB total. Its four supported layouts are desktop-only; the launcher supports their authored practice, staggered and grid starts. Version **1.0.7** adds Full Replay recording and playback. Older Pikes packs need the updated pack as well as a current launcher. The pack is installed separately from the DiRT2VR installer. Its collision archive must match a supported Pikes build. Uneven crowd lighting remains a known issue.

## Car manifest, schema 1

Use `Kind: "car"`, an empty `Layouts` array and a `Car` object containing `Code`, `Donor` and `ClassName`. See [the car template](examples/external-car.manifest.json). Its placeholders must be replaced before use.

`Code` is a unique three-character lowercase letter/digit code. Stock codes and `f04` are reserved even when not installed. `Donor` must be a supported stock car code. `ClassName` is a display label; the actual class, upgrade record structure and audio are inherited from the donor. Packs cannot supply executable registration code or replace existing cars.

Files are confined to `payload/cars/<code>/` and `payload/interiors/<code>/`. Include all resources needed by the car, with internal names and references prepared for the chosen code. At minimum: `cameras.xml`, `<code>.ctf`, `<code>.nd2`, `<code>_highLOD.pssg`, `<code>_lowLOD.pssg`, `int_<code>.pssg` and `int_<code>.xml` in the appropriate folders. Include default-livery textures, animations and every handling file referenced by the donor's upgrade records. Renaming a raw car folder is not a conversion.

Pin these original files in `Dependencies`: `database/database.bin`, `cars/class_mappings.xml`, `audio/carsetup.bin`, `audio/carsetup_ai.bin` and `audio/dictionaries.bin`. Include any further donor assets the prepared payload depends on. Registration adds new car records without changing existing records. It composes with the selected track database; incompatible source versions are rejected. The first format reuses donor audio and does not accept custom sound-bank registration.

Enable only modes tested by the pack author. The launcher requires both the track and car to support a mode. Career and LAN are unavailable. Declaring `Replay: true` requires full-race recording/playback compatibility; saved recordings pin the car pack's identity, version and exact manifest hash. Close the game, restore interrupted sessions, then replace a pack and Refresh. Invalid and missing selections remain visible rather than silently reverting to another car.

Supported Ferrari imports retain their separate accepted restrictions. An external manifest cannot bypass them or claim the Ferrari's code.
