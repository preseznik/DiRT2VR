Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports DiRT2VR

Module LanBrowserTests
    Private Function Reply(nonce As ULong) As Byte()
        Dim bytes(103) As Byte
        Encoding.ASCII.GetBytes("D2VRLAN!").CopyTo(bytes, 0)
        BitConverter.GetBytes(nonce).CopyTo(bytes, 8)
        BitConverter.GetBytes(2UI).CopyTo(bytes, 16)
        BitConverter.GetBytes(CUShort(39000)).CopyTo(bytes, 20)
        BitConverter.GetBytes(2UI).CopyTo(bytes, 24) : BitConverter.GetBytes(8UI).CopyTo(bytes, 28)
        BitConverter.GetBytes(123UL).CopyTo(bytes, 32)
        Encoding.ASCII.GetBytes("Test host").CopyTo(bytes, 40)
        Return bytes
    End Function
    Public Sub Run(check As Action(Of Boolean, String))
        Dim endpoint As New IPEndPoint(IPAddress.Parse("192.168.1.20"), LanBrowser.Port)
        Dim packet = Reply(77UL), host = LanBrowser.ParseReply(packet, endpoint, 77UL)
        check(host IsNot Nothing AndAlso host.Joinable AndAlso host.Endpoint.Port = 39000 AndAlso host.Players = 2 AndAlso host.Name = "Test host", "browser decodes native wire layout and host game endpoint")
        check(LanBrowser.ParseReply(packet, endpoint, 78UL) Is Nothing AndAlso LanBrowser.ParseReply(packet.Take(103).ToArray(), endpoint, 77UL) Is Nothing, "browser rejects stale nonce and truncated packets")
        packet(16) = 3 : check(LanBrowser.ParseReply(packet, endpoint, 77UL) Is Nothing, "browser rejects incompatible protocol")
        packet = Reply(77UL) : packet(24) = 9
        check(LanBrowser.ParseReply(packet, endpoint, 77UL) Is Nothing, "browser rejects impossible player counts")
        packet = Reply(77UL) : packet(22) = 1
        check(Not LanBrowser.ParseReply(packet, endpoint, 77UL).Joinable, "racing sessions cannot be joined")
        packet(22) = 2
        check(LanBrowser.ParseReply(packet, endpoint, 77UL).Advertised AndAlso LanBrowser.ParseReply(packet, endpoint, 77UL).Joinable, "XLocator hosts remain joinable without claiming a race state")
        packet(22) = 3
        check(LanBrowser.ParseReply(packet, endpoint, 77UL) Is Nothing, "HOST intent cannot claim player counts")
        packet(24) = 0 : packet(28) = 0
        Dim hostGame = LanBrowser.ParseReply(packet, endpoint, 77UL)
        check(hostGame IsNot Nothing AndAlso hostGame.HostGame AndAlso hostGame.Joinable AndAlso hostGame.Capacity = 0, "HOST game remains selectable without claiming a lobby or capacity")
        host.SeenUtc = DateTime.UtcNow.AddSeconds(-13)
        check(Not host.Joinable, "stale displayed hosts cannot be joined")
        packet = Reply(77UL) : packet(24) = 8
        check(Not LanBrowser.ParseReply(packet, endpoint, 77UL).Joinable, "full sessions cannot be joined")
        For Each invalid In {"8.8.8.8:39000", "127.0.0.1:0", "[::1]:39000", "host;command", "192.168.1.2:99999"}
            Dim rejected As Boolean
            Try
                LanBrowser.ParseEndpoint(invalid)
            Catch ex As IO.IOException
                rejected = True
            End Try
            check(rejected, "join rejects invalid or non-LAN target: " & invalid)
        Next
        LoopbackAsync(check).GetAwaiter().GetResult()
    End Sub
    Private Async Function LoopbackAsync(check As Action(Of Boolean, String)) As Task
        Using server As New UdpClient(New IPEndPoint(IPAddress.Loopback, 0)), deadline As New CancellationTokenSource(3000)
            Dim endpoint = DirectCast(server.Client.LocalEndPoint, IPEndPoint)
            Dim scan = LanBrowser.ScanAsync(deadline.Token, {endpoint}, 400)
            Dim query = Await server.ReceiveAsync(deadline.Token)
            check(query.Buffer.Length = 16 AndAlso Encoding.ASCII.GetString(query.Buffer, 0, 8) = "D2VRLAN?", "real UDP scan sends bounded discovery query")
            Dim packet = Reply(BitConverter.ToUInt64(query.Buffer, 8))
            Await server.SendAsync(packet, query.RemoteEndPoint, deadline.Token)
            Await server.SendAsync(packet, query.RemoteEndPoint, deadline.Token)
            Dim hosts = Await scan
            check(hosts.Count = 1 AndAlso hosts(0).Name = "Test host", "real UDP replies deduplicate and resolve host")
            Dim empty = Await LanBrowser.ScanAsync(deadline.Token, {endpoint}, 100)
            check(empty.Count = 0, "expired hosts disappear from the next scan")
        End Using
    End Function
End Module
