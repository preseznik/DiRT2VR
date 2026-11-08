using System.Text;
using System.Text.RegularExpressions;

namespace DiRT2VR.Multiplayer;

// The provider writes lap reports and official clock results independently.
internal sealed class DirectRaceProgress
{
    internal sealed class Driver
    {
        int timedLaps = -1;
        internal int Completed => ClockFinish ? 3 : timedLaps;
        readonly int[] times = new int[3];
        bool sawDriving, leftEarly, sawThree, inconsistentTarget, policy, timedFinish;
        (int Session, int Player) sampleState, resultState;
        bool threeLapTarget, endedShort, clockFinish, clockDnf;
        int flag = -1, reason = -1;
        internal int ResultSlot { get; private set; } = -1;
        bool MatchingResult => sawDriving && threeLapTarget && !inconsistentTarget && sampleState == resultState;
        internal bool ClockFinish => MatchingResult && clockFinish && !endedShort && !policy && flag == 0;
        internal bool StationaryDnf => MatchingResult && clockDnf && flag == 1 && reason == 1;
        internal bool Finished => timedFinish || ClockFinish;

        internal void Sample(int session, int player, int config, int active, int report,
            int completed, int first, int second, int third, int policyFlag, int policyReason)
        {
            sampleState = (session, player);
            threeLapTarget = config == 3 && active == 3;
            flag = policyFlag;
            reason = policyReason;
            if (config >= 0 && config != 3 || active >= 0 && active != 3) inconsistentTarget = true;
            if (policyFlag == 1) policy = true;
            if (player == 1) sawDriving = true;
            if (!leftEarly && report == 1 && completed is >= 0 and <= 3)
            {
                timedLaps = Math.Max(timedLaps, completed);
                if (completed >= 1 && first > 0) times[0] = first;
                if (completed >= 2 && second > 0) times[1] = second;
                if (completed == 3 && third > 0) times[2] = third;
                if (sawDriving && completed == 3 && times.All(t => t > 0)) sawThree = true;
            }
            if (player is 2 or 3 or 4 && sawDriving && !sawThree)
                leftEarly = true;
            if (sawThree && !leftEarly && !policy && !inconsistentTarget && policyFlag == 0 &&
                config == 3 && active == 3 && player is 3 or 4 && session is 3 or 4 or 5 or 7)
                timedFinish = true;
        }

        internal void Crossing(int session, int player, int mode, int stopped, int laps, int listed,
            int slot, int status, int kind)
        {
            resultState = (session, player);
            ResultSlot = slot;
            bool final = session is 3 or 5 && player is 3 or 4 && mode == 1 && stopped == 1 && slot is 0 or 1;
            if (final && listed == 0 && laps is >= 0 and < 3) endedShort = true;
            clockFinish = final && listed == 1 && laps == 3 && status == 0 && kind == 2;
            clockDnf = final && listed == 0 && laps is >= 0 and < 3 && status == 1 && kind == 1;
        }

        internal string Summary(string name)
        {
            if (Completed < 0) return $"{name} — lap count unavailable.";
            var laps = times.Take(Completed).Select(t => t <= 0 ? "—" :
                $"{t / 60000}:{t / 1000 % 60:00}.{t % 1000:000}");
            string timing = string.Join(" / ", laps);
            string outcome = Finished ? "Finished. " : StationaryDnf ? "Disqualified for standing still. " : "";
            return $"{name} — {outcome}{Completed} of 3 laps recorded" + (timing.Length == 0 ? "." : $" ({timing}).");
        }
    }

    static readonly Regex SampleLine = new(@"^\[tick=[0-9]+ thread=[0-9]+\] DIRECT_RACE sample local=([01]) session_state=(-?\d+) player_state=(-?\d+) config_laps=(-?\d+) active_laps=(-?\d+) report=(-?\d+) completed=(-?\d+) current=-?\d+ lap1_ms=(-?\d+) lap2_ms=(-?\d+) lap3_ms=(-?\d+) policy_flag=(-?\d+) policy_reason=(-?\d+)$", RegexOptions.CultureInvariant);
    static readonly Regex CrossingLine = new(@"^\[tick=[0-9]+ thread=[0-9]+\] DIRECT_RACE crossing local=([01]) session_state=(-?\d+) player_state=(-?\d+) clock_mode=(-?\d+) clock_stopped=(-?\d+) clock_laps=(-?\d+) clock_listed=(-?\d+) result_slot=(-?\d+) result_status=(-?\d+) result_kind=(-?\d+)$", RegexOptions.CultureInvariant);
    internal Driver Local { get; } = new();
    internal Driver Peer { get; } = new();
    internal bool SawSample { get; private set; }
    internal bool LocalResultReady => Local.Finished ||
        (Local.StationaryDnf && Peer.ClockFinish && Local.ResultSlot != Peer.ResultSlot);

    internal void Accept(string line)
    {
        bool sample = line.Contains("] DIRECT_RACE sample ", StringComparison.Ordinal);
        if (!sample && !line.Contains("] DIRECT_RACE crossing ", StringComparison.Ordinal)) return;
        var match = (sample ? SampleLine : CrossingLine).Match(line);
        if (!match.Success) return;
        Span<int> fields = stackalloc int[12];
        for (int i = 0; i < match.Groups.Count - 1; i++)
            if (!int.TryParse(match.Groups[i+1].ValueSpan, out fields[i])) return;
        var driver = fields[0] == 1 ? Local : Peer;
        if (!sample)
        {
            driver.Crossing(fields[1],fields[2],fields[3],fields[4],fields[5],fields[6],fields[7],fields[8],fields[9]);
            return;
        }
        SawSample = true;
        driver.Sample(fields[1],fields[2],fields[3],fields[4],fields[5],fields[6],fields[7],
            fields[8],fields[9],fields[10],fields[11]);
    }

    internal sealed class Tail(string path, DirectRaceProgress progress) : IDisposable
    {
        StreamReader? reader;
        readonly char[] buffer = new char[4096];
        readonly StringBuilder partial = new();
        bool skipLine;

        internal void Poll()
        {
            try
            {
                reader ??= new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    for (int i = 0; i < count; i++)
                    {
                        char c = buffer[i];
                        if (c == '\n')
                        {
                            if (!skipLine) progress.Accept(partial.ToString());
                            partial.Clear();
                            skipLine = false;
                        }
                        else if (c != '\r' && !skipLine)
                        {
                            if (partial.Length == 512) skipLine = true;
                            else partial.Append(c);
                        }
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { /* Missing measurements must never interrupt driving. */ }
        }

        public void Dispose() => reader?.Dispose();
    }
}
