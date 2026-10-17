using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace EasyHotspot.Core;
public record ControlRequest(string Command, string? Mode=null, string? Ssid=null, string? Password=null);
public record ControlReply(bool Success, BackendState? State=null, string? Error=null);
public static class ControlProtocol
{
    public const string PipeName="EasyHotspot.Control.v1";
    public static async Task<ControlReply?> Send(ControlRequest request,int timeout=1200)
    {
        using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        using var ct=new CancellationTokenSource(timeout);
        try {
            await pipe.ConnectAsync(ct.Token);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
            using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            var reply=await reader.ReadLineAsync(ct.Token); return reply is null?null:JsonSerializer.Deserialize<ControlReply>(reply);
        } catch(IOException){return null;} catch(TimeoutException){return null;} catch(OperationCanceledException){return null;}
    }
    public static async Task Serve(Backend backend,string ownerSid,CancellationToken ct)
    {
        var security=new PipeSecurity();
        security.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{ownerSid,"S-1-5-18","S-1-5-32-544"}.Distinct())
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid),PipeAccessRights.FullControl,AccessControlType.Allow));
        while(!ct.IsCancellationRequested) {
            var pipe=NamedPipeServerStreamAcl.Create(PipeName,PipeDirection.InOut,4,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
            try {await pipe.WaitForConnectionAsync(ct);_ = Handle(pipe,backend,ct);}catch {pipe.Dispose();if(ct.IsCancellationRequested)break;throw;}
        }
    }
    static async Task Handle(NamedPipeServerStream pipe,Backend backend,CancellationToken ct)
    {
        using(pipe) {
            using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
            ControlReply reply;
            try {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
                var text=await reader.ReadLineAsync(deadline.Token);
                if(text is null||text.Length>8192)return;
                var req=JsonSerializer.Deserialize<ControlRequest>(text) ?? throw new IOException("Error_InvalidRequest");
                switch(req.Command) {
                    case "status": break;
                    case "start": await backend.StartAsync(req.Mode ?? "offline");break;
                    case "stop": await backend.StopAsync();break;
                    case "configure": await backend.Hotspot.ConfigureAsync(req.Ssid ?? "",req.Password ?? "");break;
                    case "startup": ServiceSettings.Save(req.Mode??"none");break;
                    case "dhcp": if(req.Mode is not ("on" or "off"))throw new ArgumentException("Error_InvalidRequest");await backend.SetDhcpAsync(req.Mode=="on");break;
                    default: throw new InvalidOperationException("Error_UnknownCommand");
                }
                reply=new(true,backend.Read());
            }catch(Exception ex){reply=new(false,Error:ex.Message);}
            try{await writer.WriteLineAsync(JsonSerializer.Serialize(reply));}catch(IOException){}
        }
    }
}
