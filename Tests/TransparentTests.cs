using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using EasyHotspot.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass,DoNotParallelize]
public sealed class TransparentTests
{
    [TestMethod] public void WindowsProxySelectsProtocolAndHonorsBypass()
    {
        var settings=new ProxySettings(true,"http=127.0.0.1:7890;https=127.0.0.1:7891;socks=127.0.0.1:7892","*.internal;<local>");
        Assert.AreEqual(7890,settings.Manual(new("http://example.com"))!.Port);
        Assert.AreEqual(7891,settings.Manual(new("https://example.com"))!.Port);
        Assert.IsNull(settings.Manual(new("https://host.internal")));Assert.IsNull(settings.Manual(new("http://printer")));
        Assert.AreEqual("socks5",new ProxySettings(true,"socks=localhost:1080").Manual(new("https://example.com"))!.Scheme);
        Assert.IsNull(new ProxySettings(false,"localhost:7890").Manual(new("https://example.com")));
    }
    [TestMethod] public void SelfProxyIsRejectedBeforeOpeningAConnection()
    {
        var router=new OutboundRouter(new SystemProxyResolver(()=>new())){ListenerPort=23456};
        foreach(var uri in new[]{"http://127.0.0.1:18877","socks5://127.0.0.1:23456","http://192.168.137.1:18877"})
            Assert.ThrowsExactly<IOException>(()=>router.Validate(new(uri)));
        router.Validate(new("http://127.0.0.1:7890"));
    }
    [TestMethod] public void CaptureScopeUsesTheActualHotspotMask()
    {
        var filter=TransparentForwarder.Filter(IPAddress.Parse("192.168.77.1"),IPAddress.Parse("255.255.255.128"));
        StringAssert.Contains(filter,"ip.SrcAddr >= 192.168.77.0");StringAssert.Contains(filter,"ip.SrcAddr <= 192.168.77.127");
        Assert.IsFalse(filter.Contains("ifIdx"),"The forward layer reports the egress interface.");
    }
    [TestMethod] public void NdisDecoderRejectsOtherInterfacesAndTruncatedPackets()
    {
        var ip=UdpPacket(IPAddress.Parse("192.168.137.2"),IPAddress.Parse("1.1.1.1"),12000,443,"data"u8.ToArray());
        var capture=new byte[12+14+ip.Length];BinaryPrimitives.WriteInt32LittleEndian(capture,19);BinaryPrimitives.WriteInt32LittleEndian(capture.AsSpan(8,4),14+ip.Length);capture[24]=8;ip.CopyTo(capture,26);
        CollectionAssert.AreEqual(ip,HotspotPacketCapture.Decode(capture,19));Assert.IsNull(HotspotPacketCapture.Decode(capture,17));
        Assert.IsNull(HotspotPacketCapture.Decode(capture[..^1],19));
        Assert.IsTrue(TransparentForwarder.IsClientPacket(ip,IPAddress.Parse("192.168.137.1"),IPAddress.Parse("255.255.255.0")));
        Assert.IsFalse(TransparentForwarder.IsClientPacket(ip,IPAddress.Parse("10.0.0.1"),IPAddress.Parse("255.255.255.0")));
        ip[9]=1;Assert.IsFalse(TransparentForwarder.IsClientPacket(ip,IPAddress.Parse("192.168.137.1"),IPAddress.Parse("255.255.255.0")),"Remote ICMP must not produce a synthetic ping success.");
    }
    [TestMethod]
    [DataRow(0x08,0x41,24)]
    [DataRow(0x88,0x83,36)]
    public void NativeWifiFramesDecodeDecryptedSnapIncludingProtectedAndQosHeaders(int control,int flags,int header)
    {
        var ip=UdpPacket(IPAddress.Parse("192.168.137.2"),IPAddress.Parse("1.1.1.1"),12000,443,"data"u8.ToArray());
        var capture=new byte[12+header+8+ip.Length];BinaryPrimitives.WriteInt32LittleEndian(capture,19);BinaryPrimitives.WriteInt32LittleEndian(capture.AsSpan(8,4),capture.Length-12);
        capture[12]=(byte)control;capture[13]=(byte)flags;new byte[]{0xaa,0xaa,3,0,0,0,8,0}.CopyTo(capture,12+header);ip.CopyTo(capture,12+header+8);
        CollectionAssert.AreEqual(ip,HotspotPacketCapture.Decode(capture,19,true));Assert.IsNull(HotspotPacketCapture.Decode(capture,19));
        Assert.IsNull(HotspotPacketCapture.Decode(capture[..^1],19,true));capture[12+header]=0;Assert.IsNull(HotspotPacketCapture.Decode(capture,19,true));
    }
    [TestMethod] public async Task HttpSystemProxyCarriesRawTcpThroughConnect()
    {
        var proxy=new TcpListener(IPAddress.Loopback,0);proxy.Start();using var ct=new CancellationTokenSource(5000);
        try {
            var connect=ProxyTransport.ConnectAsync("198.18.1.25",443,new($"http://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}"),ct.Token);
            using var peer=await proxy.AcceptTcpClientAsync(ct.Token);var stream=peer.GetStream();
            StringAssert.StartsWith(await ProxyTransport.ReadHeader(stream,ct.Token),"CONNECT 198.18.1.25:443 HTTP/1.1");
            await stream.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray(),ct.Token);
            using var tunnel=await connect;await tunnel.WriteAsync("hello"u8.ToArray(),ct.Token);
            CollectionAssert.AreEqual("hello"u8.ToArray(),await ProxyTransport.Read(stream,5,ct.Token));
        }finally{proxy.Stop();}
    }
    [TestMethod] public async Task ProxyRejectionNeverFallsBackToDirect()
    {
        var proxy=new TcpListener(IPAddress.Loopback,0);proxy.Start();using var ct=new CancellationTokenSource(5000);
        try {
            var router=new OutboundRouter(new SystemProxyResolver(()=>new(true,$"127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}")));
            var connecting=router.ConnectAsync("127.0.0.1",1,ct.Token);using var peer=await proxy.AcceptTcpClientAsync(ct.Token);
            await ProxyTransport.ReadHeader(peer.GetStream(),ct.Token);await peer.GetStream().WriteAsync("HTTP/1.1 403 Forbidden\r\n\r\n"u8.ToArray(),ct.Token);
            var ex=await Assert.ThrowsExactlyAsync<IOException>(()=>connecting);StringAssert.StartsWith(ex.Message,"Error_UpstreamConnect|");
        }finally{proxy.Stop();}
    }
    [TestMethod] public async Task SocksUpstreamCarriesUdpAndPreservesTheDestination()
    {
        var proxy=new TcpListener(IPAddress.Loopback,0);proxy.Start();using var ct=new CancellationTokenSource(5000);
        using var relay=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        try {
            var connecting=UdpRoute.Socks(new($"socks5://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}"),"8.8.8.8",443,ct.Token);
            using var peer=await proxy.AcceptTcpClientAsync(ct.Token);var stream=peer.GetStream();await ProxyTransport.Read(stream,3,ct.Token);await stream.WriteAsync(new byte[]{5,0},ct.Token);
            CollectionAssert.AreEqual(new byte[]{5,3,0,1,0,0,0,0,0,0},await ProxyTransport.Read(stream,10,ct.Token));
            int port=((IPEndPoint)relay.Client.LocalEndPoint!).Port;await stream.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,(byte)(port>>8),(byte)port},ct.Token);
            using var route=await connecting;await route.SendAsync("udp"u8.ToArray(),ct.Token);var packet=await relay.ReceiveAsync(ct.Token);
            CollectionAssert.AreEqual(new byte[]{0,0,0,1,8,8,8,8,1,187}.Concat("udp"u8.ToArray()).ToArray(),packet.Buffer);
            await relay.SendAsync(packet.Buffer,packet.RemoteEndPoint,ct.Token);CollectionAssert.AreEqual("udp"u8.ToArray(),await route.ReceiveAsync(ct.Token));
        }finally{proxy.Stop();}
    }
    [TestMethod] public async Task PrivateBridgeRejectsAnUnauthenticatedClient()
    {
        await using var bridge=new AutomaticProxyServer(new(new SystemProxyResolver(()=>new())),_=>{});bridge.Start();using var client=new TcpClient();using var ct=new CancellationTokenSource(5000);
        await client.ConnectAsync(IPAddress.Loopback,bridge.Port,ct.Token);await client.GetStream().WriteAsync(new byte[]{5,1,0},ct.Token);
        CollectionAssert.AreEqual(new byte[]{5,255},await ProxyTransport.Read(client.GetStream(),2,ct.Token));
    }
    [TestMethod] public async Task PacketStackForwardsARealUdpPacketWithoutATunAdapter()
    {
        using var ct=new CancellationTokenSource(15000);var logs=new List<string>();
        await using var bridge=new AutomaticProxyServer(new(new SystemProxyResolver(()=>new()),()=>false),logs.Add);bridge.Start();
        using var echo=new UdpClient(new IPEndPoint(IPAddress.Any,0));int port=((IPEndPoint)echo.Client.LocalEndPoint!).Port;
        var target=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).SelectMany(n=>n.GetIPProperties().UnicastAddresses).First(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Address;
        using var stack=new Process{StartInfo=new(Path.Combine(AppContext.BaseDirectory,"Native","EasyHotspot.PacketStack.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        stack.Start();try {
            await stack.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Address=$"127.0.0.1:{bridge.Port}",bridge.Username,bridge.Password}));await stack.StandardInput.FlushAsync(ct.Token);
            Assert.AreEqual("READY",await stack.StandardError.ReadLineAsync(ct.Token));
            var packet=UdpPacket(IPAddress.Parse("192.168.137.2"),target,12345,port,"packet"u8.ToArray());
            await TransparentForwarder.WriteFrame(stack.StandardInput.BaseStream,packet,ct.Token);
            var received=await echo.ReceiveAsync(ct.Token);CollectionAssert.AreEqual("packet"u8.ToArray(),received.Buffer);
            await echo.SendAsync("reply"u8.ToArray(),received.RemoteEndPoint,ct.Token);
            var reply=await TransparentForwarder.ReadFrame(stack.StandardOutput.BaseStream,ct.Token);
            CollectionAssert.AreEqual(new byte[]{192,168,137,2},reply[16..20]);CollectionAssert.AreEqual("reply"u8.ToArray(),reply[((reply[0]&15)*4+8)..]);
        }finally{stack.StandardInput.Close();if(!stack.HasExited){try{await stack.WaitForExitAsync(ct.Token);}catch{stack.Kill(true);}}}
    }
    [TestMethod] public async Task PacketStackForwardsTcpPayloadAfterTheIpHandshake()
    {
        using var ct=new CancellationTokenSource(15000);
        await using var bridge=new AutomaticProxyServer(new(new SystemProxyResolver(()=>new())),_=>{});bridge.Start();
        var echo=new TcpListener(IPAddress.Any,0);echo.Start();int port=((IPEndPoint)echo.LocalEndpoint).Port;
        var target=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).SelectMany(n=>n.GetIPProperties().UnicastAddresses).First(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Address;
        var source=IPAddress.Parse("192.168.137.2");
        using var process=new Process{StartInfo=new(Path.Combine(AppContext.BaseDirectory,"Native","EasyHotspot.PacketStack.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        process.Start();try {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Address=$"127.0.0.1:{bridge.Port}",bridge.Username,bridge.Password}));await process.StandardInput.FlushAsync(ct.Token);
            Assert.AreEqual("READY",await process.StandardError.ReadLineAsync(ct.Token));
            await TransparentForwarder.WriteFrame(process.StandardInput.BaseStream,TcpPacket(source,target,12346,port,100,0,2,[]),ct.Token);
            var synAck=await TransparentForwarder.ReadFrame(process.StandardOutput.BaseStream,ct.Token);int start=(synAck[0]&15)*4;
            Assert.AreEqual(18,synAck[start+13]&18);uint sequence=BinaryPrimitives.ReadUInt32BigEndian(synAck.AsSpan(start+4,4));
            await TransparentForwarder.WriteFrame(process.StandardInput.BaseStream,TcpPacket(source,target,12346,port,101,sequence+1,16,[]),ct.Token);
            using var peer=await echo.AcceptTcpClientAsync(ct.Token);
            await TransparentForwarder.WriteFrame(process.StandardInput.BaseStream,TcpPacket(source,target,12346,port,101,sequence+1,24,"hello"u8.ToArray()),ct.Token);
            CollectionAssert.AreEqual("hello"u8.ToArray(),await ProxyTransport.Read(peer.GetStream(),5,ct.Token));
            await peer.GetStream().WriteAsync("world"u8.ToArray(),ct.Token);
            while(true){var response=await TransparentForwarder.ReadFrame(process.StandardOutput.BaseStream,ct.Token);int ip=(response[0]&15)*4;int offset=ip+((response[ip+12]>>4)*4);if(offset==response.Length)continue;CollectionAssert.AreEqual("world"u8.ToArray(),response[offset..]);break;}
        }finally{echo.Stop();process.StandardInput.Close();if(!process.HasExited){try{await process.WaitForExitAsync(ct.Token);}catch{process.Kill(true);}}}
    }
    static byte[] TcpPacket(IPAddress source,IPAddress destination,int sourcePort,int port,uint sequence,uint acknowledgement,byte flags,byte[] body)
    {
        var packet=UdpPacket(source,destination,sourcePort,port,new byte[12+body.Length]);packet[9]=6;packet[24]=packet[25]=0;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(24,4),sequence);BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(28,4),acknowledgement);
        packet[32]=0x50;packet[33]=flags;packet[34]=0xff;packet[35]=0xff;body.CopyTo(packet,40);packet[10]=packet[11]=0;
        uint Sum(ReadOnlySpan<byte> bytes){uint sum=0;for(int n=0;n<bytes.Length;n+=2)sum+=(uint)(bytes[n]*256+(n+1<bytes.Length?bytes[n+1]:0));return sum;}
        ushort Finish(uint sum){while(sum>>16!=0)sum=(sum&65535)+(sum>>16);return(ushort)~sum;}
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10,2),Finish(Sum(packet.AsSpan(0,20))));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(36,2),Finish(Sum(packet.AsSpan(12,8))+6+(uint)(packet.Length-20)+Sum(packet.AsSpan(20))));return packet;
    }
    static byte[] UdpPacket(IPAddress source,IPAddress destination,int sourcePort,int port,byte[] body)
    {
        var p=new byte[28+body.Length];p[0]=0x45;p[2]=(byte)(p.Length>>8);p[3]=(byte)p.Length;p[8]=64;p[9]=17;source.GetAddressBytes().CopyTo(p,12);destination.GetAddressBytes().CopyTo(p,16);
        p[20]=(byte)(sourcePort>>8);p[21]=(byte)sourcePort;p[22]=(byte)(port>>8);p[23]=(byte)port;p[24]=(byte)((8+body.Length)>>8);p[25]=(byte)(8+body.Length);body.CopyTo(p,28);
        uint sum=0;for(int n=0;n<20;n+=2)sum+=(uint)(p[n]*256+p[n+1]);while(sum>>16!=0)sum=(sum&65535)+(sum>>16);var checksum=(ushort)~sum;p[10]=(byte)(checksum>>8);p[11]=(byte)checksum;return p;
    }
}
