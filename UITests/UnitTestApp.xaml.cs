using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
namespace EasyHotspot.UITests;
public partial class UnitTestApp:Application
{
    public static MainWindow? Main;
    public UnitTestApp(){UnhandledException+=(_,e)=>File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-test-unhandled.txt"),e.Exception.ToString());InitializeComponent();}
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try{Main=new MainWindow();Main.Activate();UITestMethodAttribute.DispatcherQueue=Main.DispatcherQueue;}
        catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-test-startup.txt"),ex.ToString());Environment.ExitCode=1;Exit();return;}
        // Optional manual review uses this same test host, without another app instance or a system theme change.
        var preview=Environment.GetEnvironmentVariable("EASYHOTSPOT_UI_PREVIEW");
        if(preview is "dark" or "light"){
            Main.Title="EasyHotspot — "+preview+" UI preview";
            ((FrameworkElement)Main.Content).RequestedTheme=preview=="dark"?ElementTheme.Dark:ElementTheme.Light;return;
        }
        try{Environment.ExitCode=await MicrosoftTestingPlatformApplication.RunAsync(Environment.GetCommandLineArgs()[1..]);}
        catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-test-error.txt"),ex.ToString());Environment.ExitCode=1;}
        finally{Main.DispatcherQueue.TryEnqueue(()=>{Main.Close();Exit();});}
    }
}
