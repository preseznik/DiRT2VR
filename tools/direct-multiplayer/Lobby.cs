using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace DiRT2VR.Multiplayer;

public sealed record Player(string Name, int Car, bool Ready = false, DirectCustomCar? CustomCar = null);
public enum LayoutAgreement { Pending, Matching, Different }
public sealed record Grid(int Track, Player Host, Player? Guest, int Revision = 0,
                          DirectLayout? Layout = null, LayoutAgreement Agreement = LayoutAgreement.Pending, int Laps = 3,
                          int CarRevision = 0, bool? HostCarsMatch = null, bool? GuestCarsMatch = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SupportsRace => RallycrossCatalog.Supports(this);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasCustomCars => Host.CustomCar != null || Guest?.CustomCar != null;
    public bool CanStart => Guest != null && SupportsRace && (Layout == null || Agreement == LayoutAgreement.Matching) &&
        (!HasCustomCars || HostCarsMatch == true && GuestCarsMatch == true);
    public bool BothReady => CanStart && Host.Ready && Guest!.Ready;
    public bool IsCircuit => Layout is null ? ValidTrack(Track) && DirectStockCatalog.Track(Track).Circuit
        : Layout.ExternalCircuit ?? DiRT2VR.CustomTracks.TrackPacks.ForLayout(Layout.Id).GetLayout(Layout.Id).Circuit;
    public int RequiredLaps => IsCircuit ? Laps : 1;
    public static bool ValidLaps(int laps) => laps is >= 1 and <= 20;
    public static bool ValidCar(int id, DirectCustomCar? custom = null) =>
        custom == null ? DirectStockCatalog.Cars.ContainsKey(id) : id == 0 && custom.Valid;
    public static bool ValidTrack(int id) => id == DirectStockCatalog.PrototypeId || DirectStockCatalog.Tracks.ContainsKey(id);

    public Grid Select(bool host, int car, int track, DirectLayout? layout = null, DirectCustomCar? customCar = null)
    {
        if (!ValidCar(car, customCar) || !ValidSelection(track, layout) || (!host && (Guest == null || track != Track || layout != Layout)))
            throw new InvalidDataException("Invalid race selection.");
        var player = host ? Host : Guest!;
        if (player.Car == car && player.CustomCar == customCar && Track == track && layout == Layout) return this;
        var selected = player with { Car = car, CustomCar = customCar, Ready = false };
        int revision = checked(Revision + 1);
        return this with { Track = track, Revision = revision,
            Layout = layout, Agreement = layout == Layout && Agreement == LayoutAgreement.Matching ? Agreement : LayoutAgreement.Pending,
            CarRevision = player.CustomCar == customCar ? CarRevision : revision,
            HostCarsMatch = player.CustomCar == customCar ? HostCarsMatch : null,
            GuestCarsMatch = player.CustomCar == customCar ? GuestCarsMatch : null,
            Host = host ? selected : Host with { Ready = false },
            Guest = !host ? selected : Guest is null ? null : Guest with { Ready = false } };
    }

    public Grid SetReady(bool host, bool ready, int revision)
    {
        if (revision != Revision || Guest == null || (ready && !CanStart)) return this;
        return host ? this with { Host = Host with { Ready = ready } }
                    : this with { Guest = Guest with { Ready = ready } };
    }

    public Grid SetLaps(int laps)
    {
        if (!ValidLaps(laps)) throw new InvalidDataException("Choose 1 to 20 laps.");
        if (Laps == laps) return this;
        return this with { Laps = laps, Revision = checked(Revision + 1), Host = Host with { Ready = false },
            Guest = Guest is null ? null : Guest with { Ready = false } };
    }

    internal static bool ValidSelection(int track, DirectLayout? layout) => layout == null ? ValidTrack(track)
        : track == 0 && DiRT2VR.CustomTracks.SafeFiles.Digest(layout.Digest) &&
          (layout.ExternalCircuit.HasValue ? DiRT2VR.CustomTracks.ExternalPacks.IsTrackLayoutId(layout.Id) : DirectLayout.Known(layout.Id));
}

public sealed record LobbyMessage(string Kind, string Text = "", int Car = 0, int Track = 0,
                                 bool Ready = false, int Revision = 0, Grid? Grid = null, int? ExitCode = null,
                                 DirectCustomCar? CustomCar = null);

// Only the host publishes the grid and starts the race. Race traffic stays in DiRT 2.
public sealed class LobbyConnection : IDisposable
{
    public const int Port = 39822;
    private const int MaxFrame = 2048;
    readonly TcpClient client;
    readonly NetworkStream stream;
    readonly SemaphoreSlim writing = new(1);
    public IPAddress Peer { get; }

    public LobbyConnection(TcpClient connected)
    {
        client = connected;
        Peer = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        if (!IsPrivate(Peer)) { client.Dispose(); throw new IOException("Direct requires a private LAN IPv4 address."); }
        client.NoDelay = true;
        stream = client.GetStream();
    }

    public static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31);
    }

    public static bool ValidText(string? text, int max) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl);

    public async Task SendAsync(LobbyMessage message, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
        if (body.Length > MaxFrame) throw new InvalidDataException("Direct lobby message too long.");
        byte[] frame = new byte[body.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        await writing.WaitAsync(token);
        try { await stream.WriteAsync(frame, token); }
        finally { writing.Release(); }
    }

    public async Task<LobbyMessage> ReceiveAsync(CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > MaxFrame) throw new InvalidDataException("Invalid Direct lobby message size.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<LobbyMessage>(body) ?? throw new InvalidDataException("Empty Direct lobby message.");
    }

    public void Dispose() => client.Dispose();
}
