using EasyHotspot.Core;
using System.ServiceProcess;

if(args.Length>0 && args[0]!="--service") {
    try {
        string Value(string key)=>args.SkipWhile(x=>x!=key).Skip(1).FirstOrDefault() ?? throw new ArgumentException(key);
        if(args[0]=="--install")ServiceSetup.Install(Value("--source"),Value("--owner"));
        else if(args[0]=="--uninstall")ServiceSetup.Uninstall();
        else if(args[0]=="--firewall")ServiceSetup.Firewall(Path.Combine(Value("--source"),"EasyHotspot.exe"));
        else if(args[0]=="--cleanup")ServiceSetup.Cleanup();
        else throw new ArgumentException("不支持的操作");
        return 0;
    }catch(Exception ex){Directory.CreateDirectory(ServiceSetup.DataDirectory);File.WriteAllText(Path.Combine(ServiceSetup.DataDirectory,"setup-error.txt"),ex.ToString());return 1;}
}
ServiceBase.Run(new HotspotService());return 0;

sealed class HotspotService:ServiceBase
{
    readonly Backend backend=new();CancellationTokenSource? stop;
    public HotspotService(){ServiceName=ServiceSetup.ServiceName;CanStop=true;AutoLog=true;}
    protected override void OnStart(string[] args){stop=new();_ = Run(stop.Token);_ = AutoStart(stop.Token);}
    async Task AutoStart(CancellationToken ct)
    {
        var mode=ServiceSettings.Read().StartupMode;if(mode=="none")return;
        for(int attempt=0;attempt<20&&!ct.IsCancellationRequested;attempt++) {
            try{await Task.Delay(TimeSpan.FromSeconds(attempt==0?5:15),ct);await backend.StartAsync(mode);return;}
            catch(OperationCanceledException){return;}catch(Exception ex){backend.Log("等待启动条件："+ex.Message);}
        }
    }
    async Task Run(CancellationToken ct)
    {
        try {await ControlProtocol.Serve(backend,ServiceSetup.OwnerSid,ct);}catch(OperationCanceledException){}catch(Exception ex){backend.Log("后台服务："+ex.Message);}
    }
    protected override void OnStop(){stop?.Cancel();try{backend.StopAsync().GetAwaiter().GetResult();}catch{}backend.DisposeAsync().AsTask().GetAwaiter().GetResult();stop?.Dispose();}
}
