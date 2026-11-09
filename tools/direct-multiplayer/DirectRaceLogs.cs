using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DiRT2VR.Multiplayer;

public static class DirectRaceLogs
{
    // Only copy known measurements. Other native records can contain a join
    // ticket, session addresses, private profile names, or filesystem paths.
    static readonly Regex NativeRecord = new(
        @"^\[tick=(\d{1,20}) thread=\d+\] (RX_REQUEST|EVENT defaults|EVENT configured|LOBBY observe|ACT_GREEN original_return|DIRECT_RACE sample|DIRECT_RACE crossing|DIRECT_RACE distance|BOOT held native-frontend-job) ");
    static readonly Regex FrontendMeasurement = new(
        @"(?:^| )(calls=[0-9]{1,10}|original_called=[01])(?= |$)");
    static readonly Regex Measurement = new(
        @"(?:^| )((?:race|class|rounds|track|location|laps|collision|defaulted|native_race|native_class|active|ready|ack|result|local|session_state|player_state|config_laps|active_laps|report|completed|current|lap[123]_ms|policy_flag|policy_reason|clock_mode|clock_stopped|clock_laps|clock_listed|result_slot|result_status|result_kind)=-?[0-9]{1,10})(?= |$)");
    static readonly Regex ExitRecord = new(
        @"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z (?:Green\. Requested laps: \d+\. Lap telemetry: (?:on|off)\.|First motion packet: \d+ bytes\.|Motion packets received: \d+\.|(?:Both drivers (?:requested return|finished three laps)|Both PCs confirmed the race result)\. Window close sent: (?:True|False)\.|Process exited: 0x[0-9A-Fa-f]{8}\.|Motion bytes=\d+ raceSeconds=[\d.-]+ lapSeconds=[\d.-]+ lapMetres=[\d.-]+ totalMetres=[\d.-]+ lapIndex=[\d.-]+ position=(?:absent|[\d.Ee+-]+) totalLaps=(?:absent|[\d.Ee+-]+))$");

    public static void Save(string run, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a .zip file for the race logs.");
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                int count = 0;
                foreach (string name in new[] { "direct.log", "race-exit.log" })
                {
                    string source = Path.Combine(run, name);
                    PrivateGame.PlainPath(source);
                    if (!File.Exists(source)) continue;
                    using var output = new StreamWriter(zip.CreateEntry(name).Open());
                    foreach (string line in File.ReadLines(source))
                    {
                        string? safe = name == "direct.log" ? NativeLine(line) : ExitLine(line);
                        if (safe == null) continue;
                        output.WriteLine(safe);
                        count++;
                    }
                }
                if (count == 0) throw new IOException("This race has no recorded race diagnostics to save.");
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static string? NativeLine(string line)
    {
        var record = NativeRecord.Match(line);
        if (!record.Success) return null;
        var allowed = record.Groups[2].Value == "BOOT held native-frontend-job" ? FrontendMeasurement : Measurement;
        var fields = allowed.Matches(line, record.Length - 1).Select(m => m.Groups[1].Value).ToArray();
        return fields.Length == 0 ? null : $"[tick={record.Groups[1].Value}] {record.Groups[2].Value} {string.Join(' ', fields)}";
    }

    static string? ExitLine(string line)
    {
        if (ExitRecord.IsMatch(line)) return line;
        // Preserve the fact of these failures without exporting OS error text.
        var issue = Regex.Match(line, @"^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}Z (?:Process exited; exit code unavailable|Could not retain the process handle|Window close failed|Motion logging stopped)):");
        return issue.Success ? issue.Groups[1].Value : null;
    }
}
