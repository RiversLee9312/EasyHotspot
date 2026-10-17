using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using EasyHotspot.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace EasyHotspot;
public sealed class MainWindow:Window
{
    readonly Backend local=new();readonly Grid root=new();readonly NavigationView nav=new();readonly Grid host=new();
    readonly Dictionary<string,ScrollViewer> pages=new();
    readonly InfoBar notice=new(){IsOpen=false,IsClosable=true,Margin=new(0,0,0,16)};
    readonly ComboBox mode=new(){MinWidth=320,SelectedIndex=0};
    readonly TextBox ssid=new(){MaxLength=32};
    readonly PasswordBox password=new(){PasswordRevealMode=PasswordRevealMode.Peek,FlowDirection=FlowDirection.LeftToRight};
    readonly TextBlock status=new(){FontSize=24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    readonly TextBlock clients=new(){FontSize=24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    readonly TextBlock ip=new(){FontSize=20,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,FlowDirection=FlowDirection.LeftToRight};
    readonly TextBlock deviceList=new(){TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
    readonly TextBlock diagnostic=new(){FontSize=12,FontFamily=new("Consolas"),TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FlowDirection=FlowDirection.LeftToRight};
    readonly ToggleSwitch startup=new();
    readonly ComboBox bootMode=new(){MinWidth=260,SelectedIndex=0};
    readonly ComboBox language=new(){MinWidth=260};
    readonly Button start=new(),stop=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(5)};
    bool busy,refreshing,initial=true,updatingStartup;BackendState? latest;string page="hotspot";
    public NavigationView Navigation=>nav;
    public MainWindow()
    {
        Title=L("AppName");ExtendsContentIntoTitleBar=true;SystemBackdrop=new MicaBackdrop();AppWindow.Resize(new(1120,820));
        root.FlowDirection=FlowDirection.LeftToRight;
        nav.FlowDirection=Localization.IsRightToLeft(Localization.CurrentLanguage)?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        var icon=Path.Combine(AppContext.BaseDirectory,"Assets","app.ico");if(File.Exists(icon))AppWindow.SetIcon(icon);
        root.RowDefinitions.Add(new(){Height=new GridLength(48)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var title=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,Margin=new(16,0,140,0),VerticalAlignment=VerticalAlignment.Center};
        title.Children.Add(Picture("app",24));title.Children.Add(Text(L("AppName"),14));root.Children.Add(title);
        nav.IsSettingsVisible=false;nav.IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed;nav.PaneDisplayMode=NavigationViewPaneDisplayMode.Left;nav.OpenPaneLength=220;nav.IsPaneOpen=true;nav.IsTitleBarAutoPaddingEnabled=false;
        foreach(var item in new[]{("hotspot",L("NavHotspot")),("proxy",L("NavProxy")),("settings",L("NavSettings"))}) {
            var n=new NavigationViewItem{Content=item.Item2,Tag=item.Item1,Icon=new BitmapIcon{UriSource=new Uri(Path.Combine(AppContext.BaseDirectory,"Assets",item.Item1+".png")),ShowAsMonochrome=true}};
            AutomationProperties.SetAutomationId(n,"Nav"+item.Item1);nav.MenuItems.Add(n);
        }
        var shell=new Grid();shell.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});notice.Margin=new(32,16,32,0);shell.Children.Add(notice);Grid.SetRow(host,1);shell.Children.Add(host);
        nav.Content=shell;nav.SelectionChanged+=(_,e)=>{if(e.SelectedItemContainer?.Tag is string value)ShowPage(value);};Grid.SetRow(nav,1);root.Children.Add(nav);
        Content=root;SetTitleBar(title);AutomationProperties.SetAutomationId(root,"AppRoot");
        mode.Header=L("ModeLabel");ssid.Header=L("HotspotName");password.Header=L("HotspotPassword");
        startup.OffContent=L("ToggleOff");startup.OnContent=L("ToggleOn");
        start.Content=L("StartHotspot");stop.Content=L("StopHotspot");
        root.Loaded+=(_,_)=>{
            var scale=root.XamlRoot.RasterizationScale;
            var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Resize(new(Math.Min(area.Width,(int)(1120*scale)),Math.Min(area.Height,(int)(820*scale))));
        };
        mode.Items.Add(L("ModeOffline"));mode.Items.Add(L("ModeProxy"));mode.Items.Add(L("ModeSharing"));
        mode.SelectedIndex=0;
        mode.SelectionChanged+=(_,_)=>{if(!initial && mode.SelectedIndex==1)Message(L("ProxyModeNoticeTitle"),L("ProxyModeNotice"),InfoBarSeverity.Informational);};
        start.Style=(Style)Application.Current.Resources["AccentButtonStyle"];
        start.Click+=async(_,_)=>await Action(async()=>{var m=SelectedMode();if(m=="proxy" && !ServiceSetup.Installed)await ServiceSetup.ElevateAsync("--firewall");await Command(new("start",m));});
        stop.Click+=async(_,_)=>await Action(()=>Command(new("stop")));
        AutomationProperties.SetAutomationId(start,"StartHotspot");AutomationProperties.SetAutomationId(stop,"StopHotspot");AutomationProperties.SetAutomationId(mode,"HotspotMode");
        AutomationProperties.SetAutomationId(startup,"HighPriorityStartup");AutomationProperties.SetAutomationId(bootMode,"StartupAction");
        bootMode.Items.Add(L("StartupNone"));bootMode.Items.Add(L("StartupOffline"));bootMode.Items.Add(L("StartupProxy"));
        bootMode.SelectedIndex=ServiceSettings.Read().StartupMode switch{"offline"=>1,"proxy"=>2,_=>0};
        startup.IsOn=ServiceSetup.Installed;
        startup.Toggled+=async(_,_)=>{
            if(updatingStartup)return;await Action(async()=>{
                if(local.Relay.Running)throw new InvalidOperationException(L("StartupConflict"));
                var desired=startup.IsOn;await ServiceSetup.ElevateAsync(desired?"--install":"--uninstall");
                updatingStartup=true;startup.IsOn=ServiceSetup.Installed;updatingStartup=false;
                Message(L("StartupUpdatedTitle"),L(desired?"StartupEnabledMessage":"StartupDisabledMessage"),InfoBarSeverity.Success);
            });updatingStartup=true;startup.IsOn=ServiceSetup.Installed;updatingStartup=false;};
        bootMode.SelectionChanged+=async(_,_)=>{if(initial||updatingStartup||!ServiceSetup.Installed)return;await Action(async()=>{var r=await ControlProtocol.Send(new("startup",bootMode.SelectedIndex switch{1=>"offline",2=>"proxy",_=>"none"}),5000);if(r is not {Success:true})throw new InvalidOperationException(r?.Error??L("ServiceUnresponsive"));});};
        foreach(var tag in Localization.SupportedLanguages)
            language.Items.Add(new ComboBoxItem{Content=Localization.NativeName(tag),Tag=tag});
        var preferred=Localization.CurrentLanguage;
        language.SelectedIndex=Array.IndexOf(Localization.SupportedLanguages.ToArray(),preferred);
        AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=(_,_)=>{
            if(language.SelectedItem is not ComboBoxItem item||item.Tag is not string tag)return;
            try{Localization.SavePreferredLanguage(tag);Message(L("LanguageSavedTitle"),L("LanguageSavedMessage"),InfoBarSeverity.Informational);}
            catch(Exception ex){Message(L("OperationFailed"),Localization.Error(ex.Message),InfoBarSeverity.Error);}
        };
        nav.SelectedItem=nav.MenuItems[0];timer.Tick+=async(_,_)=>await Refresh();timer.Start();_ = Refresh();
        Closed+=(_,_)=>{timer.Stop();_ = local.DisposeAsync();};
        AppWindow.Closing+=async(_,e)=>{
            if(local.Relay.Running){e.Cancel=true;var d=new ContentDialog{XamlRoot=root.XamlRoot,FlowDirection=nav.FlowDirection,Title=L("ConfirmStopTitle"),Content=L("ConfirmStopContent"),PrimaryButtonText=L("ConfirmStopButton"),CloseButtonText=L("Cancel"),DefaultButton=ContentDialogButton.Close};if(await d.ShowAsync()==ContentDialogResult.Primary){await local.Relay.StopAsync();Close();}}
        };
    }
    string SelectedMode()=>mode.SelectedIndex switch{1=>"proxy",2=>"sharing",_=>"offline"};
    static string L(string key)=>Localization.Get(key);
    static BitmapImage Bitmap(string name)=>new(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets",name+".png")));
    static Image Picture(string name,double size)=>new(){Source=Bitmap(name),Width=size,Height=size};
    static Brush Brush(string key)=>(Brush)Application.Current.Resources[key];
    static TextBlock Text(string value,double size=14)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap};
    static TextBlock Secondary(string value)=>new(){Text=value,FontSize=14,TextWrapping=TextWrapping.Wrap,Foreground=Brush("TextFillColorSecondaryBrush")};
    static Border Card(UIElement child)=>new(){Child=child,Padding=new(20),CornerRadius=new(8),BorderThickness=new(1),BorderBrush=Brush("CardStrokeColorDefaultBrush"),Background=Brush("CardBackgroundFillColorDefaultBrush")};
    static StackPanel Stack(params UIElement[] items){var s=new StackPanel{Spacing=12};foreach(var i in items)s.Children.Add(i);return s;}
    static StackPanel Row(params UIElement[] items){var s=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};foreach(var i in items)s.Children.Add(i);return s;}
    Button Button(string label,Func<Task> action){var b=new Button{Content=label};b.Click+=async(_,_)=>await Action(action);return b;}
    public void ShowPage(string name)
    {
        page=name;host.Children.Clear();
        if(pages.TryGetValue(name,out var cached)){host.Children.Add(cached);return;}
        var content=new StackPanel{Spacing=16,MaxWidth=880,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new(32,24,32,32)};
        var header=name switch{"proxy"=>L("PageProxy"),"settings"=>L("PageSettings"),_=>L("PageHotspot")};
        content.Children.Add(new TextBlock{Text=header,FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Margin=new(0,0,0,4)});
        if(name=="hotspot")BuildHotspot(content);else if(name=="proxy")BuildProxy(content);else BuildSettings(content);
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};pages[name]=scroll;host.Children.Add(scroll);
    }
    void BuildHotspot(StackPanel content)
    {
        Detach(status);Detach(clients);Detach(ip);var stats=new Grid{ColumnSpacing=12};for(int n=0;n<3;n++)stats.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var panels=new[]{Card(Stack(Secondary(L("StatusLabel")),status)),Card(Stack(Secondary(L("ClientsLabel")),clients)),Card(Stack(Secondary(L("AddressLabel")),ip))};
        for(int n=0;n<3;n++){Grid.SetColumn(panels[n],n);stats.Children.Add(panels[n]);}content.Children.Add(stats);
        Detach(mode);Detach(start);Detach(stop);
        content.Children.Add(Card(Stack(mode,Row(start,stop,Button(L("RefreshStatus"),Refresh)))));
        Detach(ssid);Detach(password);
        var fields=new Grid{ColumnSpacing=16};fields.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});fields.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});fields.Children.Add(ssid);Grid.SetColumn(password,1);fields.Children.Add(password);
        content.Children.Add(Card(Stack(fields,Button(L("SaveHotspotInfo"),()=>Command(new("configure",Ssid:ssid.Text,Password:password.Password))))));
        Detach(deviceList);content.Children.Add(Card(Stack(Text(L("DevicesHeading"),16),deviceList)));
        Detach(diagnostic);content.Children.Add(new Expander{Header=L("DiagnosticsHeading"),HorizontalAlignment=HorizontalAlignment.Stretch,Content=diagnostic});
    }
    static void Detach(FrameworkElement e){if(e.Parent is Panel p)p.Children.Remove(e);else if(e.Parent is Border b)b.Child=null;else if(e.Parent is ContentControl c)c.Content=null;}
    void BuildProxy(StackPanel content)
    {
        var addr=latest?.Hotspot?.Address??"192.168.137.1";
        var endpoint=Text($"{addr}:18877",24);endpoint.FlowDirection=FlowDirection.LeftToRight;
        content.Children.Add(Card(Stack(Text(L("ProxyComputerHeading"),16),endpoint,Secondary(L("ProxyEndpointsHelp")),Row(Button(L("CopyProxyAddress"),()=>Copy($"{addr}:18877")),Button(L("ConfigureFirewall"),()=>ServiceSetup.ElevateAsync("--firewall"))))));
        var url=$"http://{addr}:18080/wpad.dat";
        var pacAddress=new TextBox{Text=url,IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight};
        content.Children.Add(Card(Stack(Text(L("PacHeading"),16),pacAddress,Secondary(L("PacUrlHelp")),Button(L("CopyPacAddress"),()=>Copy(url)))));
        content.Children.Add(new InfoBar{IsOpen=true,IsClosable=false,Severity=InfoBarSeverity.Informational,Title=L("DhcpDiscoveryTitle"),Message=L("DhcpDiscoveryNote")});
        content.Children.Add(Card(Stack(Text(L("MobileDevicesHeading"),16),Secondary(L("MobileDevicesHelp")),Button(L("ExportMobileProfile"),ExportMobile))));
    }
    void BuildSettings(StackPanel content)
    {
        Detach(startup);Detach(bootMode);
        var row=new Grid{ColumnSpacing=24};row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        row.Children.Add(Stack(Text(L("StartupHeading"),16),Secondary(L("StartupHelp"))));Grid.SetColumn(startup,1);startup.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(startup);
        content.Children.Add(Card(Stack(row)));
        content.Children.Add(Card(Stack(Text(L("StartupActionHeading"),16),Secondary(L("StartupActionHelp")),bootMode)));
        content.Children.Add(Card(Stack(Text(L("LanguageHeading"),16),language,Secondary(L("LanguageHelp")))));
    }
    async Task ExportMobile()
    {
        var picker=new FileSavePicker{SuggestedFileName="Mobile-Relay"};picker.FileTypeChoices.Add(L("JsonProfile"),new List<string>{".json"});InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(this));var file=await picker.PickSaveFileAsync();if(file is null)return;
        var text=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Assets","Mobile-Relay.json"));text=text.Replace("192.168.137.1",latest?.Hotspot?.Address??"192.168.137.1");await File.WriteAllTextAsync(file.Path,text);Message(L("ExportedTitle"),L("ExportedMessage"),InfoBarSeverity.Success);
    }
    Task Copy(string value){var data=new DataPackage();data.SetText(value);Clipboard.SetContent(data);Message(L("CopiedTitle"),L("CopiedMessage"),InfoBarSeverity.Success);return Task.CompletedTask;}
    void Message(string title,string value,InfoBarSeverity severity){notice.Title=title;notice.Message=value;notice.Severity=severity;notice.IsOpen=true;}
    async Task Action(Func<Task> action)
    {
        if(busy)return;busy=true;start.IsEnabled=stop.IsEnabled=false;
        try{await action();await Refresh();}catch(Exception ex){Message(L("OperationFailed"),Localization.Error(ex.Message),InfoBarSeverity.Error);}finally{busy=false;UpdateControls();}
    }
    async Task Command(ControlRequest request)
    {
        var reply=await ControlProtocol.Send(request,45000);
        if(reply is not null){if(!reply.Success)throw new InvalidOperationException(reply.Error);return;}
        if(ServiceSetup.Installed)throw new InvalidOperationException(L("ServiceNotReady"));
        switch(request.Command){case "start":await local.StartAsync(request.Mode??"offline");break;case "stop":await local.StopAsync();break;case "configure":await local.Hotspot.ConfigureAsync(request.Ssid??"",request.Password??"");break;}
    }
    public async Task Refresh()
    {
        if(refreshing)return;refreshing=true;
        try {
            var reply=await ControlProtocol.Send(new("status"),400);latest=reply?.State??await Task.Run(local.Read);
            var h=latest.Hotspot;status.Text=h?.State=="On"?L("StatusOn"):L("StatusOff");clients.Text=(h?.Clients??0).ToString(Localization.FormattingCulture);ip.Text=h?.Address??"—";
            deviceList.Text=h?.Devices.Length>0?string.Join("\n",h.Devices):L("NoDevicesConnected");
            if(initial&&h is not null){ssid.Text=h.Ssid;password.Password=h.Password;initial=false;}
            diagnostic.Text=string.Join("\n",new[]{Localization.Format("InternetStatusLine",h?.Connectivity??L("Unknown")),Localization.Format("CurrentSourceLine",h?.Source??"—"),reply is null?L("RunModeApp"):L("RunModeService")}.Concat(latest.Logs.Select(Localization.Error)).Concat(latest.Error is null?[]:new[]{Localization.Error(latest.Error)}));
            bootMode.IsEnabled=ServiceSetup.Installed;
            UpdateControls();
        } finally {refreshing=false;}
    }
    void UpdateControls(){var on=latest?.Hotspot?.State=="On";start.IsEnabled=!busy&&!on;stop.IsEnabled=!busy&&on;ssid.IsEnabled=password.IsEnabled=!on;}
}
