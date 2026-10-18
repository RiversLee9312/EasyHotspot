using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EasyHotspot.Core;

// Private, per-run authenticated bridge between gVisor and the host's proxies.
public sealed class AutomaticProxyServer(OutboundRouter router,Action<string> log) : IAsyncDisposable
{
    readonly ConcurrentDictionary<Task,byte> tasks=new();
    readonly TcpListener listener=new(IPAddress.Loopback,0);
    readonly string secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    CancellationTokenSource? lifetime;Task? accept;
    public int Port=>((IPEndPoint)listener.LocalEndpoint).Port;
    public string Username=>"EasyHotspot";
    public string Password=>secret;
    public void Cancel()=>lifetime?.Cancel();
    public void Start(){listener.Start();router.ListenerPort=Port;lifetime=new();accept=Accept(lifetime.Token);}
    void Track(Task task){tasks[task]=0;_ = task.ContinueWith(t=>tasks.TryRemove(t,out _),TaskScheduler.Default);}
    async Task Accept(CancellationToken ct)
    {
        try {while(!ct.IsCancellationRequested)Track(Serve(await listener.AcceptTcpClientAsync(ct),ct));}
        catch(Exception) when(ct.IsCancellationRequested){}
    }
    async Task Serve(TcpClient client,CancellationToken ct)
    {
        using(client)try {
            var stream=client.GetStream();using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));var token=deadline.Token;
            var hello=await ProxyTransport.Read(stream,2,token);if(hello[0]!=5)return;
            var methods=await ProxyTransport.Read(stream,hello[1],token);await stream.WriteAsync(new byte[]{5,methods.Contains((byte)2)?(byte)2:(byte)255},token);if(!methods.Contains((byte)2))return;
            var auth=await ProxyTransport.Read(stream,2,token);if(auth[0]!=1)return;
            var user=Encoding.UTF8.GetString(await ProxyTransport.Read(stream,auth[1],token));var length=(await ProxyTransport.Read(stream,1,token))[0];var pass=await ProxyTransport.Read(stream,length,token);
            bool valid=user==Username && CryptographicOperations.FixedTimeEquals(pass,Encoding.UTF8.GetBytes(secret));
            await stream.WriteAsync(new byte[]{1,valid?(byte)0:(byte)1},token);if(!valid)return;
            var command=await ProxyTransport.Read(stream,4,token);if(command[0]!=5 || command[2]!=0)return;
            var target=await ProxyTransport.ReadAddress(stream,command[3],token);
            if(command[1]==3){await Associate(stream,ct);return;}
            if(command[1]!=1){await stream.WriteAsync(new byte[]{5,7,0,1,0,0,0,0,0,0},ct);return;}
            Stream outbound;
            try {outbound=await router.ConnectAsync(target.Host,target.Port,token);}
            catch {await stream.WriteAsync(new byte[]{5,5,0,1,0,0,0,0,0,0},ct);throw;}
            using(outbound){await stream.WriteAsync(new byte[]{5,0,0,1,0,0,0,0,0,0},ct);await ProxyTransport.CopyDuplex(stream,outbound,ct,()=>client.Client.Shutdown(SocketShutdown.Send));}
        }catch(Exception ex) when(ex is OperationCanceledException or EndOfStreamException || ct.IsCancellationRequested || RelayServer.IsPeerDisconnect(ex)){}
        catch(Exception ex){log("Error_TransparentFlow|"+ex.Message.Replace('|',':'));}
    }
    async Task Associate(Stream control,CancellationToken outer)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(outer);var ct=stop.Token;
        using var local=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        var port=((IPEndPoint)local.Client.LocalEndPoint!).Port;
        await control.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,(byte)(port>>8),(byte)port},ct);
        var routes=new Dictionary<string,UdpRoute>();var receives=new List<Task>();IPEndPoint? device=null;
        async Task Reply(byte[] data,byte[] address){if(device is not null)await local.SendAsync(new byte[]{0,0,0}.Concat(address).Concat(data).ToArray(),device,ct);}
        async Task Receive(UdpRoute route,byte[] address) {
            try {while(!ct.IsCancellationRequested)await Reply(await route.ReceiveAsync(ct),address);}
            catch(Exception) when(ct.IsCancellationRequested){}
            catch(Exception ex){log("Error_TransparentFlow|"+ex.Message.Replace('|',':'));stop.Cancel();}
        }
        async Task Send() {
            while(!ct.IsCancellationRequested) {
                var packet=await local.ReceiveAsync(ct);if(!IPAddress.IsLoopback(packet.RemoteEndPoint.Address) || device is not null && !device.Equals(packet.RemoteEndPoint))continue;
                var b=packet.Buffer;if(b.Length<7 || b[0]!=0 || b[1]!=0 || b[2]!=0)continue;
                using var bytes=new MemoryStream(b,3,b.Length-3);int type=bytes.ReadByte();
                var target=await ProxyTransport.ReadAddress(bytes,type,ct);var address=ProxyTransport.Address(target.Host,target.Port);var payload=b[(3+(int)bytes.Position)..];device=packet.RemoteEndPoint;
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                if(target.Port==53){await Reply(await router.DnsAsync(payload,timeout.Token),address);continue;}
                var key=target.Host+":"+target.Port;
                if(!routes.TryGetValue(key,out var route)) {
                    if(routes.Count>=256)throw new IOException("UDP association destination limit reached");
                    route=await router.OpenUdpAsync(target.Host,target.Port,timeout.Token);routes.Add(key,route);receives.Add(Receive(route,address));
                }
                await route.SendAsync(payload,timeout.Token);
            }
        }
        var send=Send();var lease=control.ReadAsync(new byte[1],ct).AsTask();
        try {await await Task.WhenAny(send,lease);}
        finally {stop.Cancel();foreach(var route in routes.Values)route.Dispose();try{await Task.WhenAll(receives.Append(send).Append(lease));}catch(Exception) when(ct.IsCancellationRequested){}}
    }
    public async ValueTask DisposeAsync()
    {
        if(lifetime is null)return;lifetime.Cancel();listener.Stop();
        if(accept is not null)await accept;try{await Task.WhenAll(tasks.Keys);}catch(Exception) when(lifetime.IsCancellationRequested){}
        lifetime.Dispose();lifetime=null;
    }
}
