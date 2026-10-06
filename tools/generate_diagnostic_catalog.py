"""Share launcher catalog labels with bounded native event diagnostics."""
import json
import pathlib
import sys
catalog = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
quote = lambda value: json.dumps(value, ensure_ascii=True)
lines = ["// Generated from the launcher catalog; do not edit.",
         "struct DiagnosticTrack { const char *country, *track, *route, *label, *discipline; };",
         "constexpr DiagnosticTrack diagnosticTracks[]={"]
for track in catalog["Tracks"]:
    lines.append("    {" + ",".join(quote(track[k]) for k in ("Country", "Track", "Route", "Label", "Event")) + "},")
lines += ["};", "struct DiagnosticCar { const char *code, *label; };", "constexpr DiagnosticCar diagnosticCars[]={"]
for car in catalog["Cars"]:
    lines.append("    {" + quote(car["Code"]) + "," + quote(car["Label"]) + "},")
lines += ["};", ""]
pathlib.Path(sys.argv[2]).write_text("\n".join(lines), encoding="utf-8")
