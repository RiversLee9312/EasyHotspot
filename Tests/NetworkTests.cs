using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EasyHotspot.Core;

[TestClass,DoNotParallelize]
public sealed class NetworkTests
{
    static readonly System.Collections.Concurrent.ConcurrentQueue<string> logs=new();
    static readonly RelayServer relay=new(logs.Enqueue);
    [ClassInitialize] public static async Task Setup(TestContext _)=>await relay.StartAsync("127.0.0.1");
    [ClassCleanup] public static async Task Cleanup()=>await relay.StopAsync();
    [TestMethod] public async Task PacIsServedWithCorrectMimeAndProxy()
    {
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false});
        using var r=await http.GetAsync("http://127.0.0.1:18080/wpad.dat");
        Assert.AreEqual(HttpStatusCode.OK,r.StatusCode);Assert.AreEqual("application/x-ns-proxy-autoconfig",r.Content.Headers.ContentType?.MediaType);
        StringAssert.Contains(await r.Content.ReadAsStringAsync(),"PROXY 127.0.0.1:18877");
    }
    [TestMethod] public async Task PacServerDoesNotExposeOtherFiles()
    {
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false});
        Assert.AreEqual(HttpStatusCode.NotFound,(await http.GetAsync("http://127.0.0.1:18080/private.txt")).StatusCode);
    }
    [TestMethod] public async Task HttpConnectTransportsBytesEndToEnd()
    {
        var target=new TcpListener(IPAddress.Loopback,0);target.Start();
        try {
            using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",18877);var stream=client.GetStream();
            int port=((IPEndPoint)target.LocalEndpoint).Port;
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{port} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
            using var peer=await target.AcceptTcpClientAsync();var header=await Header(stream);StringAssert.StartsWith(header,"HTTP/1.1 200");
            await stream.WriteAsync("ping"u8.ToArray());var read=new byte[4];await peer.GetStream().ReadExactlyAsync(read);CollectionAssert.AreEqual("ping"u8.ToArray(),read);
            await peer.GetStream().WriteAsync("pong"u8.ToArray());await stream.ReadExactlyAsync(read);CollectionAssert.AreEqual("pong"u8.ToArray(),read);
        }finally{target.Stop();}
    }
    [TestMethod] public async Task PlainHttpIsRewrittenAndProxyHeadersAreRemoved()
    {
        var target=new TcpListener(IPAddress.Loopback,0);target.Start();
        try {
            using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",18877);var stream=client.GetStream();int port=((IPEndPoint)target.LocalEndpoint).Port;
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET http://127.0.0.1:{port}/a?q=1 HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nProxy-Authorization: secret\r\n\r\n"));
            using var peer=await target.AcceptTcpClientAsync();var request=await Header(peer.GetStream());StringAssert.StartsWith(request,"GET /a?q=1 HTTP/1.1");Assert.IsFalse(request.Contains("secret"));
            await peer.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK"u8.ToArray());
            StringAssert.StartsWith(await Header(stream),"HTTP/1.1 200");
        }finally{target.Stop();}
    }
    [TestMethod] public async Task SocksConnectTransportsBytes()
    {
        var target=new TcpListener(IPAddress.Loopback,0);target.Start();
        try {
            using var client=await Socks();var s=client.GetStream();int port=((IPEndPoint)target.LocalEndpoint).Port;
            await s.WriteAsync(new byte[]{5,1,0,1,127,0,0,1,(byte)(port>>8),(byte)port});using var peer=await target.AcceptTcpClientAsync();
            var reply=new byte[10];await s.ReadExactlyAsync(reply);Assert.AreEqual((byte)0,reply[1]);
            await s.WriteAsync("test"u8.ToArray());var b=new byte[4];await peer.GetStream().ReadExactlyAsync(b);CollectionAssert.AreEqual("test"u8.ToArray(),b);
        }finally{target.Stop();}
    }
    [TestMethod] public async Task SocksUdpReceivesReplyFromOriginalDestination()
    {
        using var target=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));using var control=await Socks();var s=control.GetStream();
        await s.WriteAsync(new byte[]{5,3,0,1,0,0,0,0,0,0});var reply=new byte[10];await s.ReadExactlyAsync(reply);Assert.AreEqual((byte)0,reply[1]);
        using var client=new UdpClient();var endpoint=new IPEndPoint(new IPAddress(reply.AsSpan(4,4)),reply[8]*256+reply[9]);
        int port=((IPEndPoint)target.Client.LocalEndPoint!).Port;
        var packet=new byte[]{0,0,0,1,127,0,0,1,(byte)(port>>8),(byte)port}.Concat("udp"u8.ToArray()).ToArray();
        await client.SendAsync(packet,endpoint);using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received=await target.ReceiveAsync(ct.Token);CollectionAssert.AreEqual("udp"u8.ToArray(),received.Buffer);
        await target.SendAsync("ack"u8.ToArray(),received.RemoteEndPoint);var response=await client.ReceiveAsync(ct.Token);CollectionAssert.AreEqual("ack"u8.ToArray(),response.Buffer[10..]);
    }
    [TestMethod] public async Task UnknownSocksCommandIsRejected()
    {
        using var client=await Socks();var s=client.GetStream();await s.WriteAsync(new byte[]{5,9,0,1,127,0,0,1,0,80});var reply=new byte[10];await s.ReadExactlyAsync(reply);Assert.AreEqual((byte)7,reply[1]);
    }
    [TestMethod] public async Task UnsupportedAuthenticationIsRejected()
    {
        using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",18877);var s=client.GetStream();await s.WriteAsync(new byte[]{5,1,2});var reply=new byte[2];await s.ReadExactlyAsync(reply);Assert.AreEqual((byte)255,reply[1]);
    }
    [TestMethod] public async Task InvalidHotspotConfigurationDoesNotReachWindows()
    {
        var h=new HotspotController();await Assert.ThrowsExactlyAsync<ArgumentException>(()=>h.ConfigureAsync("", "12345678"));await Assert.ThrowsExactlyAsync<ArgumentException>(()=>h.ConfigureAsync("x", "short"));
        await using var backend=new Backend();await Assert.ThrowsExactlyAsync<ArgumentException>(()=>backend.StartAsync("invalid"));
    }
    [TestMethod] public void StartupDefaultsToNoAutomaticHotspot()=>Assert.AreEqual("none",new AppSettings().StartupMode);
    [TestMethod] public void ServiceInstallationRequiresElevation()
    {
        if(ServiceSetup.IsAdmin){Assert.Inconclusive("此用例需要普通用户令牌。");return;}
        Assert.ThrowsExactly<UnauthorizedAccessException>(()=>ServiceSetup.Install("unused",WindowsIdentity.GetCurrent().User!.Value));
    }
    [TestMethod] public async Task ShortSocksProbeDoesNotGenerateAFalseFailure()
    {
        logs.Clear();using(var client=new TcpClient()){await client.ConnectAsync("127.0.0.1",18877);await client.GetStream().WriteAsync(new byte[]{5,2,0});}
        await Task.Delay(150);Assert.HasCount(0,logs);
    }
    [TestMethod] public async Task FailedHttpUpstreamReturns502AndLogsSocketError()
    {
        logs.Clear();var target=new TcpListener(IPAddress.Loopback,0);target.Start();int port=((IPEndPoint)target.LocalEndpoint).Port;target.Stop();
        using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",18877);await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{port} HTTP/1.1\r\n\r\n"));
        StringAssert.StartsWith(await Header(client.GetStream()),"HTTP/1.1 502");await Task.Delay(100);
        Assert.IsTrue(logs.Any(x=>x.StartsWith("Error_ProxyRequest|")&&x.Contains("ConnectionRefused")),string.Join("\n",logs));
    }
    [TestMethod] public async Task FailedSocksUpstreamHasADiagnosticInsteadOfBeingSilent()
    {
        logs.Clear();var target=new TcpListener(IPAddress.Loopback,0);target.Start();int port=((IPEndPoint)target.LocalEndpoint).Port;target.Stop();
        using var client=await Socks();await client.GetStream().WriteAsync(new byte[]{5,1,0,1,127,0,0,1,(byte)(port>>8),(byte)port});
        var reply=new byte[10];await client.GetStream().ReadExactlyAsync(reply);Assert.AreEqual((byte)5,reply[1]);await Task.Delay(100);
        Assert.IsTrue(logs.Any(x=>x.Contains("ConnectionRefused")),string.Join("\n",logs));
    }
    [TestMethod] public async Task ClientResetClosesTunnelWithoutAFailureLog()
    {
        logs.Clear();var target=new TcpListener(IPAddress.Loopback,0);target.Start();
        try {
            using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",18877);int port=((IPEndPoint)target.LocalEndpoint).Port;
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{port} HTTP/1.1\r\n\r\n"));using var peer=await target.AcceptTcpClientAsync();await Header(client.GetStream());
            await client.GetStream().WriteAsync("data"u8.ToArray());var body=new byte[4];await peer.GetStream().ReadExactlyAsync(body);
            client.Client.LingerState=new LingerOption(true,0);client.Close();
            Assert.AreEqual(0,await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));await Task.Delay(100);Assert.HasCount(0,logs);
        }finally{target.Stop();}
    }
    [TestMethod] public async Task ConnectivityCheckRequiresAProxyHotspot()
    {
        await using var backend=new Backend();var error=await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>backend.CheckProxyAsync());Assert.AreEqual("Error_ProxyNotRunning",error.Message);
    }
    [TestMethod] public async Task ClashProfileUsesTheActualHotspotAddressAndProxyDns()
    {
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false});using var response=await http.GetAsync("http://127.0.0.1:18080/mobile-clash.yaml");
        Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);Assert.AreEqual("text/yaml",response.Content.Headers.ContentType?.MediaType);
        var profile=await response.Content.ReadAsStringAsync();StringAssert.Contains(profile,"server: 127.0.0.1");StringAssert.Contains(profile,"https://223.5.5.5/dns-query#EasyHotspot");StringAssert.Contains(profile,"MATCH,EasyHotspot");
        Assert.ThrowsExactly<FormatException>(()=>MobileProfiles.Clash("127.0.0.1\nextra: value"));
    }
    static async Task<TcpClient> Socks(){var c=new TcpClient();await c.ConnectAsync("127.0.0.1",18877);await c.GetStream().WriteAsync(new byte[]{5,1,0});var b=new byte[2];await c.GetStream().ReadExactlyAsync(b);Assert.AreEqual((byte)0,b[1]);return c;}
    static async Task<string> Header(NetworkStream s){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));var b=new List<byte>();var one=new byte[1];while(b.Count<65536){await s.ReadExactlyAsync(one,timeout.Token);b.Add(one[0]);if(b.Count>=4&&b.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))return Encoding.ASCII.GetString(b.ToArray());}throw new IOException();}
}
