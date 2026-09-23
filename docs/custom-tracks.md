# Additional track prototype

## Status — 2026-09-23

An experimental, original **355.93 m** circuit is installed in the isolated
`artifacts/track-prototype/game` copy. It has two 90 m straights, two radius-28 m
turns, a 10 m road, 4 m runoff on either side, a 1.5 m smooth ramp and 1.2 m
barriers. The road sits at Y=8 m inside the donor terrain tile's X/Z footprint.
The prototype now has a flat ground apron at Y=0. Sky, distant scenery, materials
and lighting come from the user's local Battersea assets; this is a geometry
prototype, not finished environment art.

**The first manual test failed visual acceptance.** The user reported Battersea
scenery with the car held above the original track on an invisible surface.
That supports the new collision loading, but does not prove the whole route.
Candidate 5 corrected two PSSG structure defects. The user then confirmed the
track was visible but disappeared after a few metres, with Battersea structures
still visible. Candidate 7 addressed terrain visibility bounds and suppressed
local scenery, but the user reported disappearance from some angles again.
Candidate 8 disables donor precomputed occlusion in the prototype's visibility
file; it has not yet been tested in-game. Do not describe this as a
playable custom-track release. Before geometry replacement, the separately
named donor clone reached the rendered race introduction with
`london/d2vr_test/route_0` recorded in the game's parsed configuration. No
database changes were needed for that experiment. A file-open trace ruling out
all internal fallback behavior was not captured. The original Battersea route
also loaded in the isolated copy.

Computer use was cancelled by the user. No further desktop automation or game
launches were performed by the agent after cancellation. The user performed the
manual test above. Rendering, handling, resets, full laps, restart and repeated
game launches remain acceptance gates.

### Invisible-road repair

The original writer emitted `prototype_road` as both the `RENDERDATASOURCE` and
`RENDERINDEXSOURCE` ID: the upstream car writer expects a name containing `RDS`
and derives the index ID by replacing that token with `RIS`. Our name lacked the
token. It also omitted `RENDERDATASOURCE.primitive`, which the donor terrain
explicitly declares as `triangles`. The repair assigns a distinct index ID and
sets the terrain primitive explicitly. Only `routesplit.pssg` changed between
candidates 4 and 5; collision, grid and route metadata are byte-identical.

`visual-check` rejects candidate 4 with both errors and accepts candidate 5.
Authoring now checks unique object IDs, triangle declarations, stream counts and
instance source references in the saved PSSG, in addition to geometry comparison.
These were real output defects missed by the initial coordinate checks; their
correction is not yet proof that donor visibility data will render the circuit.
The user's next test confirmed initial road visibility but exposed culling as
the car moved.

### Visibility bounds and scenery cleanup

The donor `track.vis` terrain record for tile 8 (`HIGH/LOW_4_4`) had a maximum Y
of 4.777686 m, below the whole authored road. Candidate 7 raises this to 10.696216
m, enclosing the road and barriers. The patch parses all 599 spatial nodes and
25 terrain records, checks the exact donor SHA-256, matches the old tile bounds,
and verifies its containing root bounds. Only four bytes change: that tile's
maximum Y. Original PVS region masks remain unchanged; this repairs a confirmed
bounds mismatch, not a proven replacement for a visibility compiler.

The scenery pass keeps 62 ornament placements named `distant` or `horizon_*`,
plus the unchanged sky and trees. It suppresses 1,374 local ornaments and 845
ENS entity placements (including local object collision) by parking them at
Y=-10000. XML and binary ornament transforms are updated together. This keeps
all placement slots, IDs and references stable rather than deleting indexed
entries. The reports list every retained/suppressed ornament group. A simple
200 by 215 m ground apron beneath the circuit provides replacement ground and
collision. The road, grid and route layout remain the same.

This is deliberately a first cleanup pass. Donor baked lighting, crowds, water
and effect systems remain; floating effects or inappropriate shadows may still
need targeted changes. Confirm that the road stays visible through both turns
and the ramp before treating the visibility issue as resolved.

### Remaining angular disappearance: candidate 8

Correcting the tile bounds alone did not resolve the reported disappearance.
The game still consumed the donor's precomputed visibility masks (PVS). Static
inspection of the supported executable identifies leaf-mask dispatch at RVA
`0x827D3A..0x827D65` and decoding at `0x7EAF20..0x7EAFE6`: codec 0 copies raw bytes,
while codec 1 expands zero-terminated literal/repeat runs. The view tree has 481
six-byte nodes, including 241 leaves, each decoding to a 320-byte mask.

