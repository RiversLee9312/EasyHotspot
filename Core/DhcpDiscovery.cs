using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace EasyHotspot.Core;

public record DhcpDiscoveryState(bool Enabled,bool Running,long Advertisements,string? Error);
public sealed class DhcpDiscovery : IAsyncDisposable
{
    readonly Action<string> log;IntPtr handle;Task? worker;int stopping;long advertisements;string? error;
    public bool Running=>worker is {IsCompleted:false} && Volatile.Read(ref stopping)==0;
    public DhcpDiscoveryState Read()=>new(ServiceSettings.Read().DhcpDiscovery,Running,Interlocked.Read(ref advertisements),error);
    public DhcpDiscovery(Action<string> log)=>this.log=log;
    public static string Filter(int index,string address)=>$"ifIdx == {index} and ip and udp and !loopback and ((outbound and ip.SrcAddr == {IPAddress.Parse(address)} and udp.SrcPort == 67 and udp.DstPort == 68) or (inbound and udp.SrcPort == 68 and udp.DstPort == 67))";
    public static void Probe()
    {
        if(!ServiceSetup.IsAdmin)throw new UnauthorizedAccessException();
        try {using var h=DhcpNative.Open("false");}
        finally {ServiceSetup.StopDhcpDriver();}
    }
    public async Task StartAsync(string address)
    {
        if(Running)return;
        if(!ServiceSetup.IsAdmin)throw new UnauthorizedAccessException();
        var ip=IPAddress.Parse(address);
        NetworkInterface? nic=null;
        for(int attempt=0;attempt<30;attempt++) {
            nic=NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n=>n.OperationalStatus==OperationalStatus.Up && n.Description.Contains("Wi-Fi Direct",StringComparison.OrdinalIgnoreCase) && n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(ip)));
            if(nic is not null)break;await Task.Delay(500);
        }
        if(nic is null)throw new InvalidOperationException("Error_DhcpAdapter");
        var properties=nic.GetIPProperties();var mask=properties.UnicastAddresses.Single(a=>a.Address.Equals(ip)).IPv4Mask;
        using var opened=DhcpNative.Open(Filter(properties.GetIPv4Properties().Index,address));handle=opened.Detach();
        Volatile.Write(ref stopping,0);error=null;Interlocked.Exchange(ref advertisements,0);
        worker=Task.Factory.StartNew(()=>Run(ip,mask),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    }
    void Run(IPAddress ip,IPAddress mask)
    {
        var packet=new byte[65535];var address=new byte[80];
        try {
            while(DhcpNative.WinDivertRecv(handle,packet,(uint)packet.Length,out uint length,address))
            {
                var original=packet.AsSpan(0,(int)length);byte[]? modified=null;bool inform=false;
                try {
                    if((address[10]&2)!=0)modified=DhcpPackets.Advertise(original,ip);
                    else {modified=DhcpPackets.InformReply(original,ip,mask);inform=modified is not null;}
                }catch(Exception ex){log("Error_DhcpPacket|"+ex.Message);}
                var outgoing=(byte[])address.Clone();if(inform)outgoing[10]|=2;
                if(modified is not null) {
                    // Checksums are calculated in managed code; tell the driver both are valid.
                    outgoing[10]|=0xa0;
                    if(DhcpNative.WinDivertSend(handle,modified,(uint)modified.Length,out _,outgoing)){Interlocked.Increment(ref advertisements);continue;}
                }
                if(!DhcpNative.WinDivertSend(handle,packet,length,out _,address))throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if(Volatile.Read(ref stopping)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        }catch(Exception ex){error="Error_DhcpRuntime|"+ex.Message;log(error);}
        finally {var h=Interlocked.Exchange(ref handle,IntPtr.Zero);if(h!=IntPtr.Zero)DhcpNative.WinDivertClose(h);}
    }
    public async Task StopAsync()
    {
        Volatile.Write(ref stopping,1);var h=handle;if(h!=IntPtr.Zero)DhcpNative.WinDivertShutdown(h,1);
        if(worker is not null)await worker;worker=null;ServiceSetup.StopDhcpDriver();
    }
    public async ValueTask DisposeAsync()=>await StopAsync();
}

internal static class DhcpNative
{
    internal const string DependencyError="Error_DhcpComponent";
    static DhcpNative()=>NativeLibrary.SetDllImportResolver(typeof(DhcpNative).Assembly,(name,assembly,path)=>name=="WinDivert.dll"?NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"Native","WinDivert.dll")):IntPtr.Zero);
    internal sealed class Handle(IntPtr value):IDisposable
    {
        public IntPtr Value=value;
        public IntPtr Detach(){var result=Value;Value=IntPtr.Zero;return result;}
        public void Dispose(){if(Value!=IntPtr.Zero)WinDivertClose(Value);Value=IntPtr.Zero;}
    }
    internal static Handle Open(string filter)
    {
        try {
            var h=WinDivertOpen(filter,0,100,0x10); // NO_INSTALL: own driver service is registered explicitly.
            if(h==new IntPtr(-1) && Marshal.GetLastWin32Error()==1060){ServiceSetup.StartDhcpDriver();h=WinDivertOpen(filter,0,100,0x10);}
            if(h==new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());return new(h);
        }catch(Exception ex) when(ex is DllNotFoundException or BadImageFormatException or Win32Exception or EntryPointNotFoundException){throw new IOException(DependencyError,ex);}
    }
    [DllImport("WinDivert.dll",CallingConvention=CallingConvention.Cdecl,SetLastError=true,CharSet=CharSet.Ansi)]static extern IntPtr WinDivertOpen(string filter,int layer,short priority,ulong flags);
    [DllImport("WinDivert.dll",CallingConvention=CallingConvention.Cdecl,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool WinDivertRecv(IntPtr handle,[Out]byte[] packet,uint length,out uint received,[Out]byte[] address);
    [DllImport("WinDivert.dll",CallingConvention=CallingConvention.Cdecl,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool WinDivertSend(IntPtr handle,byte[] packet,uint length,out uint sent,byte[] address);
    [DllImport("WinDivert.dll",CallingConvention=CallingConvention.Cdecl,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool WinDivertShutdown(IntPtr handle,int how);
    [DllImport("WinDivert.dll",CallingConvention=CallingConvention.Cdecl,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]internal static extern bool WinDivertClose(IntPtr handle);
}
