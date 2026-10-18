using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace EasyHotspot.Core;

// All host connections use the current OS routes. Never bind them to a physical
// adapter: doing so would silently bypass an existing Clash/sing-box TUN.
public static class ProxyTransport
{
    public static async Task<Stream> ConnectAsync(string host,int port,Uri? proxy,CancellationToken ct)
    {
        var socket=new TcpClient();
        try {
            await socket.ConnectAsync(proxy?.Host??host,proxy?.Port??port,ct);
            Stream stream=socket.GetStream();
            if(proxy is null)return new ConnectionStream(stream,socket);
            if(proxy.Scheme=="https") {
                var tls=new SslStream(stream,false);
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost=proxy.Host},ct);stream=tls;
            }
            if(proxy.Scheme is "socks" or "socks5") {
                await SocksHandshake(stream,proxy,ct);
                await stream.WriteAsync(new byte[]{5,1,0}.Concat(Address(host,port)).ToArray(),ct);
                await SocksReply(stream,ct);
            } else {
                var authority=$"{(host.Contains(':')?"["+host+"]":host)}:{port}";
                var auth=proxy.UserInfo.Length==0?"":"Proxy-Authorization: Basic "+Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(proxy.UserInfo)))+"\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n{auth}\r\n"),ct);
                var header=await ReadHeader(stream,ct);
                var parts=header.Split('\r','\n')[0].Split(' ');
                if(parts.Length<2 || parts[1]!="200")throw new IOException("Error_UpstreamConnect|"+parts.ElementAtOrDefault(1));
            }
            return new ConnectionStream(stream,socket);
        } catch {socket.Dispose();throw;}
    }
    public static async Task SocksHandshake(Stream stream,Uri proxy,CancellationToken ct)
    {
        bool auth=proxy.UserInfo.Length>0;
        await stream.WriteAsync(auth?new byte[]{5,2,0,2}:new byte[]{5,1,0},ct);
        var response=await Read(stream,2,ct);
        if(response[0]!=5 || response[1] is not (0 or 2))throw new IOException("Error_UpstreamAuthentication");
        if(response[1]==2) {
            if(!auth)throw new IOException("Error_UpstreamAuthentication");
            var pair=proxy.UserInfo.Split(':',2);var user=Encoding.UTF8.GetBytes(Uri.UnescapeDataString(pair[0]));var pass=Encoding.UTF8.GetBytes(Uri.UnescapeDataString(pair.ElementAtOrDefault(1)??""));
            if(user.Length>255 || pass.Length>255)throw new IOException("Error_UpstreamAuthentication");
            await stream.WriteAsync(new byte[]{1,(byte)user.Length}.Concat(user).Concat(new[]{(byte)pass.Length}).Concat(pass).ToArray(),ct);
            response=await Read(stream,2,ct);if(response[0]!=1 || response[1]!=0)throw new IOException("Error_UpstreamAuthentication");
        }
    }
    public static async Task<(string Host,int Port)> SocksReply(Stream stream,CancellationToken ct)
    {
        var reply=await Read(stream,4,ct);if(reply[0]!=5 || reply[1]!=0 || reply[2]!=0)throw new IOException("Error_UpstreamConnect|"+reply[1]);
        return await ReadAddress(stream,reply[3],ct);
    }
    public static async Task<(string Host,int Port)> ReadAddress(Stream stream,int type,CancellationToken ct)
    {
        string host=type switch {1=>new IPAddress(await Read(stream,4,ct)).ToString(),4=>new IPAddress(await Read(stream,16,ct)).ToString(),3=>Encoding.ASCII.GetString(await Read(stream,(await Read(stream,1,ct))[0],ct)),_=>throw new IOException("Invalid SOCKS address")};
        var p=await Read(stream,2,ct);return(host,p[0]*256+p[1]);
    }
    public static byte[] Address(string host,int port)
    {
        if(port is <0 or >65535)throw new ArgumentOutOfRangeException(nameof(port));
        byte[] address;
        if(IPAddress.TryParse(host,out var ip))address=new[]{ip.AddressFamily==AddressFamily.InterNetwork?(byte)1:(byte)4}.Concat(ip.GetAddressBytes()).ToArray();
        else {var name=Encoding.ASCII.GetBytes(host);if(name.Length is <1 or >255)throw new ArgumentException(nameof(host));address=new byte[]{3,(byte)name.Length}.Concat(name).ToArray();}
        return address.Concat(new[]{(byte)(port>>8),(byte)port}).ToArray();
    }
    public static async Task<byte[]> Read(Stream stream,int count,CancellationToken ct){var bytes=new byte[count];await stream.ReadExactlyAsync(bytes,ct);return bytes;}
    public static async Task<string> ReadHeader(Stream stream,CancellationToken ct)
    {
        using var memory=new MemoryStream();var one=new byte[1];int tail=0;
        while(memory.Length<32768){await stream.ReadExactlyAsync(one,ct);memory.WriteByte(one[0]);tail=(tail<<8)|one[0];if(tail==0x0d0a0d0a)return Encoding.ASCII.GetString(memory.ToArray());}
        throw new IOException("Proxy response header is too large");
    }
    public static async Task CopyDuplex(Stream a,Stream b,CancellationToken ct,Action? finishA=null)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        async Task Pump(Stream from,Stream to,Action? finish){await from.CopyToAsync(to,stop.Token);finish?.Invoke();}
        var ab=Pump(a,b,b is ConnectionStream connection?connection.ShutdownSend:null);var ba=Pump(b,a,finishA);
        // Keep the reverse direction alive after a TCP half-close.
        var first=await Task.WhenAny(ab,ba);
        try {await first;await Task.WhenAll(ab,ba).WaitAsync(TimeSpan.FromSeconds(30),ct);}
        finally {stop.Cancel();try{await Task.WhenAll(ab,ba);}catch(Exception) when(stop.IsCancellationRequested){}}
    }
    sealed class ConnectionStream(Stream inner,TcpClient client) : Stream
    {
        public void ShutdownSend(){try{client.Client.Shutdown(SocketShutdown.Send);}catch(SocketException){}}
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>false;public override bool CanWrite=>inner.CanWrite;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
        public override void Write(byte[] buffer,int offset,int count)=>inner.Write(buffer,offset,count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)=>inner.ReadAsync(buffer,ct);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default)=>inner.WriteAsync(buffer,ct);
        public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){if(disposing){inner.Dispose();client.Dispose();}base.Dispose(disposing);}
    }
}
