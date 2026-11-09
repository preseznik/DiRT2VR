using System.Net;
using System.Net.Sockets;

namespace DiRT2VR.Multiplayer;

// Owned by the launcher UI thread. Network continuations and native worker
// notifications return to that same context before they change lobby state.
public sealed class DirectRoom : IDisposable
{
    readonly string gameRoot, packageRoot, userRoot, name, identity;
    readonly SynchronizationContext ui;
    readonly CancellationTokenSource stop = new();
    TcpListener? listener;
    LobbyConnection? connection;
    DirectRace? race;
    string? previousRunDirectory;
    bool hostPrepared, localDone, remoteDone, launching, resetting, disposed;
    string? guestPreparedGrid;
    bool onTrack, localReturn, remoteReturn;
    bool localResultReady, remoteResultReady;
    readonly string layoutRoot;
    CancellationTokenSource? layoutCheck;
    CancellationTokenSource? carCheck;
    (int Revision, DirectCustomCar? Host, DirectCustomCar? Guest)? checkedCars;
    bool localCarsMatch;
    DirectLayout? verifiedGuestLayout;
    int checkedRevision = -1;
    int firstCarRevision;
    public bool CheckingLayout => layoutCheck is { IsCancellationRequested: false };
    internal Func<DirectRace> CreateRace { get; init; } = () => new DirectRace();
    readonly Func<Task<bool>> confirmLocalLaunch;
    public Action<System.Diagnostics.ProcessStartInfo,string>? ConfigureLocalVr { get; set; }
    public bool RecordLapTelemetry { get; set; }
    public bool CanRequestReturn => Connected && onTrack && !localDone && !localReturn;
    public Grid Grid { get; private set; }
    public bool IsHost { get; }
    public bool Connected { get; private set; }
    public bool CanConfigure => IsHost && !launching && !disposed && !stop.IsCancellationRequested;
    public bool Starting => launching;
    public bool WorkerActive { get; private set; }
    public string? LastRunDirectory => race?.RunDirectory ?? previousRunDirectory;
    public string Status { get; private set; } = "";
    public event Action? Changed;
    public event Action<string>? Chat;
    public event Action? LocalGameClosed;

    public DirectRoom(bool host,string gameRoot,string userRoot,string name,string packageRoot,Func<Task<bool>> confirmLocalLaunch,string? layoutRoot = null)
    {
        if (!LobbyConnection.ValidText(name,32)) throw new ArgumentException("Enter a player name of 1 to 32 characters.");
        this.gameRoot=gameRoot; this.packageRoot=packageRoot; this.userRoot=userRoot; this.name=name;
        this.layoutRoot=layoutRoot ?? packageRoot;
        this.confirmLocalLaunch=confirmLocalLaunch ?? throw new ArgumentNullException(nameof(confirmLocalLaunch));
        identity=DirectRace.PackageIdentity(packageRoot);
        ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Open a Direct lobby from the launcher.");
        IsHost=host;
        Grid=new(143,new(host?name:"Host",88),null);
    }

