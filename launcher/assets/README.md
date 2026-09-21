# Launcher artwork

`race-catalog.json` is the embedded supported-build route/car identifier allowlist, with English display names and discipline mappings. It contains no game assets or full extracted database/localization tables. Its provenance, validation and limitations are documented in [direct practice](../../docs/direct-practice.md).

`DiRT2VR.png` is the original transparent artwork generated with the built-in image-generation tool on 2026-09-21. `DiRT2VR.ico` contains 16, 24, 32, 48, 64, 128 and 256 pixel entries, encoded from that artwork by `tools/make-launcher-icon.ps1`. Both files belong in source control; generation is not required when building the launcher. The icon is embedded in the executable, the form and the installer.

Generation prompt:

> Use case: logo-brand. Asset type: Windows application icon for DiRT2VR, an unofficial DiRT 2 virtual-reality racing mod. Create a single polished square icon, centered on a genuinely transparent background. Bold off-road rally badge with a chunky white numeral 2 integrated above a lime-green VR headset silhouette, charcoal black shield backing, and a few restrained white dirt/gravel flecks. The exact small word 'DiRT' above the large 2 and 'VR' clearly integrated into the headset. Strong recognizable silhouette and simple shapes that remain readable at 32 pixels. Inspired by DiRT 2's gritty rally graphic language and acid lime highlights, not a photoreal scene. Front-facing flat graphic with subtle depth, crisp edges, generous 8% transparent margin, balanced square composition. No mockup, no surrounding scene, no extra slogans, no watermark.
