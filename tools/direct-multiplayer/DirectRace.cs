using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EgoEngineLibrary.Data;

namespace DiRT2VR.Multiplayer;

public class DirectRace
{
    private static readonly Regex Ticket = new(@"\A([0-9a-f]{16}) ([0-9a-f]{16}) ([0-9a-f]{76})\z", RegexOptions.CultureInvariant);
    private readonly TaskCompletionSource<string> start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool returnRequested;
    private volatile bool resultReturn;
    internal DirectRaceProgress Progress { get; private set; } = new();
    internal Action? LocalResultReady { get; set; }
    internal bool RecordLapTelemetry { get; set; }
    internal Action<ProcessStartInfo,string>? ConfigureVr { get; set; }
    internal string? LayoutInstallRoot { get; set; }
    internal string? RunDirectory { get; set; }
    internal bool ReturnRequested => returnRequested;
    internal void RequestReturn(bool result = false)
    {
        if (result) resultReturn = true;
        returnRequested = true;
    }
    public static bool ValidTicket(string? value) => value != null && Ticket.IsMatch(value);
    public void Launch(string ticket) => start.TrySetResult(ticket);

    internal static string PayloadHash(string install, string name)
    {
        string manifest = Path.Combine(install, "DiRT2VR", "package.json");
        PrivateGame.PlainPath(manifest);
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var files = json.RootElement.GetProperty("Files");
        if (!files.TryGetProperty("DiRT2VR/payload/"+name,out var entry) || entry.GetString() is not string hash || !Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z"))
            throw new IOException("Install the complete Experimental package with Direct multiplayer.");
        return hash.ToUpperInvariant();
    }

    public static string PackageIdentity(string install)
    {
        var hashes = new List<string>();
        foreach (var role in new[] { "host", "client" })
        {
            string name = $"direct-{role}.dll";
            string hash = PayloadHash(install,name);
            var file = Path.Combine(install,"DiRT2VR","payload",name);
            PrivateGame.PlainPath(file);
            if (!PrivateGame.Hash(file).Equals(hash,StringComparison.OrdinalIgnoreCase)) throw new IOException($"The Direct {role} library differs from this package.");
            hashes.Add(hash);
        }
        // Lobby messages can change between builds that use the same native race libraries.
        return $"D2DIRECT13-{typeof(DirectRoom).Module.ModuleVersionId:N}/" + string.Join("/",hashes);
    }

    // A Windows mutex must be released on its acquiring thread. Keep it through
    // preparation, the launch barrier, and the owned game's entire lifetime.
    public virtual Task<int?> RunAsync(string gameRoot, string packageRoot, string userRoot, bool host, Grid grid, IPAddress peer,
                         Action<string> status, Action prepared, Action<string> ticketReady, CancellationToken cancel, Action? started = null)
        => Task.Factory.StartNew(() => Run(gameRoot,packageRoot,userRoot,host,grid,peer,status,prepared,ticketReady,cancel,started),
                                 cancel,TaskCreationOptions.LongRunning,TaskScheduler.Default);

    private int? Run(string stockRoot, string packageRoot, string userRoot, bool host, Grid grid, IPAddress peer,
                     Action<string> status, Action prepared, Action<string> ticketReady, CancellationToken cancel, Action? started)
    {
        if (!grid.CanStart || !Grid.ValidLaps(grid.Laps) || !Grid.ValidSelection(grid.Track,grid.Layout) || !Grid.ValidCar(grid.Host.Car) || !Grid.ValidCar(grid.Guest!.Car) || !LobbyConnection.IsPrivate(peer))
            throw new IOException("The Direct starting grid is incomplete.");
        Progress = new DirectRaceProgress(grid.RequiredLaps);
        using var guard = new Mutex(false,@"Global\DiRT2VR.Session");
        bool held;
        try { held = guard.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Another DiRT 2 session is active.");
        string cache = Path.Combine(userRoot,"Direct");
        try
        {
            foreach (var name in new[]{"dirt2","dirt2_game"})
            {
                var games = Process.GetProcessesByName(name);
                foreach (var running in games) running.Dispose();
                if (games.Length > 0) throw new IOException("Close DiRT 2 before starting Direct.");
            }
            var identity = PackageIdentity(packageRoot).Split('/');
            var (gameRoot, model) = PrepareGame(stockRoot,cache,LayoutInstallRoot ?? packageRoot,grid,status,cancel);
            var role = host ? "host" : "client";
            var run = Path.Combine(cache,"runs", role+"-"+Guid.NewGuid().ToString("N"));
            PrivateGame.PlainPath(run);
            Directory.CreateDirectory(run);
            var profile = PrivateGame.Snapshot(userRoot,run);
            using var telemetry = RecordLapTelemetry ? LapTelemetry.Open(run,gameRoot) : null;
            var localDll = Path.Combine(gameRoot,"xlive.dll");
            var temp = Path.Combine(cache,Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                File.Copy(Path.Combine(packageRoot,"DiRT2VR","payload",$"direct-{role}.dll"),temp);
                if (PrivateGame.Hash(temp) != identity[host ? 1 : 2]) throw new IOException("Direct library changed during preparation.");
                File.Move(temp,localDll,true);
            }
            finally { File.Delete(temp); }
            var config = Path.Combine(run,"xlln.ini");
            File.WriteAllLines(config,["[XLLN-Config-Version:1.6.2.1]", "xlive_username_p1 = D2"+Guid.NewGuid().ToString("N")[..10],
                "xlive_user_auto_login_p1 = 1","xlive_user_live_enabled_p1 = 0","xlive_user_online_enabled_p1 = 0",
                "xlive_net_disable = 0","xlive_xhv_engine_enabled = 0","xlln_debug_log_level = 0x00000000",
                "xlln_network_instance_base_port = 39000",$"xlln_network_instance_port = {(host?39001:39002)}",
                $"xlive_broadcast_address = {peer}:{(host?39002:39001)}","xlive_fps_limit = 60"]);
            var exe = Path.Combine(gameRoot,"dirt2_game.exe");
            var log = Path.Combine(run,"direct.log");
            var psi = new ProcessStartInfo(Path.Combine(gameRoot,"dirt2.exe")) { WorkingDirectory=gameRoot,UseShellExecute=false };
            foreach (var key in psi.Environment.Keys.Where(k=>k.StartsWith("DIRT2VR_",StringComparison.OrdinalIgnoreCase)).ToArray()) psi.Environment.Remove(key);
            var env = psi.Environment;
            env["DIRT2VR_ACTIVE"]="0"; env["DIRT2VR_DIRECT_LAB_EXE"]=exe; env["DIRT2VR_LAN_CONFIG"]=config;
            env["DIRT2VR_PROFILE_ROOT"]=Path.Combine(run,"career"); env["DIRT2VR_LAN_DOCUMENTS"]=Path.Combine(run,"Documents");
            env["DIRT2VR_OUTPUT"]=run; env["DIRT2VR_ENGINE_EXPECTED_NAME"]=profile; env["DIRT2VR_ENGINE_CAREER_LOG"]=log;
            ConfigureGrid(psi,grid,host,model,gameRoot,run);
            foreach (var key in new[]{"ACTIVATION_CANDIDATE","LOADED_ACK_CANDIDATE","FINALIZATION_CANDIDATE","INITIAL_LOAD","INITIAL_CONFIG","EVENT_PROBE","CAREER","BOOTSTRAP_HOLD"})
                env["DIRT2VR_ENGINE_"+key]="1";
            if (ConfigureVr != null)
            {
                PrivateVrRenderer.Prepare(cache,packageRoot);
                ConfigureVr(psi,run);
            }
            cancel.ThrowIfCancellationRequested();
            prepared();
            string ticket = start.Task.WaitAsync(TimeSpan.FromMinutes(12),cancel).GetAwaiter().GetResult();
            if (!host && !ValidTicket(ticket)) throw new IOException("Invalid Direct host ticket.");
            if (!host)
            {
                var parts = ticket.Split(' ');
                env["DIRT2VR_ENGINE_HOST_XUID"]=parts[0]; env["DIRT2VR_ENGINE_JOIN_KEY"]=parts[1]; env["DIRT2VR_ENGINE_JOIN_ADDRESS"]=parts[2];
            }
            status("Starting DiRT 2…");
            cancel.ThrowIfCancellationRequested();
            DateTime began = DateTime.UtcNow;
            using var wrapper = Process.Start(psi) ?? throw new IOException("DiRT 2 did not start.");
            RunDirectory = run;
            // Resolve the launched game even if the lobby closes during wrapper startup.
            using var game = FindGame(exe,began) ?? throw new IOException("The private DiRT 2 game did not start. Logs: "+run);
            try
            {
                Observe(game,run,host,status,ticketReady,cancel);
                started?.Invoke();
                return WaitForReturn(game,run,status,telemetry);
            }
            finally
            {
                if (!game.HasExited)
                {
                    status("Close the Direct game window to return to the lobby.");
                    if (!returnRequested) game.CloseMainWindow();
                    game.WaitForExit();
                }
            }
        }
        finally
        {
            try
            {
                try { if (ConfigureVr!=null) PrivateVrRenderer.Recover(cache); }
                finally { if (grid.Layout!=null) PrivateCircuit.Recover(cache); }
            }
            finally { guard.ReleaseMutex(); }
        }
    }

    internal static (string Root, int Model) PrepareGame(string stockRoot, string cache, string layoutRoot,
        Grid grid, Action<string> status, CancellationToken cancel)
    {
        if (!Grid.ValidLaps(grid.Laps) || !Grid.ValidSelection(grid.Track,grid.Layout) || (grid.Layout!=null && grid.Agreement!=LayoutAgreement.Matching))
            throw new IOException("Both PCs must agree on the selected circuit before preparation.");
        string root = PrivateGame.Prepare(stockRoot,cache,status,cancel);
        if (grid.Layout==null) return (root,grid.Track);
        status("Preparing the selected circuit in the private game folder…");
        return (root,PrivateCircuit.Activate(cache,layoutRoot,grid.Layout,cancel).Model);
    }

    internal static void ConfigureGrid(ProcessStartInfo start, Grid grid, bool host, int model, string gameRoot, string run)
    {
        var guest = grid.Guest ?? throw new IOException("The Direct starting grid needs two players.");
        if (grid.Layout == null && model != grid.Track)
            throw new IOException("The prepared track does not match the lobby selection.");
        int race = 1, vehicleClass = 1;
        if (!RallycrossCatalog.Supports(grid))
        {
            if (grid.Layout?.ExternalCircuit != null || grid.Track == DirectStockCatalog.PrototypeId ||
                grid.Host.CustomCar != null || guest.CustomCar != null)
                throw new IOException("The selected custom content has no Direct race setup.");
            string schema = CircuitCatalog.WriteSchema(run);
            try
            {
                using var database = new DatabaseFile(Path.Combine(gameRoot, "database", "database.bin"), schema);
                string? discipline = grid.Layout == null ? null : grid.IsCircuit ? "rallycross" : "raid";
                (race, vehicleClass) = CircuitCatalog.RaceEvent(database, model, grid.Host.Car, guest.Car, discipline);
            }
            finally { File.Delete(schema); }
        }
        start.Environment["DIRT2VR_ENGINE_VEHICLE_ID"] = (host ? grid.Host.Car : guest.Car).ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["DIRT2VR_ENGINE_LAPS"] = grid.RequiredLaps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["DIRT2VR_ENGINE_RX_REQUEST"] = JsonSerializer.Serialize(new { native_race = race, native_class = vehicleClass, track_model = model });
    }

    internal int? WaitForReturn(Process game,string run,Action<string> status,LapTelemetry? telemetry)
    {
        StreamWriter? record = null;
        try
        {
            try { record = new StreamWriter(Path.Combine(run,"race-exit.log")) { AutoFlush=true }; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status("Race log unavailable: "+ex.Message); }
            void Write(string line)
            {
                try { record?.WriteLine($"{DateTime.UtcNow:O} {line}"); }
                catch (IOException) { /* Logging must never close a running game. */ }
            }
            Write($"Green. Requested laps: {Progress.RequiredLaps}. Lap telemetry: {(telemetry==null ? "off" : "on")}.");
            // We discovered the wrapper's child by PID. Process.ExitCode needs a
            // handle retained while it is alive, even after WaitForExit succeeds.
            try { _ = game.SafeHandle; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { Write("Could not retain the process handle: "+ex.Message); }
            bool closeSent = false;
            bool resultSent = false;
            using var native = new DirectRaceProgress.Tail(Path.Combine(run,"direct.log"),Progress);
            while (!game.WaitForExit(150))
            {
                telemetry?.Poll(Write);
                native.Poll();
                if (Progress.LocalResultReady && !resultSent)
                {
                    resultSent = true;
                    LocalResultReady?.Invoke();
                }
                if (!returnRequested || closeSent) continue;
                closeSent = true;
                bool requested = false;
                try { game.Refresh(); requested = game.CloseMainWindow(); }
                catch (InvalidOperationException) when (game.HasExited) { }
                catch (System.ComponentModel.Win32Exception ex) { Write("Window close failed: "+ex.Message); }
                string reason = resultReturn ? "Both PCs confirmed the race result." : "Both drivers requested return.";
                Write($"{reason} Window close sent: {requested}.");
                status(reason+" If DiRT 2 stays open, close its window.");
            }
            native.Poll();
            telemetry?.Poll(Write);
            if (telemetry != null) Write($"Motion packets received: {telemetry.Received}.");
            int? exitCode = null;
            try { exitCode = game.ExitCode; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { Write("Process exited; exit code unavailable: "+ex.Message); }
            if (exitCode.HasValue) Write($"Process exited: 0x{exitCode.Value:X8}.");
            return exitCode;
        }
        finally { try { record?.Dispose(); } catch (IOException) { } }
    }

    static Process? FindGame(string exe,DateTime began)
    {
        while (DateTime.UtcNow-began < TimeSpan.FromSeconds(45))
        {
            foreach (var p in Process.GetProcessesByName("dirt2_game"))
            {
                try { if (p.StartTime.ToUniversalTime() >= began && string.Equals(p.MainModule?.FileName,exe,StringComparison.OrdinalIgnoreCase)) return p; }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                p.Dispose();
            }
            Thread.Sleep(100);
        }
        return null;
    }

    static void Observe(Process game,string run,bool host,Action<string> status,Action<string> ticketReady,CancellationToken cancel)
    {
        string path = Path.Combine(run,"direct.log");
        var limit = Stopwatch.StartNew();
        StreamReader? reader = null;
        bool sent = false;
        try
        {
            while (limit.Elapsed < TimeSpan.FromMinutes(3))
            {
                cancel.ThrowIfCancellationRequested();
                if (reader == null && File.Exists(path)) reader = new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite));
                while (reader?.ReadLine() is string line)
                {
                    int at = line.IndexOf("] DIRECT_HOST ",StringComparison.Ordinal);
                    if (host && !sent && at >= 0)
                    {
                        string value = line[(at+14)..];
                        if (!ValidTicket(value)) throw new IOException("Invalid native host ticket. Logs: "+run);
                        VerifyCareerMount(run);
                        sent = true; ticketReady(value);
                    }
                    if (line.Contains("] ACT stage=4 status=3 ") && line.EndsWith(" green=1",StringComparison.Ordinal))
                    { VerifyCareerMount(run); return; }
                    if (!host && line.Contains("] SEARCH complete ") && line.Contains(" count=0 "))
                        throw new IOException("DiRT 2 could not find the host. Check private LAN access for UDP 39001/39002.");
                }
                if (game.HasExited) break;
                Thread.Sleep(150);
            }
            throw new IOException("DiRT 2 did not reach the race start. Logs: "+run);
        }
        finally { reader?.Dispose(); }
    }

    static void VerifyCareerMount(string run)
    {
        var receipt = Path.Combine(run,"Documents","profile-ready.txt");
        if (!File.Exists(receipt) || !File.ReadAllText(receipt).Contains("Managed career mount active",StringComparison.Ordinal))
            throw new IOException("DiRT 2 could not confirm the private career. Logs: "+run);
    }
}
