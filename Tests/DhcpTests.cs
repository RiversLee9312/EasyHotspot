using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using EasyHotspot.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class DhcpTests
{
    static readonly IPAddress Server=IPAddress.Parse("192.168.137.1"),Mask=IPAddress.Parse("255.255.255.0");
    public static byte[] Packet(byte message=5,bool request252=true)
    {
        var p=new byte[328];p[0]=0x45;p[8]=64;p[9]=17;Put16(p,2,p.Length);Put16(p,24,p.Length-20);
        bool request=message is 1 or 3 or 8;p[28]=request?(byte)1:(byte)2;p[29]=1;p[30]=6;
        Put16(p,20,request?68:67);Put16(p,22,request?67:68);
        (request?new byte[]{192,168,137,42}:Server.GetAddressBytes()).CopyTo(p,12);
        (request?Server.GetAddressBytes():new byte[]{192,168,137,42}).CopyTo(p,16);
        new byte[]{0x71,0x2c,0xe9,0x18}.CopyTo(p,32);new byte[]{2,0x22,0x33,0x44,0x55,0x66}.CopyTo(p,56);
        if(request)new byte[]{192,168,137,42}.CopyTo(p,40);else new byte[]{192,168,137,42}.CopyTo(p,44);
        new byte[]{99,130,83,99,53,1,message,54,4,192,168,137,1,1,4,255,255,255,0,3,4,192,168,137,1,51,4,0,0,14,16,55,2,1,request252?(byte)252:(byte)3,255}.CopyTo(p,264);
        DhcpPackets.Checksums(p);return p;
    }
    static void Put16(byte[] p,int n,int value)=>BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(n),checked((ushort)value));
    static List<(byte Code,byte[] Data)> Options(byte[] packet)
    {
        int n=(packet[0]&15)*4+248;var list=new List<(byte,byte[])>();
        while(n<packet.Length){var code=packet[n++];if(code==255)break;if(code==0)continue;int length=packet[n++];list.Add((code,packet[n..(n+length)]));n+=length;}return list;
    }
    [TestMethod,DataRow((byte)2),DataRow((byte)5)] public void LeaseReplyKeepsLeaseAndAddsPac(byte message)
    {
        var original=Packet(message);var reply=DhcpPackets.Advertise(original,Server)!;Assert.IsNotNull(reply);
        CollectionAssert.AreEqual(original[28..264],reply[28..264]);
        foreach(var option in Options(original))CollectionAssert.AreEqual(option.Data,Options(reply).Single(x=>x.Code==option.Code).Data);
        Assert.AreEqual("http://192.168.137.1:18080/wpad.dat\0",Encoding.ASCII.GetString(Options(reply).Single(x=>x.Code==252).Data));
    }
    [TestMethod] public void ExistingPacIsReplacedOnlyOnce()
    {
        var once=DhcpPackets.Advertise(Packet(),Server)!;var twice=DhcpPackets.Advertise(once,Server)!;Assert.HasCount(1,Options(twice).Where(x=>x.Code==252));CollectionAssert.AreEqual(once,twice);
    }
    [TestMethod] public void InformGetsAnAckWithoutAnAddressLease()
    {
        var input=Packet(8);var reply=DhcpPackets.InformReply(input,Server,Mask)!;Assert.IsNotNull(reply);
        CollectionAssert.AreEqual(input[32..36],reply[32..36]);CollectionAssert.AreEqual(input[56..72],reply[56..72]);CollectionAssert.AreEqual(new byte[12],reply[44..56]);
        CollectionAssert.AreEqual(new byte[]{192,168,137,42},reply[16..20]);Assert.AreEqual((byte)5,Options(reply).Single(x=>x.Code==53).Data[0]);Assert.IsFalse(Options(reply).Any(x=>x.Code==51));Assert.IsTrue(Options(reply).Any(x=>x.Code==252));
    }
    [TestMethod,DataRow((byte)1),DataRow((byte)3),DataRow((byte)6)] public void NeverTakesOverAddressAllocation(byte message)=>Assert.IsNull(DhcpPackets.InformReply(Packet(message),Server,Mask));
    [TestMethod] public void InformWithoutPacRequestPassesThrough()=>Assert.IsNull(DhcpPackets.InformReply(Packet(8,false),Server,Mask));
    [TestMethod] public void OtherSubnetsRelaysAndSpoofedClientsAreRejected()
    {
        var p=Packet(8);p[14]=1;p[42]=1;Assert.IsNull(DhcpPackets.InformReply(p,Server,Mask));
        p=Packet(8);p[52]=1;Assert.IsNull(DhcpPackets.InformReply(p,Server,Mask));
        p=Packet(8);p[15]=43;Assert.IsNull(DhcpPackets.InformReply(p,Server,Mask));
        p=Packet(8);p[19]=100;Assert.IsNull(DhcpPackets.InformReply(p,Server,Mask));
    }
    [TestMethod] public void WrongServerFragmentsAndMalformedOptionsAreUnchanged()
    {
        var p=Packet();p[15]=2;Assert.IsNull(DhcpPackets.Advertise(p,Server));
        p=Packet();p[6]=0x20;Assert.IsNull(DhcpPackets.Advertise(p,Server));
        p=Packet();p[265]=0;Assert.IsNull(DhcpPackets.Advertise(p,Server));
        p=Packet();p[268]=250;Assert.IsNull(DhcpPackets.Advertise(p,Server));
        p=Packet();p[271]=52;Assert.IsNull(DhcpPackets.Advertise(p,Server));
        Assert.IsNull(DhcpPackets.Advertise(Packet(6),Server));
    }
    [TestMethod] public void BothChecksumsAreValid()
    {
        foreach(var p in new[]{DhcpPackets.Advertise(Packet(),Server)!,DhcpPackets.InformReply(Packet(8),Server,Mask)!})
        {
            Assert.AreEqual(p.Length,(int)BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(2)));Assert.AreEqual(65535,Sum(p.AsSpan(0,20)));
            var pseudo=p[12..20].Concat(new byte[]{0,17,(byte)((p.Length-20)>>8),(byte)(p.Length-20)}).Concat(p[20..]).ToArray();Assert.AreEqual(65535,Sum(pseudo));
        }
    }
    [TestMethod] public void TruncatedAndRandomInputCannotBreakDhcpForwarding()
    {
        var p=Packet();for(int i=0;i<p.Length;i++){Assert.IsNull(DhcpPackets.Advertise(p.AsSpan(0,i),Server));Assert.IsNull(DhcpPackets.InformReply(p.AsSpan(0,i),Server,Mask));}
        var random=new Random(420);for(int i=0;i<2000;i++){var b=new byte[random.Next(0,1800)];random.NextBytes(b);DhcpPackets.Advertise(b,Server);DhcpPackets.InformReply(b,Server,Mask);}
    }
    [TestMethod] public void OldSettingsDoNotEnableDiscovery()=>Assert.IsFalse(JsonSerializer.Deserialize<AppSettings>("{\"StartupMode\":\"proxy\"}")!.DhcpDiscovery);
    [TestMethod] public void DriverFilterCannotCaptureCampusDhcp()
    {
        var filter=DhcpDiscovery.Filter(42,"192.168.137.1");StringAssert.StartsWith(filter,"ifIdx == 42 and ip and udp and !loopback and (");StringAssert.Contains(filter,"ip.SrcAddr == 192.168.137.1");
    }
    static int Sum(ReadOnlySpan<byte> data){uint sum=0;int i=0;for(;i+1<data.Length;i+=2)sum+=BinaryPrimitives.ReadUInt16BigEndian(data[i..]);if(i<data.Length)sum+=(uint)data[i]<<8;while(sum>>16!=0)sum=(sum&65535)+(sum>>16);return (int)sum;}
}
