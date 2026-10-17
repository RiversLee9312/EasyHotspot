using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyHotspot.Core;
public record AppSettings(string StartupMode = "none");
public record BackendState(HotspotState? Hotspot, bool Relay, string Mode, string[] Logs, string? Error);
public sealed class Backend : IAsyncDisposable
{
    public readonly HotspotController Hotspot = new();
    public readonly RelayServer Relay;
    readonly SemaphoreSlim gate = new(1,1);
    readonly ConcurrentQueue<string> logs = new();
    public string Mode { get; private set; } = "offline";
    public Backend() { Relay = new RelayServer(Log); }
    public void Log(string value) { logs.Enqueue($"{DateTime.Now:HH:mm:ss}  {value}"); while (logs.Count > 40) logs.TryDequeue(out _); }
    public BackendState Read() { try { return new(Hotspot.Read(), Relay.Running, Mode, logs.ToArray(), null); } catch(Exception ex) { return new(null, Relay.Running, Mode, logs.ToArray(), ex.Message); } }
    public async Task StartAsync(string mode)
    {
        if (mode is not ("offline" or "sharing" or "proxy")) throw new ArgumentException("Error_InvalidMode");
        await gate.WaitAsync();
        try {
            await Hotspot.StartAsync(mode);
            try { if (mode == "proxy") await Relay.StartAsync(HotspotController.FindHotspotAddress()); }
            catch { await Hotspot.StopAsync(); throw; }
            Mode = mode; Log(mode == "proxy" ? "本地热点与代理已开启" : "移动热点已开启");
        } finally { gate.Release(); }
    }
    public async Task StopAsync() { await gate.WaitAsync(); try { await Relay.StopAsync(); await Hotspot.StopAsync(); Log("热点与本地代理已关闭"); } finally { gate.Release(); } }
    public async ValueTask DisposeAsync() { await Relay.StopAsync(); gate.Dispose(); }
}
