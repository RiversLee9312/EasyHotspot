using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

namespace EasyHotspot.Core;

public record TransparentState(bool Running,long Received,long Sent,string Upstream,string? Error);

public sealed class TransparentForwarder(Action<string> log) : IAsyncDisposable
{
    IntPtr forward,dns,send;
    Process? process;AutomaticProxyServer? bridge;OutboundRouter? router;
    HotspotPacketCapture? capture;
    HotspotIsolation? isolation;
    CancellationTokenSource? lifetime;
    Task[] workers=[];long received,sent;string? error;bool active;
    byte[] subnet=[],subnetMask=[];IPAddress? ownAddress;
    public bool Running=>Volatile.Read(ref active) && lifetime is {IsCancellationRequested:false};
    public Task CheckAsync()=>Running && router is not null?NetworkDiagnostics.CheckProxyAsync(router):throw new InvalidOperationException("Error_ProxyNotRunning");
    public TransparentState Read()=>new(Running,Interlocked.Read(ref received),Interlocked.Read(ref sent),router?.Description??"",error);
    public static string Filter(IPAddress address,IPAddress mask)
    {
        var ip=address.GetAddressBytes();var m=mask.GetAddressBytes();
        var first=new IPAddress(ip.Zip(m,(a,b)=>(byte)(a&b)).ToArray());var last=new IPAddress(ip.Zip(m,(a,b)=>(byte)(a|~b)).ToArray());
        return $"ip and ip.SrcAddr >= {first} and ip.SrcAddr <= {last} and ip.SrcAddr != {address} and ip.DstAddr != {first} and ip.DstAddr != {last} and ip.DstAddr < 224.0.0.0 and (tcp or udp or icmp)";
    }
    public async Task StartAsync(string address)
    {
        if(Running)return;if(lifetime is not null)await StopAsync();if(!ServiceSetup.IsAdmin)throw new UnauthorizedAccessException("Error_TransparentAdmin");
        var executable=Path.Combine(AppContext.BaseDirectory,"Native","EasyHotspot.PacketStack.exe");
        if(!File.Exists(executable))throw new IOException("Error_TransparentComponent");
        lifetime=new();error=null;received=sent=0;var ct=lifetime.Token;
        try {
            var ip=IPAddress.Parse(address);NetworkInterface? adapter=null;
            for(int n=0;n<30;n++) {
                adapter=NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n=>n.OperationalStatus==OperationalStatus.Up && n.Description.Contains("Wi-Fi Direct",StringComparison.OrdinalIgnoreCase) && n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(ip)));
                if(adapter is not null)break;await Task.Delay(500,ct);
            }
            if(adapter is null)throw new IOException("Error_DhcpAdapter");
            var properties=adapter.GetIPProperties();var mask=properties.UnicastAddresses.Single(a=>a.Address.Equals(ip)).IPv4Mask;int index=properties.GetIPv4Properties().Index;
            ownAddress=ip;subnetMask=mask.GetAddressBytes();subnet=ip.GetAddressBytes().Zip(subnetMask,(a,b)=>(byte)(a&b)).ToArray();
            router=new(new SystemProxyResolver(ServiceSetup.OwnerSid)){HotspotAddress=address};bridge=new(router,log);bridge.Start();
            process=new Process{StartInfo=new(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            if(!process.Start())throw new IOException("Error_TransparentComponent");
            // The ephemeral bridge credential travels over the inherited pipe, not the command line.
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Address=$"127.0.0.1:{bridge.Port}",bridge.Username,bridge.Password}));await process.StandardInput.FlushAsync();
            var ready=await process.StandardError.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(15),ct);
            if(ready!="READY")throw new IOException("Error_TransparentComponent|"+ready);
            using(var h=DhcpNative.Open("false",0,200,0x18))send=h.Detach();
            // NDIS receives the client's original packets before ICS/NAT, which
            // can absorb forwarded traffic before WinDivert's forward layer.
            using(var h=DhcpNative.Open(Filter(ip,mask),1,200,0x12))forward=h.Detach();
            using(var h=DhcpNative.Open($"ifIdx == {index} and inbound and ip and ip.SrcAddr != {ip} and ip.DstAddr == {ip} and (udp.DstPort == 53 or tcp.DstPort == 53)",0,200,0x12))dns=h.Detach();
            isolation=new();await isolation.AcquireAsync(adapter);
            var queue=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1024){SingleReader=true,FullMode=BoundedChannelFullMode.DropWrite});
            workers=[WritePackets(queue.Reader,ct),ReadPackets(ct),ReadErrors(ct)];
            capture=new();await capture.StartAsync(adapter,packet=>{
                if(!IsClientPacket(packet,ip,mask))return;
                Interlocked.Increment(ref received);WinDivertHelperCalcChecksums(packet,(uint)packet.Length,IntPtr.Zero,0);queue.Writer.TryWrite(packet);
            },Failed);
            Volatile.Write(ref active,true);
            log("Error_TransparentStarted");
        }catch{await StopAsync();throw;}
    }
    public static bool IsClientPacket(byte[] packet,IPAddress gateway,IPAddress mask)
    {
        if(packet.Length<20 || packet[0]>>4!=4)return false;
        int h=(packet[0]&15)*4;if(h<20 || h>packet.Length)return false;
        var own=gateway.GetAddressBytes();var m=mask.GetAddressBytes();bool destinationLocal=true,broadcast=true;
        for(int i=0;i<4;i++){if((packet[12+i]&m[i])!=(own[i]&m[i]))return false;if((packet[16+i]&m[i])!=(own[i]&m[i]))destinationLocal=false;if((packet[16+i]|m[i])!=255)broadcast=false;}
        if(packet.AsSpan(12,4).SequenceEqual(own) || packet[16]>=224 || packet[16]==127)return false;
        // SOCKS/HTTP egress carries TCP and UDP. Let Windows handle gateway
        // ICMP locally rather than synthesizing remote ping replies in gVisor.
        if(packet[9] is not (6 or 17))return false;
        if(destinationLocal && broadcast)return false;
        bool fragment=(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6,2))&0x1fff)!=0;
        if(!fragment && (packet[9] is 6 or 17)){if(packet.Length<h+4)return false;int port=packet[h+2]*256+packet[h+3];if(port is 67 or 68)return false;if(packet.AsSpan(16,4).SequenceEqual(own) && port!=53)return false;}
        return true;
    }
    void Failed(Exception ex)
    {
        if(lifetime?.IsCancellationRequested!=false)return;
        error="Error_TransparentRuntime|"+ex.Message.Replace('|',':');log(error);lifetime.Cancel();
        Volatile.Write(ref active,false);bridge?.Cancel();
        // Keep the capture handles until StopAsync, so a failed bridge cannot
        // turn this mode into ordinary direct Internet sharing.
    }
    async Task WritePackets(ChannelReader<byte[]> queue,CancellationToken ct)
    {
        try {await foreach(var packet in queue.ReadAllAsync(ct)){await WriteFrame(process!.StandardInput.BaseStream,packet,ct);}}
        catch(Exception) when(ct.IsCancellationRequested){}catch(Exception ex){Failed(ex);}
    }
    async Task ReadPackets(CancellationToken ct)
    {
        var address=new byte[80];address[10]=0xe2; // outbound, valid IPv4/TCP/UDP checksums
        try {
            while(!ct.IsCancellationRequested) {
                var packet=await ReadFrame(process!.StandardOutput.BaseStream,ct);
                // Reply destinations must stay within the connected hotspot subnet.
                if(packet[0]>>4!=4 || packet.Length<20 || !packet.AsSpan(16,4).ToArray().Zip(subnetMask,(a,b)=>(byte)(a&b)).SequenceEqual(subnet) || new IPAddress(packet.AsSpan(16,4)).Equals(ownAddress))throw new IOException("Invalid packet stack reply destination");
                address[10]=0xe2;
                if(!DhcpNative.WinDivertSend(send,packet,(uint)packet.Length,out _,address))throw new Win32Exception(Marshal.GetLastWin32Error());
                Interlocked.Increment(ref sent);
            }
        }catch(Exception) when(ct.IsCancellationRequested){}catch(Exception ex){Failed(ex);}
    }
    async Task ReadErrors(CancellationToken ct)
    {
        try {while(await process!.StandardError.ReadLineAsync(ct) is { } line)log("Error_TransparentFlow|"+line.Replace('|',':'));if(!ct.IsCancellationRequested)Failed(new IOException("Packet stack exited"));}
        catch(Exception) when(ct.IsCancellationRequested){}catch(Exception ex){Failed(ex);}
    }
    public static async Task WriteFrame(Stream stream,byte[] packet,CancellationToken ct)
    {
        if(packet.Length is <20 or >65535)throw new IOException("Invalid packet frame");var size=new byte[2];BinaryPrimitives.WriteUInt16BigEndian(size,(ushort)packet.Length);
        await stream.WriteAsync(size,ct);await stream.WriteAsync(packet,ct);await stream.FlushAsync(ct);
    }
    public static async Task<byte[]> ReadFrame(Stream stream,CancellationToken ct)
    {
        var size=new byte[2];await stream.ReadExactlyAsync(size,ct);int length=BinaryPrimitives.ReadUInt16BigEndian(size);
        if(length<20)throw new IOException("Invalid packet frame");var packet=new byte[length];await stream.ReadExactlyAsync(packet,ct);return packet;
    }
    public async Task StopAsync()
    {
        Volatile.Write(ref active,false);lifetime?.Cancel();foreach(var handle in new[]{forward,dns})if(handle!=IntPtr.Zero)DhcpNative.WinDivertShutdown(handle,1);
        List<Exception> errors=[];
        async Task Clean(Func<Task> action){try{await action();}catch(Exception ex){errors.Add(ex);}}
        if(capture is not null)await Clean(async()=>await capture.DisposeAsync());capture=null;
        await Clean(async()=>{if(process is {HasExited:false}){process.StandardInput.Close();try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){process.Kill(true);await process.WaitForExitAsync();}}});
        await Clean(async()=>{try{await Task.WhenAll(workers);}catch(Exception) when(lifetime?.IsCancellationRequested==true){}});
        foreach(var handle in new[]{forward,dns,send})if(handle!=IntPtr.Zero)DhcpNative.WinDivertClose(handle);forward=dns=send=IntPtr.Zero;workers=[];
        if(bridge is not null)await Clean(async()=>await bridge.DisposeAsync());bridge=null;process?.Dispose();process=null;lifetime?.Dispose();lifetime=null;
        if(isolation is not null){await Clean(HotspotIsolation.RecoverAsync);isolation=null;}
        if(errors.Count>0)throw new IOException("Error_TransparentRuntime|"+string.Join("; ",errors.Select(ex=>ex.Message.Replace('|',':'))),errors[0]);
    }
    public async ValueTask DisposeAsync()=>await StopAsync();
    [DllImport("WinDivert.dll",CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.Bool)]static extern bool WinDivertHelperCalcChecksums(byte[] packet,uint length,IntPtr address,ulong flags);
}