Candidate 8 redirects every leaf to one raw all-visible mask inside the existing
mask section. This removes the old buildings' baked occlusion decisions while
retaining normal frustum culling, object IDs, file length and scene-tree offsets.
It is restricted to the fingerprinted donor file. The cost can be more rendering
work for background objects; it is a prototype measure, not a visibility baker.
Only `track.vis` differs from candidate 7. PSSG, collision, grids and scenery
placements are byte-identical. Road, runoff and ground winding was also checked:
all 1,082 surface triangles face upward.

`mask-test` covers raw and RLE decoding, malformed/truncated runs, invalid codecs
and offsets, plus byte-extent checks. It decodes all 241 donor masks and verifies
all replacement masks. Runtime confirmation still requires rotating the camera
and driving through the previously failing viewpoints; do not label this fixed
until that check passes.

## Trying the candidate

Close the previous test launcher, then run `tools/track-prototype/launch.cmd`.
This opens the internal Release launcher built into `artifacts/track-prototype/launcher`
against the isolated game copy. Choose **Direct practice → Rallycross → Prototype
circuit (experimental, desktop solo)**, select the Subaru STI, and use **Launch**
for desktop. The script does not change saved launcher preferences.

All 41 original routes remain in the catalog and the isolated installation.
The prototype appears only when its installation marker exists. Before preparing
a session, the launcher checks its required files against the marker's SHA-256
hashes. Missing/changed assets fail before preparation. Race/AI and VR are rejected;
native-menu and LAN registration are not implemented.

Use `tools/track-prototype/restore.ps1` with the game closed to restore the
donor clone and hide the prototype. This retains the candidate in a separate
lab directory; it deletes no track assets. Original track folders and the game
database are never edited by these scripts.

## Reproduction and ownership

Prerequisites: the supported licensed game at `artifacts/game`, Blender 5.1,
.NET 10, and the existing EGO converter at `artifacts/ego-converter`. Its build
helper is `tools/build-xml-converter.ps1`. Do not vendor or distribute game assets.
This run used EGO source commit `f3fe9ee0f6e8379c64bc4a29e1d1b586bba5d1c1` and
`EgoEngineLibrary.dll` SHA-256
`B27A45447CD08A8E46423F16151A9D375A179A374AB83475B8029749FF528D98`.

From the repository root, on a fresh lab:

```powershell
tools/track-prototype/prepare.ps1
dotnet build tools/track-prototype/TrackPrototype.csproj -c Release
dotnet tools/track-prototype/bin/Release/net10.0/TrackPrototype.dll mask-test artifacts/game/tracks/london/battersea/route_1/track.vis
dotnet tools/track-prototype/bin/Release/net10.0/TrackPrototype.dll roundtrip artifacts/game/tracks/london/battersea/route_1 artifacts/track-prototype/roundtrip
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python tools/track-prototype/author.py -- artifacts/track-prototype/authored
dotnet tools/track-prototype/bin/Release/net10.0/TrackPrototype.dll author artifacts/game/tracks/london/battersea/route_1 artifacts/track-prototype/authored artifacts/track-prototype/candidate
tools/track-prototype/install.ps1 -Candidate artifacts/track-prototype/candidate
dotnet build tests/launcher/LauncherTests.vbproj -c Release -p:OutDir="$PWD/artifacts/track-prototype/launcher/"
artifacts/track-prototype/launcher/LauncherTests.exe . --prototype-only --installed-prototype
```

Preparation makes a full independent game copy (about 10.8 GiB), copies shared
Battersea files into `d2vr_test`, and copies donor `route_1` to new `route_0`.
Outputs must be new paths. Preparation rejects an existing test installation;
installation rejects an already installed candidate. To iterate, restore first
and use a fresh authoring/candidate output directory. Installation verifies
candidate hashes from its validation report, stages the complete route, retains
the donor backup and switches directories only with the game closed.

