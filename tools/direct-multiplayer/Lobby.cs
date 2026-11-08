using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace DiRT2VR.Multiplayer;

public sealed record Player(string Name, int Car, bool Ready = false);
public enum LayoutAgreement { Pending, Matching, Different }
public sealed record Grid(int Track, Player Host, Player? Guest, int Revision = 0,
                          DirectLayout? Layout = null, LayoutAgreement Agreement = LayoutAgreement.Pending)
{
    public bool CanStart => Guest != null && Guest.Car != Host.Car && (Layout == null || Agreement == LayoutAgreement.Matching);
    public bool BothReady => CanStart && Host.Ready && Guest!.Ready;
    public static bool ValidCar(int id) => id is 85 or 88;
    public static bool ValidTrack(int id) => id is 143 or 144;

    public Grid Select(bool host, int car, int track, DirectLayout? layout = null)
    {
        if (!ValidCar(car) || !ValidSelection(track, layout) || (!host && (Guest == null || track != Track || layout != Layout)))
            throw new InvalidDataException("Invalid race selection.");
        if ((host ? Host : Guest!).Car == car && Track == track && layout == Layout) return this;
        return this with { Track = track, Revision = checked(Revision + 1),
            Layout = layout, Agreement = LayoutAgreement.Pending,
            Host = Host with { Car = host ? car : Host.Car, Ready = false },
            Guest = Guest == null ? null : Guest with { Car = host ? Guest.Car : car, Ready = false } };
    }

    public Grid SetReady(bool host, bool ready, int revision)
    {
        if (revision != Revision || Guest == null || (ready && Layout != null && Agreement != LayoutAgreement.Matching)) return this;
        return host ? this with { Host = Host with { Ready = ready } }
                    : this with { Guest = Guest with { Ready = ready } };
    }

    internal static bool ValidSelection(int track, DirectLayout? layout) => layout == null ? ValidTrack(track)
        : track == 0 && DirectLayout.Known(layout.Id) && DiRT2VR.CustomTracks.SafeFiles.Digest(layout.Digest);
}

public sealed record LobbyMessage(string Kind, string Text = "", int Car = 0, int Track = 0,
                                 bool Ready = false, int Revision = 0, Grid? Grid = null, int? ExitCode = null);

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
