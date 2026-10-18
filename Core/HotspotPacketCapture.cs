using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using System.Buffers.Binary;
using System.Net.NetworkInformation;

namespace EasyHotspot.Core;

// Read packets at the hotspot NIC before ICS/NAT. Windows' built-in NDIS
// capture provider is restricted to this adapter. No capture files are written.
public sealed class HotspotPacketCapture : IAsyncDisposable
{
    const string SessionName="EasyHotspot.Forwarding";
    static readonly Guid Provider=new("2ed6006e-4729-4609-b423-3ee7bcd678ef");
    ETWTraceEventSource? source;TraceEventSession? session;Task? worker,flusher;CancellationTokenSource? stop;
    bool disposed;
    public static Task RecoverAsync(bool force=false)
    {
        if(!force && !TraceEventSession.GetActiveSessionNames().Contains(SessionName,StringComparer.Ordinal))return Task.CompletedTask;
        return WindowsNetworkCommand.Run($"$s=Get-NetEventSession -Name '{SessionName}' -ErrorAction SilentlyContinue;if($s){{Stop-NetEventSession -Name '{SessionName}' -ErrorAction SilentlyContinue;$s|Remove-NetEventSession -ErrorAction SilentlyContinue}}");
    }
    public async Task StartAsync(NetworkInterface adapter,Action<byte[]> packet,Action<Exception> failed)
    {
        string Q(string value)=>"'"+value.Replace("'","''")+"'";
        var mac=BitConverter.ToString(adapter.GetPhysicalAddress().GetAddressBytes());
        // An existing capture owned by another tool is left alone.
        var script="$ErrorActionPreference='Stop';$captureStage='create session';"+
            $"if(Get-NetEventSession -Name '{SessionName}' -ErrorAction SilentlyContinue){{Remove-NetEventSession -Name '{SessionName}'}};"+
            $"New-NetEventSession -Name '{SessionName}' -CaptureMode RealtimeLocal -MaxNumberOfBuffers 128 -TraceBufferSize 64|Out-Null;"+
            $"$captureStage='add provider';Add-NetEventPacketCaptureProvider -SessionName '{SessionName}' -CaptureType Physical -TruncationLength 65535 -EtherType 0x0800 -LinkLayerAddress {Q(mac)}|Out-Null;"+
            $"$captureStage='select hotspot';Add-NetEventNetworkAdapter -Name {Q(adapter.Name)}|Out-Null;$captureStage='start session';Start-NetEventSession -Name '{SessionName}'";
        try {
            await WindowsNetworkCommand.Run(script);
            source=new ETWTraceEventSource(SessionName,TraceEventSourceType.Session);
            int hotspotIndex=adapter.GetIPProperties().GetIPv4Properties().Index;
            source.AllEvents+=data=>{
                if(data.ProviderGuid!=Provider || (int)data.ID!=1001)return;
                try {var bytes=data.EventData();var ip=Decode(bytes,hotspotIndex,((ulong)data.Keywords&0x10000)!=0);if(ip is not null)packet(ip);}
                catch(Exception ex){failed(ex);}
            };
            session=new TraceEventSession(SessionName,TraceEventSessionOptions.Attach);
            stop=new();var ct=stop.Token;
            worker=Task.Factory.StartNew(()=>{try{source.Process();if(!ct.IsCancellationRequested)failed(new IOException("Hotspot capture ended"));}catch(Exception ex){if(!ct.IsCancellationRequested)failed(ex);}},CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            flusher=Task.Run(async()=>{try{while(!ct.IsCancellationRequested){session.Flush();await Task.Delay(20,ct);}}catch(Exception ex){if(!ct.IsCancellationRequested)failed(ex);}},ct);
        }catch{try{await DisposeAsync();}catch{}throw;}
    }
    public static byte[]? Decode(byte[] data,int interfaceIndex,bool nativeWifi=false)
    {
        if(data.Length<26)return null;
        int mini=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0,4)),lower=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4,4));
        if(mini!=interfaceIndex && lower!=interfaceIndex)return null;
        int size=BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8,4));if(size<14 || size>data.Length-12)return null;
        var frame=data.AsSpan(12,size);int offset;ushort type;
        if(nativeWifi) {
            // Wi-Fi Direct reports decrypted native 802.11 MSDUs, even when
            // the Protected bit is retained. Only accept a complete LLC/SNAP
            // IPv4 payload; raw encrypted frames cannot match this header.
            if(size<32 || (frame[0]&0x0c)!=8)return null;
            offset=24;if((frame[1]&3)==3)offset+=6;
            if((frame[0]&0x80)!=0){offset+=2;if((frame[1]&0x80)!=0)offset+=4;}
            if(size<offset+8 || !frame.Slice(offset,6).SequenceEqual(new byte[]{0xaa,0xaa,3,0,0,0}))return null;
            type=BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(offset+6,2));offset+=8;
        }else {
            offset=14;type=BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12,2));
            if(type==0x8100 && size>=18){type=BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(16,2));offset=18;}
        }
        if(type!=0x0800 || size-offset<20 || frame[offset]>>4!=4)return null;
        int length=BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(offset+2,2));
        if(length<20 || length>size-offset)return null;
        return frame.Slice(offset,length).ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;
        stop?.Cancel();if(flusher is not null)await flusher;
        try {
            source?.StopProcessing();try{session?.Flush();}catch(Exception){/* The session may already have ended. */}
            // StopProcessing is observed at an ETW buffer callback. Stop the
            // session before awaiting Process(), including on an idle hotspot.
            await RecoverAsync(true);
        }finally {
            source?.Dispose();
            try{if(worker is not null)await worker.WaitAsync(TimeSpan.FromSeconds(5));}
            finally {
                if(session is not null){session.StopOnDispose=false;session.Dispose();session=null;}
                source=null;stop?.Dispose();stop=null;
            }
        }
    }
}