`author.py` owns the source mesh and exports `.blend`, collision GLB and explicit
Y-up/metres JSON. `TrackAuthor` writes EGO ground collision, terrain PSSG, grids,
navigation, progress gates, route overrides, boundaries, camera and reset planes.
It preserves the donor's terrain tile names and binds the new geometry into
`HIGH_4_4` and `LOW_4_4`, using existing shader layouts. Four-character collision
surface codes are retained separately from rendering materials. AI metadata is
present for route initialization; competitive AI behavior is not supported.

The prototype is intentionally a fixed catalog entry, not a general plugin
format. `prototype.json` schema 1 contains `TrackId` and a map of required relative
filenames to SHA-256 hashes. The launcher uses a fixed required-file allowlist.
Existing catalog IDs and settings meanings are unchanged.

## Evidence and outstanding dependencies

- `artifacts/track-prototype/roundtrip-2`: XML and PSSG semantic round trips passed.
  Ground and all three CQTC files rebuilt and decoded. Bidirectional triangle
  matching found zero missing/new geometric triangles at 2 cm tolerance, checking
  material codes and cyclic winding. JPK rebuilding introduced 8 duplicate triangle
  instances at partition boundaries; archive byte identity is not expected.
- `artifacts/track-prototype/authored-4` and `candidate-8`: 3,962 authored/visual
  triangles; 4,180 decoded collision triangle instances after partitioning; 180
  navigation and progress gates. Blender GLB, JSON, saved PSSG and collision match.
  The starting-grid matrix has determinant 1 and starts just after the finish gate.
  `validation.json` records static results and exact output hashes, explicitly
  keeping `RuntimeValidated=false`.
- `candidate-4-visual-structure.json` and `candidate-5-visual-structure.json` in
  the lab record the failing original and passing repaired visual structure.
- `artifacts/track-prototype/clearance-3.json`: no sampled path intersections with
  the existing GoRacer extraction's Battersea object-collision geometry. This is
  nine path samples per segment, not a complete car-volume sweep or game proof.
- `artifacts/track-prototype/headless-tests.log`: 398 launcher checks passed,
  including prototype restrictions and repeat prepare/recover behavior. The
  headless switch returns before controller polling and window-based tests.
- `artifacts/track-prototype/installed-tests.log`: 21 focused checks passed,
  including validation of the installed candidate and all 41 original routes.
- `artifacts/track-prototype/candidate-7-installed-tests.log`: 25 focused checks
  passed, including tampered visibility/scenery rejection and installed assets.
- `artifacts/track-prototype/candidate-7-headless-tests.log`: 402 headless launcher
  checks passed with the updated file guards. No game or GUI test was launched.
- Candidate 7's `visibility.json` and `scenery.json` record the bounds patch,
  byte extent, retained backdrop and suppressed placement counts.
- Candidate 8's mask-test log records 10 checks plus all 241 masks; its installed
  test log records 25 passing checks. `visibility.json` records all-visible mode.
- `artifacts/track-prototype/original-integrity.json`: 154 hash comparisons passed
  for the original donor assets in both installations and the copied database.
- `artifacts/track-prototype/authoring-preview.png`: inspected Blender render of
  the authored loop, runoff and ramp; this is not an in-game screenshot.
- `installed.json` records the active candidate, retained donor backup and hashes.
  Restore/reinstall was exercised in this lab.

`track.vis` retains the donor's 151,008-byte structure with expanded terrain
bounds and all-visible PVS masks. No general visibility writer was found.
Keeping tile identities and the footprint does not prove visibility correctness.
`route.clm`, `ao.clm`
and shared baked lighting remain donor data. The available CLM template describes
a compressed light-map tree, not a lighting baker. Water, effects and introduction
cameras remain donor-derived. Their exact interaction with the new elevated road
and cleared local scenery needs runtime inspection.

The reset/camera planes use source `RESE`/`RESD` codes; route boundaries use
`FBND`; visible solid barriers also exist in ground collision as `CON+`.
Correct engine interpretation of the newly generated planes is unverified.

Manual acceptance must cover visible road/ramp/barriers from every section,
tire contact and surface response, both barrier sides, leaving the course and
resetting, a full timed lap, restart, return/relaunch and an original-track
regression. If geometry is missing, investigate visibility/tile loading before
adding detail. If the vehicle encounters invisible objects, inspect the retained
placements rather than disabling collision globally.

This is an internal compile/test build. No distribution package or release was
created and no version was reserved. A future distribution containing this new
feature must use `tools/package.ps1 -VersionBump Minor` after runtime acceptance.
