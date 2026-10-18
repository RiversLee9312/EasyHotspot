using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace EasyHotspot.Core;

public sealed class OutboundRouter(SystemProxyResolver resolver,Func<bool>? hasTun=null)
{
    readonly Func<bool> tun=hasTun??(()=>NetworkDiagnostics.ActiveTun() is not null);
    public int ListenerPort{get;set;}
    public string HotspotAddress{get;set;}="192.168.137.1";
    public string Description {
        get {var p=resolver.Settings;return p.Enabled?"Windows system proxy":!string.IsNullOrEmpty(p.Pac)?"System PAC":p.AutoDetect?"System auto proxy":"Windows routes / TUN";}
    }
    public async Task<Stream> ConnectAsync(string host,int port,CancellationToken ct)
    {
        if(port==53 && host==HotspotAddress)host=FindDns().ToString();
        var proxy=await resolver.ResolveAsync(host,port,ct);Validate(proxy);
        return await ProxyTransport.ConnectAsync(host,port,proxy,ct);
    }
    public void Validate(Uri? proxy)
    {
        if(proxy is null)return;
        bool local=proxy.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase) || proxy.Host==HotspotAddress ||
            (IPAddress.TryParse(proxy.Host,out var ip) && IPAddress.IsLoopback(ip));
        if(local && (proxy.Port==ListenerPort || proxy.Port is 18877 or 18080))throw new IOException("Error_UpstreamLoop");
    }
    public async Task<UdpRoute> OpenUdpAsync(string host,int port,CancellationToken ct)
    {
        // Raw UDP follows the already-running TUN. HTTP CONNECT cannot carry UDP.
        // Mixed Clash ports can be negotiated as SOCKS5 without changing Clash.
        if(tun())return await UdpRoute.Direct(host,port,ct);
        var proxy=await resolver.ResolveAsync(host,port,ct);Validate(proxy);
        if(proxy is null)return await UdpRoute.Direct(host,port,ct);
        if(proxy.Scheme is "socks" or "socks5")return await UdpRoute.Socks(proxy,host,port,ct);
        try {return await UdpRoute.Socks(new UriBuilder(proxy){Scheme="socks5"}.Uri,host,port,ct);}
        catch(Exception ex) when(ex is IOException or SocketException){throw new IOException("Error_UpstreamUdp",ex);}
    }
    public async Task<byte[]> DnsAsync(byte[] query,CancellationToken ct)
    {
        // Resolve on the PC's existing DNS path, including a TUN's fake-IP DNS.
        // With only an HTTP system proxy, tunnel DNS over HTTPS instead of
        // sending the phone's DNS requests directly to the physical network.
        var proxy=await resolver.ResolveAsync("cloudflare-dns.com",443,ct);Validate(proxy);
        if(proxy is not null && !tun()) {
            using var handler=new SocketsHttpHandler{UseProxy=false,ConnectCallback=async(_,token)=>await ProxyTransport.ConnectAsync("cloudflare-dns.com",443,proxy,token)};
            using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)};
            using var request=new HttpRequestMessage(HttpMethod.Post,"https://cloudflare-dns.com/dns-query"){Content=new ByteArrayContent(query)};
            request.Content.Headers.ContentType=new("application/dns-message");request.Headers.Accept.ParseAdd("application/dns-message");
            using var response=await http.SendAsync(request,ct);response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        var dns=FindDns();
        using var socket=new UdpClient(dns.AddressFamily);socket.Connect(dns,53);
        await socket.SendAsync(query,ct);var answer=await socket.ReceiveAsync(ct);return answer.Buffer;
    }
    public static IPAddress FindDns()
    {
        var interfaces=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up && !x.Description.Contains("Wi-Fi Direct",StringComparison.OrdinalIgnoreCase));
        var preferred=interfaces.OrderBy(n=>new[]{"tunnel","wintun","wireguard","mihomo","sing-box","clash"}.Any(x=>n.Description.Contains(x,StringComparison.OrdinalIgnoreCase))?0:1);
        return preferred.SelectMany(x=>x.GetIPProperties().DnsAddresses).FirstOrDefault(x=>x.AddressFamily==AddressFamily.InterNetwork && !IPAddress.IsLoopback(x) && !x.Equals(IPAddress.Any))??IPAddress.Parse("1.1.1.1");
    }
}

public sealed class UdpRoute : IDisposable
{
    readonly UdpClient socket;
    readonly TcpClient? lease;
    readonly byte[]? target;
    UdpRoute(UdpClient socket,TcpClient? lease=null,byte[]? target=null){this.socket=socket;this.lease=lease;this.target=target;}
    public static async Task<UdpRoute> Direct(string host,int port,CancellationToken ct)
    {
        var ip=IPAddress.TryParse(host,out var literal)?literal:(await Dns.GetHostAddressesAsync(host,ct)).First();
        var udp=new UdpClient(ip.AddressFamily);udp.Connect(ip,port);return new(udp);
    }
    public static async Task<UdpRoute> Socks(Uri proxy,string host,int port,CancellationToken ct)
    {
        var tcp=new TcpClient();UdpClient? udp=null;
        try {
            await tcp.ConnectAsync(proxy.Host,proxy.Port,ct);var stream=tcp.GetStream();await ProxyTransport.SocksHandshake(stream,proxy,ct);
            await stream.WriteAsync(new byte[]{5,3,0,1,0,0,0,0,0,0},ct);var endpoint=await ProxyTransport.SocksReply(stream,ct);
            var address=endpoint.Host is "0.0.0.0" or "::"?proxy.Host:endpoint.Host;
            var ip=IPAddress.TryParse(address,out var literal)?literal:(await Dns.GetHostAddressesAsync(address,ct)).First();
            udp=new UdpClient(ip.AddressFamily);udp.Connect(ip,endpoint.Port);
            return new(udp,tcp,ProxyTransport.Address(host,port));
        }catch{tcp.Dispose();udp?.Dispose();throw;}
    }
    public async Task SendAsync(byte[] payload,CancellationToken ct)
    {
        var packet=target is null?payload:new byte[]{0,0,0}.Concat(target).Concat(payload).ToArray();await socket.SendAsync(packet,ct);
    }
    public async Task<byte[]> ReceiveAsync(CancellationToken ct)
    {
        while(true) {
            var packet=(await socket.ReceiveAsync(ct)).Buffer;
            if(target is null)return packet;
            if(packet.Length<7 || packet[0]!=0 || packet[1]!=0 || packet[2]!=0)continue;
            int offset=packet[3] switch{1=>10,4=>22,3=>7+packet[4],_=>int.MaxValue};
            // Connected UDP sockets accept replies only from the negotiated relay.
            if(offset<=packet.Length)return packet[offset..];
        }
    }
    public void Dispose(){socket.Dispose();lease?.Dispose();}
}
