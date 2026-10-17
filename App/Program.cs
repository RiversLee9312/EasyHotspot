using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyHotspot;
public sealed partial class HotspotApp:Application
{
    Window? window;
    public HotspotApp(){Localization.ApplyPreferredLanguage();UnhandledException+=(s,e)=>{try{File.AppendAllText(Path.Combine(Path.GetTempPath(),"EasyHotspot-error.txt"),e.Exception+"\n");}catch{}};InitializeComponent();}
    protected override void OnLaunched(LaunchActivatedEventArgs args){window=new MainWindow();window.Activate();}
}
