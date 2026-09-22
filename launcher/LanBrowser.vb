Imports System.Net
Imports System.Net.Sockets
Imports System.Net.NetworkInformation
Imports System.Text
Imports System.Threading.Tasks

Public Class LanHost
    Public Property Name As String
    Public Property Endpoint As IPEndPoint
    Public Property Players As UInteger
    Public Property Capacity As UInteger
    Public Property Racing As Boolean
    Public Property Advertised As Boolean
    Public Property HostGame As Boolean
    Public Property SessionId As ULong
    Public Property SeenUtc As DateTime = DateTime.UtcNow
    Public ReadOnly Property Joinable As Boolean
        Get
            Return Not Racing AndAlso (HostGame OrElse Players < Capacity) AndAlso DateTime.UtcNow - SeenUtc < TimeSpan.FromSeconds(12)
        End Get
    End Property
End Class

Public Module LanBrowser
    Public Const Port As Integer = 39820
    Public Function ParseEndpoint(value As String) As IPEndPoint
        Dim endpoint As IPEndPoint = Nothing
        If Not IPEndPoint.TryParse(value, endpoint) OrElse endpoint.AddressFamily <> AddressFamily.InterNetwork OrElse endpoint.Port < 1 OrElse Not IsLocal(endpoint.Address) Then Throw New IOException("Choose a valid LAN IPv4 address and port.")
        Return endpoint
    End Function
    Public Function IsLocal(address As IPAddress) As Boolean
        Dim b = address.GetAddressBytes()
        Return b.Length = 4 AndAlso (b(0) = 10 OrElse b(0) = 127 OrElse (b(0) = 172 AndAlso b(1) >= 16 AndAlso b(1) <= 31) OrElse (b(0) = 192 AndAlso b(1) = 168) OrElse (b(0) = 169 AndAlso b(1) = 254))
    End Function
    Public Function ParseReply(data As Byte(), sender As IPEndPoint, nonce As ULong) As LanHost
        If data.Length <> 104 OrElse Not IsLocal(sender.Address) OrElse Encoding.ASCII.GetString(data, 0, 8) <> "D2VRLAN!" OrElse BitConverter.ToUInt64(data, 8) <> nonce OrElse BitConverter.ToUInt32(data, 16) <> 2 OrElse data(22) > 3 OrElse data(23) <> 0 Then Return Nothing
        Dim gamePort = BitConverter.ToUInt16(data, 20), players = BitConverter.ToUInt32(data, 24), capacity = BitConverter.ToUInt32(data, 28)
        If gamePort = 0 Then Return Nothing
        If data(22) = 3 Then
            If capacity <> 0 OrElse players <> 0 Then Return Nothing
        ElseIf capacity < 2 OrElse capacity > 8 OrElse players > capacity Then
            Return Nothing
        End If
        Dim name = Encoding.ASCII.GetString(data, 40, 64).Split(ChrW(0))(0)
        name = New String(name.Where(Function(c) Not Char.IsControl(c)).Take(63).ToArray()).Trim()
        Return New LanHost With {.Name = If(name.Length = 0, "DiRT 2 host", name), .Endpoint = New IPEndPoint(sender.Address, gamePort), .Players = players, .Capacity = capacity, .Racing = data(22) = 1, .Advertised = data(22) = 2, .HostGame = data(22) = 3, .SessionId = BitConverter.ToUInt64(data, 32)}
    End Function
    Public Function Targets() As List(Of IPEndPoint)
        Dim addresses As New HashSet(Of IPAddress) From {IPAddress.Broadcast}
        For Each adapter In NetworkInterface.GetAllNetworkInterfaces()
            If adapter.OperationalStatus <> OperationalStatus.Up OrElse adapter.NetworkInterfaceType = NetworkInterfaceType.Loopback Then Continue For
            For Each unicast In adapter.GetIPProperties().UnicastAddresses
                If unicast.Address.AddressFamily <> AddressFamily.InterNetwork OrElse Not IsLocal(unicast.Address) Then Continue For
                Dim ip = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes()
                Dim broadcast(3) As Byte
                For i = 0 To 3
                    broadcast(i) = CByte(CInt(ip(i)) Or (255 Xor CInt(mask(i))))
                Next
                addresses.Add(New IPAddress(broadcast))
            Next
        Next
        Return addresses.Select(Function(a) New IPEndPoint(a, Port)).ToList()
    End Function
    Public Async Function ScanAsync(token As CancellationToken, Optional targetsOverride As IEnumerable(Of IPEndPoint) = Nothing, Optional durationMs As Integer = 2400) As Task(Of List(Of LanHost))
        Using client As New UdpClient(New IPEndPoint(IPAddress.Any, 0)), timeout = CancellationTokenSource.CreateLinkedTokenSource(token)
            client.EnableBroadcast = True
            timeout.CancelAfter(durationMs)
            Dim nonce = BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8))
            Dim query(15) As Byte
            Encoding.ASCII.GetBytes("D2VRLAN?").CopyTo(query, 0) : BitConverter.GetBytes(nonce).CopyTo(query, 8)
            Dim sent As Integer
            For Each target In If(targetsOverride, Targets())
                Try
                    Await client.SendAsync(query.AsMemory(), target, timeout.Token)
                    sent += 1
                Catch ex As SocketException
                    ' A down adapter must not prevent querying the others.
                End Try
            Next
            If sent = 0 Then Throw New IOException("No LAN discovery query could be sent. Check your network connection.")
            Dim hosts As New Dictionary(Of String, LanHost)
            Try
                While Not timeout.IsCancellationRequested
                    Dim packet = Await client.ReceiveAsync(timeout.Token)
                    Dim host = ParseReply(packet.Buffer, packet.RemoteEndPoint, nonce)
                    If host Is Nothing Then Continue While
                    Dim key = host.Endpoint.ToString() & "/" & host.SessionId.ToString()
                    If hosts.Count < 64 OrElse hosts.ContainsKey(key) Then hosts(key) = host
                End While
            Catch ex As OperationCanceledException When Not token.IsCancellationRequested
            End Try
            token.ThrowIfCancellationRequested()
            Return hosts.Values.OrderBy(Function(h) h.Name).ToList()
        End Using
    End Function
End Module