    public async Task OpenAsync(string address)
    {
        TcpClient? socket=null;
        try
        {
            if (IsHost)
            {
                listener=new(IPAddress.Any,LobbyConnection.Port);
                listener.Start(1);
                var addresses = Dns.GetHostAddresses(Dns.GetHostName()).Where(LobbyConnection.IsPrivate);
                SetStatus("Waiting for a player. Host IP: "+string.Join(", ",addresses));
                using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var advertise = DirectDiscovery.AdvertiseAsync(name, waiting.Token);
                try { socket=await listener.AcceptTcpClientAsync(stop.Token); }
                finally { waiting.Cancel(); await advertise; }
                listener.Stop();
            }
            else
            {
                if (!IPAddress.TryParse(address,out var ip) || !LobbyConnection.IsPrivate(ip))
                    throw new IOException("Enter the host PC’s private LAN IPv4 address.");
                socket=new TcpClient(AddressFamily.InterNetwork);
                SetStatus("Connecting to "+ip+"…");
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await socket.ConnectAsync(ip,LobbyConnection.Port,timeout.Token);
            }
            connection=new(socket); socket=null;
            using (var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await connection.SendAsync(new("hello",identity),timeout.Token);
                var hello=await connection.ReceiveAsync(timeout.Token);
                if (hello.Kind!="hello" || hello.Text!=identity) throw new IOException("Both players need the same Direct multiplayer build.");
                await connection.SendAsync(new("name",name),timeout.Token);
                var other=await connection.ReceiveAsync(timeout.Token);
                if (other.Kind!="name" || !LobbyConnection.ValidText(other.Text,32)) throw new IOException("Invalid Direct player name.");
                if (IsHost)
                {
                    Grid=Grid with { Host=Grid.Host with { Ready=false }, Guest=new(other.Text,85),
                        Agreement=LayoutAgreement.Pending, Revision=checked(Grid.Revision+1) };
                    await connection.SendAsync(new("grid",Grid:Grid),timeout.Token);
                }
                else
                {
                    var initial=await connection.ReceiveAsync(timeout.Token);
                    if (initial.Kind!="grid" || !ValidGrid(initial.Grid) || initial.Grid!.Host.Name!=other.Text || initial.Grid.Guest!.Name!=name)
                        throw new IOException("Invalid initial Direct grid.");
                    Grid=initial.Grid;
                }
            }
            Connected=true;
            if (IsHost) await Publish(); // Include edits made while the initial handshake was in flight.
            SetStatus("Lobby open. Choose your cars and mark yourselves ready.");
            if (!IsHost) { _=CheckGuestLayout(Grid); _=CheckCars(); }
            while (!stop.IsCancellationRequested) await Receive(await connection.ReceiveAsync(stop.Token));
        }
        catch(Exception ex) when (ex is IOException or InvalidDataException or SocketException or OperationCanceledException or System.Text.Json.JsonException or ObjectDisposedException)
        {
            if (!disposed && !stop.IsCancellationRequested) SetStatus(ex is EndOfStreamException ? "The other player left the lobby." : ex.Message);
        }
        finally
        {
            socket?.Dispose(); listener?.Stop(); connection?.Dispose();
            Connected=false; stop.Cancel(); Changed?.Invoke();
        }
    }

    Task Send(LobbyMessage message) => connection!.SendAsync(message,stop.Token);
    void SetStatus(string value) { Status=value; Changed?.Invoke(); }
    void LobbyOnly()
    {
        if (!Connected || launching) throw new IOException("The lobby is unavailable.");
    }
    void SelectionOnly()
    {
        if (IsHost && CanConfigure) return;
        LobbyOnly();
    }
    public async Task SelectAsync(int car,int track,DirectCustomCar? customCar = null)
    {
        SelectionOnly();
        if (IsHost)
        {
            layoutCheck?.Cancel();
            await PublishSelection(Grid.Select(true,car,track,track==0 ? Grid.Layout : null,customCar));
        }
        else await Send(new("select",Car:car,Revision:Grid.Revision,CustomCar:customCar));
    }

    public async Task SelectLayoutAsync(int car,string id,DirectCustomCar? customCar = null)
    {
        SelectionOnly();
        if (!IsHost) throw new IOException("Only the host can select the track.");
        layoutCheck?.Cancel();
        using var check=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        layoutCheck=check;
        Changed?.Invoke();
        try
        {
            var layout=await Task.Run(()=>DirectLayout.Read(layoutRoot,id,check.Token,gameRoot),check.Token);
            check.Token.ThrowIfCancellationRequested();
            SelectionOnly();
            var updated=Grid.Select(true,car,0,layout,customCar);
            if (updated==Grid && Grid.Agreement==LayoutAgreement.Different)
                updated=Grid with { Agreement=LayoutAgreement.Pending, Revision=checked(Grid.Revision+1) };
            await PublishSelection(updated);
        }
        finally { if (layoutCheck==check) { layoutCheck=null; Changed?.Invoke(); } }
    }

    public Task SetLapsAsync(int laps)
    {
        SelectionOnly();
        if (!IsHost) throw new IOException("Only the host can set the laps.");
        return PublishSelection(Grid.SetLaps(laps));
    }

