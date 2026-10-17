using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EasyHotspot.Core;
public sealed class RelayServer(Action<string>? log = null)
{
    TcpListener? proxy, pac;
    CancellationTokenSource? lifetime;
    string address = "192.168.137.1";
    public bool Running => lifetime is { IsCancellationRequested: false };
    public string Pac => $"function FindProxyForURL(url, host) {{ if (host === 'localhost' || host === '127.0.0.1' || host === '{address}' || host === '::1') return 'DIRECT'; return 'PROXY {address}:18877'; }}";
    public Task StartAsync(string ip)
    {
        if (Running) return Task.CompletedTask;
        address = ip; var bind = IPAddress.Parse(ip);
        if (bind.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("热点需要 IPv4 地址。");
        proxy = new(bind,18877); pac = new(bind,18080);
        proxy.Server.ExclusiveAddressUse = pac.Server.ExclusiveAddressUse = true;
        try { proxy.Start(); pac.Start(); } catch { proxy.Stop(); pac.Stop(); throw; }
        lifetime = new(); _ = AcceptAsync(proxy,false,lifetime.Token); _ = AcceptAsync(pac,true,lifetime.Token);
        return Task.CompletedTask;
    }
    bool Allowed(IPAddress ip) => address == "127.0.0.1" ? IPAddress.IsLoopback(ip) :
        ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString().StartsWith(address[..(address.LastIndexOf('.')+1)],StringComparison.Ordinal);
    async Task AcceptAsync(TcpListener listener,bool pacOnly,CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            try { var client = await listener.AcceptTcpClientAsync(ct); _ = ServeAsync(client,pacOnly,ct); }
            catch (Exception) when(ct.IsCancellationRequested) { break; }
            catch(Exception ex) { log?.Invoke("监听连接失败："+ex.GetType().Name); await Task.Delay(500,ct).ConfigureAwait(false); }
        }
    }
    async Task ServeAsync(TcpClient client,bool pacOnly,CancellationToken ct)
    {
        using(client) {
            try {
                if (client.Client.RemoteEndPoint is not IPEndPoint peer || !Allowed(peer.Address)) return;
                var s = client.GetStream(); var first = new byte[1];
                using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); headerTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                if (await s.ReadAsync(first,headerTimeout.Token) != 1) return;
                if (!pacOnly && first[0] == 5) await SocksAsync(client,s,peer,ct);
                else await HttpAsync(client,s,first[0],pacOnly,headerTimeout.Token,ct);
            } catch(OperationCanceledException) { }
            catch(Exception ex) { log?.Invoke("一次转发未完成："+ex.GetType().Name); }
        }
    }
    async Task HttpAsync(TcpClient client,NetworkStream s,byte first,bool pacOnly,CancellationToken readToken,CancellationToken ct)
    {
        using var buffer = new MemoryStream(); buffer.WriteByte(first); var one=new byte[1];
        while(buffer.Length<65536) {
            if(await s.ReadAsync(one,readToken)==0) return; buffer.WriteByte(one[0]);
            var b=buffer.GetBuffer(); var n=(int)buffer.Length;
            if(n>=4 && b[n-4]==13 && b[n-3]==10 && b[n-2]==13 && b[n-1]==10) break;
        }
        if(buffer.Length>=65536) return;
        var lines=Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n"); var parts=lines[0].Split(' ');
        if(parts.Length!=3) return;
        if(pacOnly) {
            var valid=parts[0] is "GET" or "HEAD" && parts[1].Split('?')[0] is "/wpad.dat" or "/proxy.pac";
            var body=Encoding.UTF8.GetBytes(valid?Pac:"Not found");
            await s.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid?"200 OK":"404 Not Found")}\r\nContent-Type: {(valid?"application/x-ns-proxy-autoconfig":"text/plain")}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n"),ct);
            if(parts[0]!="HEAD") await s.WriteAsync(body,ct); return;
        }
        var connect=parts[0]=="CONNECT";
        var uri=new Uri(connect?"https://"+parts[1]:parts[1]);
        if(uri.Scheme is not ("http" or "https")) return;
        using var outbound=new TcpClient(); await outbound.ConnectAsync(uri.Host,uri.Port,ct);
        if(connect) await s.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(),ct);
        else {
            var h=new StringBuilder($"{parts[0]} {uri.PathAndQuery} HTTP/1.1\r\n");
            foreach(var line in lines.Skip(1).Where(x=>x.Length>0)) {
                var name=line.Split(':',2)[0];
                if(name.Equals("Connection",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Proxy-",StringComparison.OrdinalIgnoreCase)) continue;
                h.Append(line).Append("\r\n");
            }
            h.Append("Connection: close\r\n\r\n"); await outbound.GetStream().WriteAsync(Encoding.ASCII.GetBytes(h.ToString()),ct);
        }
        await Tunnel(s,outbound.GetStream(),ct);
    }
    static async Task<byte[]> Read(NetworkStream s,int count,CancellationToken ct) { var b=new byte[count]; await s.ReadExactlyAsync(b,ct); return b; }
    static async Task<string> ReadHost(NetworkStream s,int type,CancellationToken ct) => type switch {
        1 => new IPAddress(await Read(s,4,ct)).ToString(),
        4 => new IPAddress(await Read(s,16,ct)).ToString(),
        3 => Encoding.ASCII.GetString(await Read(s,(await Read(s,1,ct))[0],ct)),
        _ => throw new IOException("SOCKS address type")
    };
    async Task SocksAsync(TcpClient client,NetworkStream s,IPEndPoint peer,CancellationToken ct)
    {
        var count=(await Read(s,1,ct))[0]; var methods=await Read(s,count,ct);
        await s.WriteAsync(new byte[]{5,methods.Contains((byte)0)?(byte)0:(byte)255},ct); if(!methods.Contains((byte)0))return;
        var h=await Read(s,4,ct); if(h[0]!=5)return;
        var host=await ReadHost(s,h[3],ct); var portBytes=await Read(s,2,ct); int port=portBytes[0]*256+portBytes[1];
        if(h[1]==1) {
            using var outbound=new TcpClient();
            try { await outbound.ConnectAsync(host,port,ct); } catch { await s.WriteAsync(new byte[]{5,5,0,1,0,0,0,0,0,0},ct); return; }
            await s.WriteAsync(new byte[]{5,0,0,1,0,0,0,0,0,0},ct); await Tunnel(s,outbound.GetStream(),ct);
        } else if(h[1]==3) await UdpAssociate(s,peer,ct);
        else await s.WriteAsync(new byte[]{5,7,0,1,0,0,0,0,0,0},ct);
    }
    static async Task Tunnel(Stream a,Stream b,CancellationToken ct)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ab=a.CopyToAsync(b,stop.Token); var ba=b.CopyToAsync(a,stop.Token);
        await Task.WhenAny(ab,ba); stop.Cancel();
        try { await Task.WhenAll(ab,ba); } catch(OperationCanceledException) { }
    }
    async Task UdpAssociate(NetworkStream control,IPEndPoint peer,CancellationToken outer)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(outer); var ct=stop.Token;
        using var local=new UdpClient(new IPEndPoint(IPAddress.Parse(address),0));
        using var remote=new UdpClient(new IPEndPoint(IPAddress.Any,0));
        var bound=(IPEndPoint)local.Client.LocalEndPoint!;
        await control.WriteAsync(new byte[]{5,0,0,1}.Concat(bound.Address.GetAddressBytes()).Concat(new byte[]{(byte)(bound.Port>>8),(byte)bound.Port}).ToArray(),ct);
        IPEndPoint? device=null; var destinations=new ConcurrentDictionary<string,bool>();
        async Task Outgoing() {
            while(!ct.IsCancellationRequested) {
                var packet=await local.ReceiveAsync(ct); if(!packet.RemoteEndPoint.Address.Equals(peer.Address))continue;
                var b=packet.Buffer; if(b.Length<10||b[0]!=0||b[1]!=0||b[2]!=0)continue;
                int p=4; string host;
                if(b[3]==1) {host=new IPAddress(b.AsSpan(p,4)).ToString();p+=4;}
                else if(b[3]==3) {int n=b[p++];if(p+n+2>b.Length)continue;host=Encoding.ASCII.GetString(b,p,n);p+=n;}
                else continue;
                if(p+2>b.Length)continue;int port=b[p]*256+b[p+1];p+=2;
                var ip=(await Dns.GetHostAddressesAsync(host,ct)).FirstOrDefault(x=>x.AddressFamily==AddressFamily.InterNetwork); if(ip is null)continue;
                var target=new IPEndPoint(ip,port);destinations[target.ToString()]=true;device=packet.RemoteEndPoint;
                await remote.SendAsync(b.AsMemory(p),target,ct);
            }
        }
        async Task Incoming() {
            while(!ct.IsCancellationRequested) {
                var packet=await remote.ReceiveAsync(ct); if(device is null||!destinations.ContainsKey(packet.RemoteEndPoint.ToString()))continue;
                var ep=packet.RemoteEndPoint;
                var header=new byte[]{0,0,0,1}.Concat(ep.Address.GetAddressBytes()).Concat(new byte[]{(byte)(ep.Port>>8),(byte)ep.Port});
                await local.SendAsync(header.Concat(packet.Buffer).ToArray(),device,ct);
            }
        }
        var outTask=Outgoing();var inTask=Incoming();var lease=control.ReadAsync(new byte[1],ct).AsTask();
        await Task.WhenAny(outTask,inTask,lease);stop.Cancel();
        try {await Task.WhenAll(outTask,inTask,lease);}catch(OperationCanceledException){}
    }
    public Task StopAsync() { lifetime?.Cancel(); proxy?.Stop(); pac?.Stop(); lifetime?.Dispose(); lifetime=null; return Task.CompletedTask; }
}
