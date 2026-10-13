# Prepared external packs (experimental)

This format is being validated for a future experimental build. It does not add automatic conversion from other games. Track packs must already contain working DiRT 2 assets and registration data. External car activation is not yet available.

## Installing and sharing

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

Disciplines are `Rally`, `Rallycross`, `Landrush`, `Trailblazer`, `Head-to-head` or `Point-to-point`. This label does not create native rules or AI paths. Point-to-point layouts use one complete run. A Race-capable pack must provide a working starting grid and AI route, with 1–7 opponents declared per layout. A Practice-only layout may declare zero opponents. LAN and career are outside this format.

Payload files for a layout live under `payload/tracks/usa/<Folder>/`. At minimum its `route_0` contains `track.jpk`, `routesplit.pssg`, `track.vis`, `grids.pssg` and `progress_track.xml`. Include every other resource the prepared track needs; these five files alone do not establish playability. `route_0.clm` is optional when the track does not use a baked shadow map.

Each `Session` entry pairs a game `Target` with a prepared `File` under `payload/session/`. The file must appear in `Files`, and its original target hash must appear in `Dependencies`. `database/database.bin` registration is required. Only these shared targets are currently permitted:

- `database/database.bin`, `surface_materials.xml`
- `language/language_eng.lng`, `language/language_fre.lng`, `language/language_ger.lng`, `language/language_ita.lng`, `language/language_jpn.lng`, `language/language_pol.lng`, `language/language_rus.lng`, `language/language_spa.lng`, `language/language_use.lng`
- `effects/pfx_kickup_data_set.xml`, `effects/pfx_pssg_dataset.xml`
- `tracks/light_definitions.xml`, `tracks/waterdefs.xml`, `tracks/ornament_system_settings.xml`
- `dx11/shaderpack/shaderpack.pssg`

All other game destinations are blocked. Shared files are accepted only when their original hashes match. Existing files in an external track's destination are never overwritten. Two packs claiming the same folder are both blocked.

Bounds: 256 folders per content kind, 32 layouts per pack, 4,096 payload files, 512 dependencies, 768 MiB per payload file, 64 MiB per shared replacement and 4 GiB total payload. Manifests are limited to 2 MiB. Paths must be relative, use `/`, and cannot contain traversal, device names, links or alternate data streams. Only recognised game-data extensions are accepted; executables and scripts are rejected.

## Cars: discovery only

Car folders use `Kind: "car"`, an empty `Layouts` array and `Car` with `Code`, `Donor` and `ClassName`. Codes are three lowercase letters/digits and must differ from the donor. Payloads are confined to that code's `cars`, `interiors` and `audio/dictionaries/d2vr_<code>` folders. Duplicate car codes are flagged. This is a reserved discovery format, not a working car authoring contract: registration, supported car/track combinations and second-car acceptance still need to be completed before car activation can be offered.
