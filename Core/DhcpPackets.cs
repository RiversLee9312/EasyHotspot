using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace EasyHotspot.Core;

/// <summary>IPv4 DHCP packet processing. Never allocates addresses or changes leases.</summary>
public static class DhcpPackets
{
    static ushort U16(ReadOnlySpan<byte> p,int n)=>BinaryPrimitives.ReadUInt16BigEndian(p[n..]);
    static void Put16(Span<byte> p,int n,int value)=>BinaryPrimitives.WriteUInt16BigEndian(p[n..],checked((ushort)value));
    static bool Parse(ReadOnlySpan<byte> p,out int ip,out int boot,out int end,out byte message,out bool requested)
    {
        ip=boot=end=0;message=0;requested=false;
        if(p.Length<268 || p[0]>>4!=4 || p[9]!=17)return false;
        ip=(p[0]&15)*4;end=U16(p,2);
        if(ip<20 || end>p.Length || end<ip+248 || (U16(p,6)&0x3fff)!=0 || U16(p,ip+4)!=end-ip)return false;
        boot=ip+8;
        if(p[boot+1]!=1 || p[boot+2]!=6 || !p.Slice(boot+236,4).SequenceEqual(new byte[]{99,130,83,99}))return false;
        bool terminated=false;
        for(int n=boot+240;n<end;)
        {
            byte code=p[n++];if(code==255){terminated=true;break;}if(code==0)continue;
            if(n>=end)return false;int length=p[n++];if(n+length>end)return false;
            if(code==52)return false; // Option overload requires parsing file/sname; leave such packets intact.
            if(code==53){if(length!=1 || message!=0)return false;message=p[n];}
            if(code==55 && p.Slice(n,length).Contains((byte)252))requested=true;
            n+=length;
        }
        return terminated && message!=0;
    }
    static byte[] Url(IPAddress server)
    {
        if(server.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new ArgumentException(nameof(server));
        return Encoding.ASCII.GetBytes($"http://{server}:18080/wpad.dat\0");
    }
    /// <summary>Add option 252 to Windows' OFFER/ACK, retaining every other option and BOOTP field.</summary>
    public static byte[]? Advertise(ReadOnlySpan<byte> packet,IPAddress server)
    {
        if(!Parse(packet,out int ip,out int boot,out int end,out byte message,out _) || message is not (2 or 5) || packet[boot]!=2 || U16(packet,ip)!=67 || U16(packet,ip+2)!=68 || !packet.Slice(12,4).SequenceEqual(server.GetAddressBytes()))return null;
        using var result=new MemoryStream();result.Write(packet[..(boot+240)]);
        for(int n=boot+240;n<end;)
        {
            byte code=packet[n++];if(code==255)break;if(code==0)continue;
            int length=packet[n++];if(code!=252){result.WriteByte(code);result.WriteByte((byte)length);result.Write(packet.Slice(n,length));}n+=length;
        }
        var url=Url(server);result.WriteByte(252);result.WriteByte((byte)url.Length);result.Write(url);result.WriteByte(255);
        if(result.Length>1500)return null; // Do not create fragments or exceed the conservative hotspot MTU.
        while(result.Length<boot+300)result.WriteByte(0);
        var bytes=result.ToArray();Put16(bytes,2,bytes.Length);Put16(bytes,ip+4,bytes.Length-ip);Checksums(bytes);return bytes;
    }
    /// <summary>Answer only a local DHCPINFORM requesting 252. DISCOVER/REQUEST remain Windows' responsibility.</summary>
    public static byte[]? InformReply(ReadOnlySpan<byte> packet,IPAddress server,IPAddress mask)
    {
        if(!Parse(packet,out int ip,out int boot,out _,out byte message,out bool requested) || message!=8 || !requested || packet[boot]!=1 || packet[boot+3]!=0 || U16(packet,ip)!=68 || U16(packet,ip+2)!=67)return null;
        var s=server.GetAddressBytes();var m=mask.GetAddressBytes();var client=packet.Slice(boot+12,4);
        if(s.Length!=4 || m.Length!=4 || client.SequenceEqual(s) || !packet.Slice(12,4).SequenceEqual(client) || !packet.Slice(boot+24,4).SequenceEqual(new byte[4]))return null;
        uint host=BinaryPrimitives.ReadUInt32BigEndian(s),netmask=BinaryPrimitives.ReadUInt32BigEndian(m),peer=BinaryPrimitives.ReadUInt32BigEndian(client);
        if((peer&netmask)!=(host&netmask) || (peer&~netmask)==0 || (peer&~netmask)==~netmask)return null;
        var destination=packet.Slice(16,4);
        if(!destination.SequenceEqual(s) && !destination.SequenceEqual(new byte[]{255,255,255,255}) && BinaryPrimitives.ReadUInt32BigEndian(destination)!=(host|~netmask))return null;
        var url=Url(server);var reply=new byte[328];reply[0]=0x45;Put16(reply,2,reply.Length);reply[8]=64;reply[9]=17;s.CopyTo(reply,12);
        // DHCPINFORM already has ciaddr. Unicast to that address, avoiding broadcast routing onto another NIC.
        client.CopyTo(reply.AsSpan(16,4));
        Put16(reply,20,67);Put16(reply,22,68);Put16(reply,24,reply.Length-20);
        const int b=28;packet.Slice(boot,236).CopyTo(reply.AsSpan(b));reply[b]=2;
        reply.AsSpan(b+8,4).Clear();reply.AsSpan(b+16,12).Clear();reply.AsSpan(b+44,192).Clear();
        new byte[]{99,130,83,99,53,1,5,54,4}.CopyTo(reply,b+236);s.CopyTo(reply,b+245);
        reply[b+249]=252;reply[b+250]=(byte)url.Length;url.CopyTo(reply,b+251);reply[b+251+url.Length]=255;
        Checksums(reply);return reply;
    }
    public static void Checksums(Span<byte> packet)
    {
        int ip=(packet[0]&15)*4;packet.Slice(10,2).Clear();Put16(packet,10,Checksum(packet[..ip]));
        packet.Slice(ip+6,2).Clear();uint sum=17u+(uint)(packet.Length-ip);
        for(int n=12;n<20;n+=2)sum+=U16(packet,n);
        var value=Checksum(packet[ip..],sum);Put16(packet,ip+6,value==0?65535:value);
    }
    static ushort Checksum(ReadOnlySpan<byte> data,uint sum=0)
    {
        int n=0;for(;n+1<data.Length;n+=2)sum+=U16(data,n);if(n<data.Length)sum+=(uint)data[n]<<8;
        while(sum>>16!=0)sum=(sum&65535)+(sum>>16);return (ushort)~sum;
    }
}