    async Task CheckGuestLayout(Grid selection)
    {
        checkedRevision=selection.Revision;
        if (selection.Agreement==LayoutAgreement.Matching && verifiedGuestLayout==selection.Layout) return;
        layoutCheck?.Cancel();
        verifiedGuestLayout=null;
        if (selection.Layout is not { } expected) return;
        using var check=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        layoutCheck=check;
        try
        {
            DirectLayout? localLayout;
            try { localLayout=await Task.Run(()=>DirectLayout.Read(layoutRoot,expected.Id,check.Token,gameRoot),check.Token); }
            catch(Exception ex) when (ex is IOException or UnauthorizedAccessException) { localLayout=null; }
            if (check.IsCancellationRequested || Grid.Revision!=selection.Revision) return;
            if (localLayout==expected) verifiedGuestLayout=localLayout;
            await Send(new("layout-check",Ready:localLayout==expected,Revision:selection.Revision));
        }
        catch(OperationCanceledException) when(check.IsCancellationRequested) { }
        catch(Exception ex) { if (!stop.IsCancellationRequested) Abort(ex.Message); }
        finally { if (layoutCheck==check) layoutCheck=null; }
    }
    public async Task ReadyAsync(bool ready)
    {
        LobbyOnly();
        if (IsHost) { Grid=Grid.SetReady(true,ready,Grid.Revision); await Publish(); }
        else await Send(new("ready",Ready:ready,Revision:Grid.Revision));
    }
    // Both PCs check both cars. Changing only laps or layout keeps the same file checks.
    async Task CheckCars()
    {
        var cars = (Revision: Grid.CarRevision, Host: Grid.Host.CustomCar, Guest: Grid.Guest?.CustomCar);
        if (!Connected || cars == checkedCars) return;
        checkedCars = cars;
        carCheck?.Cancel();
        localCarsMatch = !Grid.HasCustomCars;
        if (localCarsMatch) return;
        using var check = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        carCheck = check;
        try
        {
            bool match;
            try
            {
                match = await Task.Run(() =>
                    (cars.Host?.Matches(layoutRoot, gameRoot, check.Token) ?? true) &&
                    (cars.Guest == cars.Host || (cars.Guest?.Matches(layoutRoot, gameRoot, check.Token) ?? true)), check.Token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { match = false; }
            if (check.IsCancellationRequested) return;
            localCarsMatch = match;
            if (IsHost)
            {
                Grid = Grid with { HostCarsMatch = match };
                await Publish();
            }
            else await Send(new("cars-checked", Ready: match, Revision: cars.Revision));
        }
        catch (OperationCanceledException) when (check.IsCancellationRequested) { }
        catch (Exception ex) { if (!stop.IsCancellationRequested) Abort(ex.Message); }
        finally { if (carCheck == check) carCheck = null; }
    }

    async Task Publish()
    {
        Changed?.Invoke();
        if (!Connected) return;
        _=CheckCars();
        await Send(new("grid",Grid:Grid));
    }
    Task PublishSelection(Grid updated)
    {
        if (updated.Track!=Grid.Track || updated.Layout!=Grid.Layout) firstCarRevision=updated.Revision;
        Grid=updated;
        return Publish();
    }
    public async Task ChatAsync(string text)
    {
        text=text.Trim();
        if (!Connected || !LobbyConnection.ValidText(text,200)) throw new IOException("Enter a chat message of 1 to 200 characters.");
        await Send(new("chat",text)); Chat?.Invoke(name+": "+text);
    }
    public async Task StartAsync(bool force)
    {
        LobbyOnly();
        if (CheckingLayout) throw new IOException("The selected layout is still being checked.");
        if (Grid.Layout!=null && Grid.Agreement!=LayoutAgreement.Matching)
            throw new IOException("Both PCs need matching files for the selected custom layout.");
        if (!IsHost || !Grid.CanStart || (!force && !Grid.BothReady)) throw new IOException("The host can start when both players are ready, or choose Force start with two players connected.");
        if (!await AcceptLocalStart()) return;
        await Send(new("start",Grid:Grid));
    }
    async Task<bool> AcceptLocalStart()
    {
        launching=true;
        SetStatus("Waiting to start the race…");
        string reason="Race start cancelled on this PC.";
        try
        {
            if (await confirmLocalLaunch() && Connected && !stop.IsCancellationRequested) return true;
        }
        catch (Exception ex) { reason="Could not confirm this race start: "+ex.Message; }
        launching=false;
        SetStatus(reason);
        return false;
    }
    public async Task ReturnAsync()
    {
        if (!CanRequestReturn) return;
        localReturn=true;
        SetStatus("Return requested. Waiting for the other player…");
        await Send(new("return"));
        if (remoteReturn || remoteDone) race?.RequestReturn();
    }
    static bool ValidGrid(Grid? g) => g?.Guest != null && g.Host != null && Enum.IsDefined(g.Agreement) &&
        g.CarRevision >= 0 && g.CarRevision <= g.Revision && (g.HasCustomCars || (g.HostCarsMatch == null && g.GuestCarsMatch == null)) &&
        Grid.ValidLaps(g.Laps) && (g.Layout!=null || g.Agreement==LayoutAgreement.Pending) && Grid.ValidSelection(g.Track,g.Layout) && Grid.ValidCar(g.Host.Car,g.Host.CustomCar) &&
        Grid.ValidCar(g.Guest.Car,g.Guest.CustomCar) && LobbyConnection.ValidText(g.Host.Name,32) && LobbyConnection.ValidText(g.Guest.Name,32) && g.Revision >= 0;
    async Task Receive(LobbyMessage msg)
    {
        switch(msg.Kind)
        {
            case "chat" when LobbyConnection.ValidText(msg.Text,200):
                Chat?.Invoke((IsHost?Grid.Guest!.Name:Grid.Host.Name)+": "+msg.Text);
                break;
            case "select" when IsHost:
                // Car picks are independent within a circuit and round. Ready marks still require an exact revision.
                if (!launching && msg.Revision>=firstCarRevision && msg.Revision<=Grid.Revision)
                    Grid=Grid.Select(false,msg.Car,Grid.Track,Grid.Layout,msg.CustomCar);
                if (!launching) await Publish();
                break;
            case "ready" when IsHost:
                if (!launching) { Grid=Grid.SetReady(false,msg.Ready,msg.Revision); await Publish(); }
                break;
            case "grid" when !IsHost && !launching && ValidGrid(msg.Grid):
                Grid=msg.Grid!; Changed?.Invoke();
                if (Grid.Revision!=checkedRevision) _=CheckGuestLayout(Grid);
                _=CheckCars();
                break;
            case "cars-checked" when IsHost:
                if (!launching && Grid.HasCustomCars && msg.Revision==Grid.CarRevision)
                {
                    Grid=Grid with { GuestCarsMatch=msg.Ready };
                    await Publish();
                }
                break;
            case "layout-check" when IsHost:
                if (!launching && Grid.Layout!=null && msg.Revision==Grid.Revision)
                {
                    Grid=Grid with { Agreement=msg.Ready ? LayoutAgreement.Matching : LayoutAgreement.Different };
                    await Publish();
                }
                break;
            case "start" when !IsHost && !launching && msg.Grid==Grid && Grid.CanStart && Grid.Layout==verifiedGuestLayout && (!Grid.HasCustomCars || localCarsMatch):
                if (!await AcceptLocalStart())
                {
                    if (Connected && !stop.IsCancellationRequested) await Send(new("start-declined",Revision:Grid.Revision));
                    break;
                }
                await Send(new("start-accepted",Revision:Grid.Revision));
                BeginPreparation();
                break;
            case "start-accepted" when IsHost && launching && race==null && msg.Revision==Grid.Revision:
                BeginPreparation();
                break;
            case "start-declined" when IsHost && launching && race==null && msg.Revision==Grid.Revision:
                launching=false;
                SetStatus("The other player cancelled this race start.");
                break;
            case "prepared" when IsHost && launching && race!=null && guestPreparedGrid==null && LobbyConnection.ValidText(msg.Text,80):
                guestPreparedGrid=msg.Text; StartPreparedHost(race); break;
            case "ticket" when !IsHost && launching && race!=null && DirectRace.ValidTicket(msg.Text):
                race.Launch(msg.Text); break;
            case "return" when launching:
                remoteReturn=true;
                if (localReturn) race?.RequestReturn();
                else if (onTrack && !localDone) SetStatus("The other player wants to return. Choose Return to lobby when you're ready.");
                break;
            case "result" when launching && msg.Revision == Grid.Revision:
                remoteResultReady=true;
                if (localResultReady) race?.RequestReturn(result:true);
                break;
            case "closed" when launching && !remoteDone:
                remoteDone=true;
                ReportExit(IsHost ? Grid.Guest!.Name : Grid.Host.Name,msg.ExitCode);
                if (localReturn) race?.RequestReturn();
                else if (onTrack && !localDone) SetStatus("The other game closed. Choose Return to lobby when you're ready.");
                await FinishWhenBothDone(); break;
            case "reset" when !IsHost && localDone && remoteDone && ValidGrid(msg.Grid):
                Grid=msg.Grid!; Reset(); _=CheckGuestLayout(Grid); break;
            case "error" when LobbyConnection.ValidText(msg.Text,200):
                throw new IOException("Other PC: "+msg.Text);
            default: throw new IOException("Unexpected Direct lobby message.");
        }
    }

    void BeginPreparation()
    {
        race=CreateRace();
        var current=race;
        current.LayoutInstallRoot=layoutRoot;
        current.ConfigureVr=ConfigureLocalVr;
        current.LocalResultReady=()=>Post(async ()=>{
            if (race!=current || localDone || localResultReady) return;
            localResultReady=true;
            await Send(new("result",Revision:Grid.Revision));
            if (remoteResultReady) current.RequestReturn(result:true);
        });
        race.RecordLapTelemetry=RecordLapTelemetry;
        WorkerActive=true;
        SetStatus("Preparing the private game copy…");
        _=ObserveRace(race);
    }
    void Post(Func<Task> callback) => ui.Post(async _ =>
    {
        if (disposed || stop.IsCancellationRequested) return;
        try { await callback(); } catch(Exception ex) { Abort(ex.Message); }
    },null);
    void ReportExit(string player, int? code) => Chat?.Invoke(code is null or 0 ? $"{player} closed DiRT 2." : $"{player}’s game exited with code 0x{code:X8}.");

    async Task ObserveRace(DirectRace current)
    {
        try
        {
            int? exitCode = await current.RunAsync(gameRoot,packageRoot,userRoot,IsHost,Grid,connection!.Peer,
                s=>Post(()=>{if (race==current && !localDone) SetStatus(s);return Task.CompletedTask;}),
                ()=>Post(async ()=>{
                    SetStatus("Game files ready. Waiting for the other PC…");
                    if (race!=current || current.PreparedGrid==null) throw new IOException("The local race grid was not prepared.");
                    if (IsHost) { hostPrepared=true; StartPreparedHost(current); }
                    else await Send(new("prepared",current.PreparedGrid));
                }),
                ticket=>Post(()=>Send(new("ticket",ticket))),stop.Token,()=>Post(()=>{
                    if (race==current && !localDone)
                    {
                        onTrack=true;
                        SetStatus(remoteReturn || remoteDone ? "The other player is ready to return. Choose Return to lobby when you're ready." :
                            "Race started. Quit DiRT 2 at any time to return to this room.");
                    }
                    return Task.CompletedTask;
                }));
            WorkerActive=false;
            localDone=true;
            if (current.Progress.SawSample)
            {
                var progress=current.Progress;
                Chat?.Invoke(progress.Local.Summary(name));
                Chat?.Invoke(progress.Peer.Summary(IsHost ? Grid.Guest!.Name : Grid.Host.Name));
            }
            ReportExit(name,exitCode);
            SetStatus("Game closed. Waiting for the other player…");
            LocalGameClosed?.Invoke();
            if (Connected) { await Send(new("closed",ExitCode:exitCode)); await FinishWhenBothDone(); }
        }
        catch(Exception ex)
        {
            if (Connected && !stop.IsCancellationRequested)
            {
                try { await Send(new("error","Direct race startup or play ended on the other PC.")); } catch (Exception) { }
                Abort(ex.Message);
            }
        }
        finally
        {
            if (current.RunDirectory != null) previousRunDirectory = current.RunDirectory;
            if (race==current) WorkerActive=false;
            Changed?.Invoke();
        }
    }
    void StartPreparedHost(DirectRace current)
    {
        if (!hostPrepared || guestPreparedGrid==null) return;
        if (guestPreparedGrid!=current.PreparedGrid)
            throw new IOException("The two PCs prepared different race settings. Check the cars and layout on both PCs.");
        current.Launch("");
    }
    async Task FinishWhenBothDone()
    {
        if (!IsHost || !localDone || !remoteDone || resetting) return;
        resetting=true;
        Grid=Grid with {Host=Grid.Host with {Ready=false},Guest=Grid.Guest! with {Ready=false},
            Revision=checked(Grid.Revision+1),Agreement=LayoutAgreement.Pending,
            CarRevision=Grid.HasCustomCars ? checked(Grid.Revision+1) : Grid.CarRevision,HostCarsMatch=null,GuestCarsMatch=null};
        firstCarRevision=Grid.Revision;
        await Send(new("reset",Grid:Grid)); Reset();
    }
    void Reset()
    {
        launching=resetting=hostPrepared=localDone=remoteDone=false; guestPreparedGrid=null; race=null;
        onTrack=localReturn=remoteReturn=localResultReady=remoteResultReady=false;
        _=CheckCars();
        SetStatus("Both players returned. Choose cars for the next race.");
    }
    void Abort(string reason)
    {
        SetStatus(reason); stop.Cancel(); connection?.Dispose();
    }
    public void Dispose()
    {
        disposed=true;
        stop.Cancel(); listener?.Stop(); connection?.Dispose();
    }
}
