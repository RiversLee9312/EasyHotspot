using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyHotspot.Core;
public record AppSettings(string StartupMode = "none",bool DhcpDiscovery=false);
public record BackendState(HotspotState? Hotspot, bool Relay, string Mode, string[] Logs, string? Error,DhcpDiscoveryState Dhcp,string? Tun=null,TransparentState? Transparent=null);
public sealed class Backend : IAsyncDisposable
{
    public readonly HotspotController Hotspot = new();
    public readonly RelayServer Relay;
    public readonly DhcpDiscovery Dhcp;
    public readonly TransparentForwarder Transparent;
    readonly SemaphoreSlim gate = new(1,1);
    readonly ConcurrentQueue<string> logs = new();
    public string Mode { get; private set; } = "offline";
    public Backend() { Relay = new RelayServer(Log);Dhcp=new DhcpDiscovery(Log);Transparent=new(Log); }
    public void Log(string value) { logs.Enqueue($"{DateTime.Now:HH:mm:ss}  {value}"); while (logs.Count > 40) logs.TryDequeue(out _); }
    public BackendState Read() { try { return new(Hotspot.Read(), Relay.Running, Mode, logs.ToArray(), null,Dhcp.Read(),NetworkDiagnostics.ActiveTun(),Transparent.Read()); } catch(Exception ex) { return new(null, Relay.Running, Mode, logs.ToArray(), ex.Message,Dhcp.Read(),Transparent:Transparent.Read()); } }
    public Task CheckProxyAsync(){if(Mode=="transparent")return Transparent.CheckAsync();if(!Relay.Running)throw new InvalidOperationException("Error_ProxyNotRunning");return NetworkDiagnostics.CheckProxyAsync(HotspotController.FindHotspotAddress());}
    public async Task SetDhcpAsync(bool enabled)
    {
        await gate.WaitAsync();
        try {
            if(Hotspot.Read().State=="On")throw new InvalidOperationException("Error_DhcpStopFirst");
            if(enabled)DhcpDiscovery.Probe();else await Dhcp.StopAsync();
            ServiceSettings.SaveDhcp(enabled);
        }finally{gate.Release();}
    }
    public async Task StartAsync(string mode)
    {
        if (mode is not ("offline" or "sharing" or "proxy" or "transparent")) throw new ArgumentException("Error_InvalidMode");
        await gate.WaitAsync();
        try {
            await Hotspot.StartAsync(mode);
            try { if (mode == "proxy") {
                // Tethering can report On before ICS has assigned the hotspot interface's address.
                string address;
                for(int attempt=0;;attempt++) {
                    address=HotspotController.FindHotspotAddress();
                    try {await Relay.StartAsync(address);break;}
                    catch(System.Net.Sockets.SocketException ex) when(ex.SocketErrorCode==System.Net.Sockets.SocketError.AddressNotAvailable && attempt<30){await Task.Delay(500);}
                }
                if(ServiceSettings.Read().DhcpDiscovery)await Dhcp.StartAsync(address);
            } else if(mode=="transparent")await Transparent.StartAsync(HotspotController.FindHotspotAddress()); }
            catch {await Transparent.StopAsync();await Dhcp.StopAsync();await Relay.StopAsync();await Hotspot.StopAsync();throw;}
            Mode = mode; Log(mode == "proxy" ? "本地热点与代理已开启" : "移动热点已开启");
        } finally { gate.Release(); }
    }
    public async Task StopAsync() { await gate.WaitAsync(); try { try{await Transparent.StopAsync();}finally{try{await Dhcp.StopAsync();}finally{try{await Relay.StopAsync();}finally{await Hotspot.StopAsync();}}}Log("热点与本地代理已关闭"); } finally { gate.Release(); } }
    public async ValueTask DisposeAsync() { await Transparent.DisposeAsync();await Dhcp.DisposeAsync();await Relay.StopAsync(); gate.Dispose(); }
}
