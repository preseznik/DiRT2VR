using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;

namespace DiRT2VR.Multiplayer;

// Optional diagnostic for a private Direct run. A motion packet describes the
// local vehicle at that instant, including during the post-race AI drive.
internal sealed class LapTelemetry : IDisposable
{
    readonly UdpClient client;
    readonly Stopwatch sinceSample = Stopwatch.StartNew();
    int? lastLap;
    bool stopped;
    internal long Received { get; private set; }

    LapTelemetry(UdpClient client) => this.client = client;

    internal static LapTelemetry Open(string run, string gameRoot)
    {
        var client = new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        try
        {
            var path = Path.Combine(run,"Documents","My Games","DiRT2","hardwaresettings","hardware_settings_config.xml");
            var xml = XDocument.Load(File.Exists(path) ? path : Path.Combine(gameRoot,"system","hardware_settings_config.xml"));
            var motion = xml.Root?.Element("motion") ?? throw new InvalidDataException("Private game settings have no motion output.");
            motion.SetAttributeValue("ip","127.0.0.1");
            motion.SetAttributeValue("port",((IPEndPoint)client.Client.LocalEndPoint!).Port);
            motion.SetAttributeValue("enabled","true");
            motion.SetAttributeValue("extradata","3");
            motion.SetAttributeValue("delay","1");
            xml.Save(path);
            return new(client);
        }
        catch { client.Dispose(); throw; }
    }

    internal void Poll(Action<string> log)
    {
        if (stopped) return;
        IPEndPoint sender = new(IPAddress.Any,0);
        try
        {
            for (int i=0; i<256 && client.Available>0; i++)
            {
                var data = client.Receive(ref sender);
                if (!IPAddress.IsLoopback(sender.Address)) continue;
                if (++Received == 1) log($"First motion packet: {data.Length} bytes.");
                if (data.Length < 152) continue;
                float Field(int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
                float lap = Field(144);
                if (!float.IsFinite(lap) || lap < -1 || lap > 100 || lap != MathF.Truncate(lap)) continue;
                if (lastLap == (int)lap && sinceSample.Elapsed < TimeSpan.FromSeconds(10)) continue;
                lastLap = (int)lap;
                sinceSample.Restart();
                string Optional(int offset) => data.Length >= offset+4 ? Field(offset).ToString("G9",CultureInfo.InvariantCulture) : "absent";
                log(FormattableString.Invariant($"Motion bytes={data.Length} raceSeconds={Field(0):F3} lapSeconds={Field(4):F3} lapMetres={Field(8):F1} totalMetres={Field(12):F1} lapIndex={lap} position={Optional(156)} totalLaps={Optional(240)}"));
            }
        }
        catch (SocketException error)
        {
            stopped = true;
            log($"Motion logging stopped: {error.SocketErrorCode}.");
        }
    }

    public void Dispose() => client.Dispose();
}
