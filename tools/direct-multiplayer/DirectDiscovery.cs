using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DiRT2VR.Multiplayer;

public sealed record DiscoveredLobby(string Name, IPAddress Address)
{
    public override string ToString() => $"{Name} ({Address})";
}

public static class DirectDiscovery
{
    const int QuerySize = 24;
    static readonly byte[] Query = "D2VRDRQ1"u8.ToArray(), Reply = "D2VRDRH1"u8.ToArray();

    internal static async Task AdvertiseAsync(string name, CancellationToken token)
    {
        try
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, LobbyConnection.Port));
            var response = new byte[QuerySize + Encoding.UTF8.GetByteCount(name)];
            Reply.CopyTo(response, 0);
            Encoding.UTF8.GetBytes(name, response.AsSpan(QuerySize));
            while (!token.IsCancellationRequested)
            {
                var request = await socket.ReceiveAsync(token).ConfigureAwait(false);
                if (request.Buffer.Length != QuerySize || !request.Buffer.AsSpan().StartsWith(Query) ||
                    !LobbyConnection.IsPrivate(request.RemoteEndPoint.Address)) continue;
                request.Buffer.AsSpan(8).CopyTo(response.AsSpan(8));
                await socket.SendAsync(response.AsMemory(), request.RemoteEndPoint, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) { } // Manual IP joins still work if discovery is unavailable.
    }

    public static async Task<DiscoveredLobby[]> FindAsync(IEnumerable<IPAddress> destinations, CancellationToken token)
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        var query = new byte[QuerySize];
        Query.CopyTo(query, 0);
        Guid.NewGuid().TryWriteBytes(query.AsSpan(8));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        var found = new Dictionary<IPAddress, DiscoveredLobby>();
        try
        {
            bool sent = false;
            foreach (var target in destinations.Distinct())
            {
                try
                {
                    await socket.SendAsync(query.AsMemory(), new IPEndPoint(target, LobbyConnection.Port), deadline.Token).ConfigureAwait(false);
                    sent = true;
                }
                catch (SocketException) { }
            }
            if (!sent) throw new IOException("Could not send a LAN search. Enter the host IP to join.");
            while (found.Count < 64)
            {
                UdpReceiveResult answer;
                try { answer = await socket.ReceiveAsync(deadline.Token).ConfigureAwait(false); }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused)
                {
                    // An adapter can report an ICMP unreachable while another still has a lobby.
                    continue;
                }
                var data = answer.Buffer;
                var peer = answer.RemoteEndPoint;
                if (peer.Port != LobbyConnection.Port || !LobbyConnection.IsPrivate(peer.Address) ||
                    data.Length <= QuerySize || data.Length > QuerySize + 128 || !data.AsSpan().StartsWith(Reply) ||
                    !data.AsSpan(8, 16).SequenceEqual(query.AsSpan(8))) continue;
                var name = Encoding.UTF8.GetString(data, QuerySize, data.Length - QuerySize);
                if (LobbyConnection.ValidText(name, 32)) found[peer.Address] = new(name, peer.Address);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
        return found.Values.OrderBy(host => host.Name).ThenBy(host => host.Address.ToString()).ToArray();
    }
}
