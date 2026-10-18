using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using EasyHotspot.Core;

namespace EasyHotspot;
public sealed partial class HotspotApp:Application
{
    MainWindow? window;
    SingleInstanceGuard? instance;
    public HotspotApp(){UnhandledException+=(s,e)=>{_ = Task.Run(async()=>{try{await File.AppendAllTextAsync(Path.Combine(Path.GetTempPath(),"EasyHotspot-error.txt"),e.Exception+"\n");}catch{}});};InitializeComponent();}
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        instance=new();
        if(!instance.IsPrimary){instance.ActivateExisting();instance.Dispose();instance=null;Exit();return;}
        var dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        instance.Activated+=()=>dispatcher.TryEnqueue(()=>window?.ShowFromTray());
        await Localization.InitializeAsync();
        window=new MainWindow();window.EnableTray();window.Closed+=(_,_)=>{instance?.Dispose();instance=null;};window.Activate();
    }
}
