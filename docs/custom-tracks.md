# Additional track prototype

## Status — 2026-09-23

An experimental, original **355.93 m** circuit is installed in the isolated
`artifacts/track-prototype/game` copy. It has two 90 m straights, two radius-28 m
turns, a 10 m road, 4 m runoff on either side, a 1.5 m smooth ramp and 1.2 m
barriers. The road sits at Y=8 m inside the donor terrain tile's X/Z footprint.
The surrounding scene, sky, materials and lighting come from the user's local
Battersea assets; this is a geometry prototype, not finished environment art.

**The generated circuit has not been validated in-game.** Do not describe it as
a playable custom-track release. Before geometry replacement, the separately
named donor clone reached the rendered race introduction with
`london/d2vr_test/route_0` recorded in the game's parsed configuration. No
database changes were needed for that experiment. A file-open trace ruling out
all internal fallback behavior was not captured. The original Battersea route
also loaded in the isolated copy.

Computer use was cancelled by the user. No further desktop automation or game
launches were performed after cancellation. Generated-track rendering, handling,
reset behavior, full laps, restart and repeated game launches remain manual gates.

## Trying the candidate

Run `tools/track-prototype/launch.cmd`. This opens the internal Release launcher
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
dotnet tools/track-prototype/bin/Release/net10.0/TrackPrototype.dll roundtrip artifacts/game/tracks/london/battersea/route_1 artifacts/track-prototype/roundtrip
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python tools/track-prototype/author.py -- artifacts/track-prototype/authored
dotnet tools/track-prototype/bin/Release/net10.0/TrackPrototype.dll author artifacts/game/tracks/london/battersea/route_1 artifacts/track-prototype/authored artifacts/track-prototype/candidate
tools/track-prototype/install.ps1 -Candidate artifacts/track-prototype/candidate
dotnet build tests/launcher/LauncherTests.vbproj -c Release
tests/launcher/bin/Release/net10.0-windows/win-x64/LauncherTests.exe . --prototype-only --installed-prototype
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
- `artifacts/track-prototype/authored-3` and `candidate-4`: 3,960 authored/visual
  triangles; 4,048 decoded collision triangle instances after partitioning; 180
  navigation and progress gates. Blender GLB, JSON, saved PSSG and collision match.
  The starting-grid matrix has determinant 1 and starts just after the finish gate.
  `validation.json` records static results and exact output hashes, explicitly
  keeping `RuntimeValidated=false`.
- `artifacts/track-prototype/clearance-3.json`: no sampled path intersections with
  the existing GoRacer extraction's Battersea object-collision geometry. This is
  nine path samples per segment, not a complete car-volume sweep or game proof.
- `artifacts/track-prototype/headless-tests.log`: 398 launcher checks passed,
  including prototype restrictions and repeat prepare/recover behavior. The
  headless switch returns before controller polling and window-based tests.
- `artifacts/track-prototype/installed-tests.log`: 21 focused checks passed,
  including validation of the installed candidate and all 41 original routes.
- `artifacts/track-prototype/original-integrity.json`: 154 hash comparisons passed
  for the original donor assets in both installations and the copied database.
- `artifacts/track-prototype/authoring-preview.png`: inspected Blender render of
  the authored loop, runoff and ramp; this is not an in-game screenshot.
- `installed.json` records the active candidate, retained donor backup and hashes.
  Restore/reinstall was exercised in this lab.

`track.vis` is still the donor's 151,008-byte visibility data. No compatible
writer was found. Keeping tile identities and the footprint reduces changes to
its assumptions but does not prove visibility correctness. `route.clm`, `ao.clm`
and shared baked lighting remain donor data. The available CLM template describes
a compressed light-map tree, not a lighting baker. Shared objects, ornaments,
water, effects and introduction cameras also remain donor-derived. Their exact
interaction with the new elevated road needs runtime inspection.

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
