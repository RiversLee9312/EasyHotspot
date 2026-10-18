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
    readonly ComboBox mode=new(){SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly TextBox ssid=new(){MaxLength=32};
    readonly PasswordBox password=new(){PasswordRevealMode=PasswordRevealMode.Peek,FlowDirection=FlowDirection.LeftToRight};
    readonly TextBlock status=new(){FontSize=24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    readonly TextBlock clients=new(){FontSize=24,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    readonly TextBlock ip=new(){FontSize=20,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,FlowDirection=FlowDirection.LeftToRight};
    readonly TextBlock deviceList=new(){TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
    readonly TextBlock diagnostic=new(){FontSize=12,FontFamily=new("Consolas"),TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true,FlowDirection=FlowDirection.LeftToRight};
    readonly ToggleSwitch startup=new();
    readonly ToggleSwitch dhcp=new();
    readonly TextBlock dhcpStatus=new(){TextWrapping=TextWrapping.Wrap};
    readonly TextBlock tunStatus=new(){TextWrapping=TextWrapping.Wrap};
    readonly TextBlock transparentStatus=new(){TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
    readonly ProgressRing progress=new(){Width=24,Height=24,IsActive=false};
    readonly TextBlock operationStatus=new(){VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
    readonly StackPanel operationPanel=new(){Orientation=Orientation.Horizontal,Spacing=12,Margin=new(32,12,32,8),Visibility=Visibility.Collapsed};
    readonly List<Button> actionButtons=[];
    readonly ComboBox bootMode=new(){SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly ComboBox language=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    readonly Button start=new(),stop=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(5)};
    readonly Windows.UI.ViewManagement.AccessibilitySettings accessibility=new();
    bool busy,refreshing,initial=true,updatingStartup,updatingDhcp,updatingMode,serviceInstalled;BackendState? latest;string page="hotspot";Task? refreshTask;
    TrayIcon? tray;bool exiting;
    public void EnableTray(){tray??=new TrayIcon(this,ShowFromTray,()=>_ = ExitFromTray());}
    public void ShowFromTray(){AppWindow.Show();if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State==Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)presenter.Restore();Activate();SetForegroundWindow(WindowNative.GetWindowHandle(this));}
    async Task ExitFromTray()
    {
        if(busy)return;
        ShowFromTray();bool ready=false;
        await Action(async()=>{
            await Refresh();
            if(latest?.Hotspot?.State=="On")await Command(new("stop"));
            if(tray is not null){tray.Dispose();await tray.Disposal;}
            ready=true;
        },L("TrayExit"));
        if(ready){exiting=true;Close();}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    public NavigationView Navigation=>nav;
    public MainWindow()
    {
        Title=L("AppName");ExtendsContentIntoTitleBar=true;SystemBackdrop=new MicaBackdrop();AppWindow.Resize(new(1120,820));
        root.FlowDirection=FlowDirection.LeftToRight;
        nav.FlowDirection=Localization.IsRightToLeft(Localization.CurrentLanguage)?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
        var icon=Path.Combine(AppContext.BaseDirectory,"Assets","app.ico");if(File.Exists(icon))AppWindow.SetIcon(icon);
        root.RowDefinitions.Add(new(){Height=new GridLength(48)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var title=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,Margin=new(16,0,140,0),VerticalAlignment=VerticalAlignment.Center};
        title.Children.Add(Picture("app",28));title.Children.Add(Text(L("AppName"),14));root.Children.Add(title);
        nav.IsSettingsVisible=false;nav.IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed;nav.PaneDisplayMode=NavigationViewPaneDisplayMode.Auto;nav.OpenPaneLength=220;nav.ExpandedModeThresholdWidth=1008;nav.CompactModeThresholdWidth=640;nav.IsTitleBarAutoPaddingEnabled=false;
        foreach(var item in new[]{("hotspot",L("NavHotspot")),("proxy",L("NavProxy")),("settings",L("NavSettings")),("about",L("PageAbout"))}) {
            var n=new NavigationViewItem{Content=item.Item2,Tag=item.Item1,Icon=item.Item1=="about"?new SymbolIcon(Symbol.Help):NavigationIcons.Create(item.Item1)};
            AutomationProperties.SetAutomationId(n,"Nav"+item.Item1);nav.MenuItems.Add(n);
        }
        var shell=new Grid();shell.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});notice.Margin=new(32,16,32,0);shell.Children.Add(notice);
        operationPanel.Children.Add(progress);operationPanel.Children.Add(operationStatus);Grid.SetRow(operationPanel,1);shell.Children.Add(operationPanel);Grid.SetRow(host,2);shell.Children.Add(host);
        AutomationProperties.SetAutomationId(progress,"BusyIndicator");AutomationProperties.SetAutomationId(operationStatus,"OperationStatus");
        nav.Content=shell;nav.SelectionChanged+=(_,e)=>{if(e.SelectedItemContainer?.Tag is string value)ShowPage(value);};Grid.SetRow(nav,1);root.Children.Add(nav);
        Content=root;SetTitleBar(title);AutomationProperties.SetAutomationId(root,"AppRoot");
        root.Loaded+=(_,_)=>UpdateTitleBar();root.ActualThemeChanged+=(_,_)=>UpdateTitleBar();
        mode.Header=L("ModeLabel");ssid.Header=L("HotspotName");password.Header=L("HotspotPassword");
        startup.OffContent=L("ToggleOff");startup.OnContent=L("ToggleOn");
        dhcp.OffContent=L("ToggleOff");dhcp.OnContent=L("ToggleOn");
        AutomationProperties.SetAutomationId(dhcp,"DhcpAutoDiscovery");AutomationProperties.SetAutomationId(dhcpStatus,"DhcpDiscoveryStatus");
        AutomationProperties.SetName(dhcp,L("DhcpDiscoveryTitle"));AutomationProperties.SetName(startup,L("StartupHeading"));
        dhcp.Toggled+=async(_,_)=>{
            if(updatingDhcp)return;
            await Action(async()=>{
                if(latest?.Hotspot?.State=="On")throw new InvalidOperationException("Error_DhcpStopFirst");
                var desired=dhcp.IsOn;var reply=await ControlProtocol.Send(new("dhcp",desired?"on":"off"),15000);
                if(reply is null)await ServiceSetup.ElevateAsync(desired?"--dhcp-on":"--dhcp-off");else if(!reply.Success)throw new InvalidOperationException(reply.Error);
            });
            updatingDhcp=true;dhcp.IsOn=(await Task.Run(ServiceSettings.Read)).DhcpDiscovery;updatingDhcp=false;
        };
        start.Content=L("StartHotspot");stop.Content=L("StopHotspot");
        root.Loaded+=(_,_)=>{
            var scale=root.XamlRoot.RasterizationScale;
            var area=Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id,Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter){presenter.PreferredMinimumWidth=(int)(360*scale);presenter.PreferredMinimumHeight=(int)(300*scale);}
            AppWindow.Resize(new(Math.Min(area.Width,(int)(1120*scale)),Math.Min(area.Height,(int)(820*scale))));
        };
        mode.Items.Add(L("ModeOffline"));mode.Items.Add(L("ModeProxy"));mode.Items.Add(L("ModeSharing"));mode.Items.Add(L("ModeTransparent"));
        mode.SelectedIndex=0;
        mode.SelectionChanged+=(_,_)=>{if(!initial && !updatingMode && mode.SelectedIndex==1)Message(L("ProxyModeNoticeTitle"),L("ProxyModeNotice"),InfoBarSeverity.Informational);};
        start.Style=(Style)Application.Current.Resources["AccentButtonStyle"];
        start.Click+=async(_,_)=>await Action(async()=>{var m=SelectedMode();if(m=="transparent" && (!serviceInstalled || latest?.Transparent is null))await ServiceSetup.ElevateAsync("--ensure-service");else if(m=="proxy" && !serviceInstalled)await ServiceSetup.ElevateAsync("--firewall");await Command(new("start",m));});
        stop.Click+=async(_,_)=>await Action(()=>Command(new("stop")));
        AutomationProperties.SetAutomationId(start,"StartHotspot");AutomationProperties.SetAutomationId(stop,"StopHotspot");AutomationProperties.SetAutomationId(mode,"HotspotMode");
        AutomationProperties.SetAutomationId(startup,"HighPriorityStartup");AutomationProperties.SetAutomationId(bootMode,"StartupAction");
        bootMode.Items.Add(L("StartupNone"));bootMode.Items.Add(L("StartupOffline"));bootMode.Items.Add(L("StartupProxy"));bootMode.Items.Add(L("ModeTransparent"));
        bootMode.SelectedIndex=0;
        startup.Toggled+=async(_,_)=>{
            if(updatingStartup)return;await Action(async()=>{
                if(local.Relay.Running)throw new InvalidOperationException(L("StartupConflict"));
                var desired=startup.IsOn;await ServiceSetup.ElevateAsync(desired?"--install":"--uninstall");
                updatingStartup=true;startup.IsOn=await Task.Run(()=>ServiceSetup.AutoStartEnabled);updatingStartup=false;
                Message(L("StartupUpdatedTitle"),L(desired?"StartupEnabledMessage":"StartupDisabledMessage"),InfoBarSeverity.Success);
            });updatingStartup=true;startup.IsOn=await Task.Run(()=>ServiceSetup.AutoStartEnabled);updatingStartup=false;};
        bootMode.SelectionChanged+=async(_,_)=>{if(initial||updatingStartup||!serviceInstalled)return;await Action(async()=>{var r=await ControlProtocol.Send(new("startup",bootMode.SelectedIndex switch{1=>"offline",2=>"proxy",3=>"transparent",_=>"none"}),5000);if(r is not {Success:true})throw new InvalidOperationException(r?.Error??L("ServiceUnresponsive"));});};
        foreach(var tag in Localization.SupportedLanguages)
            language.Items.Add(new ComboBoxItem{Content=Localization.NativeName(tag),Tag=tag});
        var preferred=Localization.CurrentLanguage;
        language.SelectedIndex=Array.IndexOf(Localization.SupportedLanguages.ToArray(),preferred);
        AutomationProperties.SetAutomationId(language,"AppLanguage");
        language.SelectionChanged+=async(_,_)=>{
            if(language.SelectedItem is not ComboBoxItem item||item.Tag is not string tag)return;
            await Action(async()=>{await Task.Run(()=>Localization.SavePreferredLanguage(tag));Message(L("LanguageSavedTitle"),L("LanguageSavedMessage"),InfoBarSeverity.Informational);});
        };
        nav.SelectedItem=nav.MenuItems[0];timer.Tick+=async(_,_)=>{UpdateTitleBar();await Refresh();};timer.Start();_ = Refresh();
        Closed+=(_,_)=>{timer.Stop();tray?.Dispose();_ = Task.Run(async()=>await local.DisposeAsync());};
        AppWindow.Closing+=async(_,e)=>{
            if(tray is not null && !tray.SessionEnding && !exiting){e.Cancel=true;await Action(async()=>{if(await tray.EnsureAsync())AppWindow.Hide();else {Message(L("AppName"),L("TrayRecovering"),InfoBarSeverity.Warning);var exitButton=new Button{Content=L("TrayExit")};exitButton.Click+=async(_,_)=>await ExitFromTray();notice.ActionButton=exitButton;}});return;}
            if(local.Relay.Running){e.Cancel=true;var d=new ContentDialog{XamlRoot=root.XamlRoot,FlowDirection=nav.FlowDirection,Title=L("ConfirmStopTitle"),Content=L("ConfirmStopContent"),PrimaryButtonText=L("ConfirmStopButton"),CloseButtonText=L("Cancel"),DefaultButton=ContentDialogButton.Close};if(await d.ShowAsync()==ContentDialogResult.Primary){await local.Relay.StopAsync();Close();}}
        };
    }
    string SelectedMode()=>mode.SelectedIndex switch{1=>"proxy",2=>"sharing",3=>"transparent",_=>"offline"};
    void UpdateTitleBar()
    {
        if(!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())return;
        var titleBar=AppWindow.TitleBar;
        if(accessibility.HighContrast){
            titleBar.ButtonForegroundColor=null;titleBar.ButtonInactiveForegroundColor=null;
            titleBar.ButtonBackgroundColor=null;titleBar.ButtonInactiveBackgroundColor=null;
            titleBar.ButtonHoverForegroundColor=null;titleBar.ButtonHoverBackgroundColor=null;
            titleBar.ButtonPressedForegroundColor=null;titleBar.ButtonPressedBackgroundColor=null;return;
        }
        bool dark=root.ActualTheme==ElementTheme.Dark;
        var ink=dark?Microsoft.UI.Colors.White:Microsoft.UI.Colors.Black;
        titleBar.ButtonForegroundColor=ink;titleBar.ButtonHoverForegroundColor=ink;titleBar.ButtonPressedForegroundColor=ink;
        titleBar.ButtonInactiveForegroundColor=Color.FromArgb(255,128,128,128);
        titleBar.ButtonBackgroundColor=Microsoft.UI.Colors.Transparent;titleBar.ButtonInactiveBackgroundColor=Microsoft.UI.Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor=dark?Color.FromArgb(25,255,255,255):Color.FromArgb(20,0,0,0);
        titleBar.ButtonPressedBackgroundColor=dark?Color.FromArgb(40,255,255,255):Color.FromArgb(30,0,0,0);
    }
    static string L(string key)=>Localization.Get(key);
    static BitmapImage Bitmap(string name)=>new(new Uri(Path.Combine(AppContext.BaseDirectory,"Assets",name+".png")));
    static Image Picture(string name,double size)=>new(){Source=Bitmap(name),Width=size,Height=size};
    static TextBlock Text(string value,double size=14)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap};
    static TextBlock Secondary(string value)=>new(){Text=value,FontSize=14,TextWrapping=TextWrapping.Wrap,Style=(Style)Application.Current.Resources["AppSecondaryTextStyle"]};
    static Border Card(UIElement child)=>new(){Child=child,Padding=new(20),CornerRadius=new(8),BorderThickness=new(1),Style=(Style)Application.Current.Resources["AppCardStyle"]};
    static StackPanel Stack(params UIElement[] items){var s=new StackPanel{Spacing=12};foreach(var i in items)s.Children.Add(i);return s;}
    static ActionPanel Row(params UIElement[] items){var s=new ActionPanel();foreach(var i in items)s.Children.Add(i);return s;}
    static Grid Columns(double minimumWidth,int maximumColumns,double gap,params FrameworkElement[] items)
    {
        var grid=new Grid{ColumnSpacing=gap,RowSpacing=gap};foreach(var item in items)grid.Children.Add(item);
        int previous=0;
        void Reflow(double width){int columns=Math.Clamp((int)((width+gap)/(minimumWidth+gap)),1,maximumColumns);if(columns==previous)return;previous=columns;grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();for(int n=0;n<columns;n++)grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});for(int n=0;n<(items.Length+columns-1)/columns;n++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});for(int n=0;n<items.Length;n++){Grid.SetColumn(items[n],n%columns);Grid.SetRow(items[n],n/columns);}}
        Reflow(0);grid.SizeChanged+=(_,e)=>Reflow(e.NewSize.Width);return grid;
    }
    static Grid SettingRow(FrameworkElement description,FrameworkElement toggle){var row=Columns(300,2,24,description,toggle);row.SizeChanged+=(_,e)=>toggle.HorizontalAlignment=e.NewSize.Width>=624?HorizontalAlignment.Right:HorizontalAlignment.Left;return row;}
    Button Button(string label,Func<Task> action){var b=new Button{Content=label,IsEnabled=!busy};actionButtons.Add(b);b.Click+=async(_,_)=>await Action(action,label);return b;}
    public void ShowPage(string name)
    {
        page=name;host.Children.Clear();
        if(pages.TryGetValue(name,out var cached)){host.Children.Add(cached);return;}
        var content=new StackPanel{Spacing=16,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(32,24,32,32)};
        AutomationProperties.SetAutomationId(content,"PageContent-"+name);
        var header=name switch{"proxy"=>L("PageProxy"),"settings"=>L("PageSettings"),"about"=>L("PageAbout"),_=>L("PageHotspot")};
        content.Children.Add(new TextBlock{Text=header,FontSize=28,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Margin=new(0,0,0,4)});
        if(name=="hotspot")BuildHotspot(content);else if(name=="proxy")BuildProxy(content);else if(name=="about")BuildAbout(content);else BuildSettings(content);
        var scroll=new ScrollViewer{Content=content,HorizontalContentAlignment=HorizontalAlignment.Left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        void ResizePage(){double padding=scroll.ActualWidth<640?16:32;content.Margin=new(padding,24,padding,32);content.Width=Math.Max(0,Math.Min(1200,scroll.ActualWidth-2*padding-4));notice.Margin=new(padding,16,padding,0);}
        scroll.SizeChanged+=(_,_)=>ResizePage();scroll.Loaded+=(_,_)=>ResizePage();pages[name]=scroll;host.Children.Add(scroll);
    }
    void BuildHotspot(StackPanel content)
    {
        Detach(status);Detach(clients);Detach(ip);
        var panels=new[]{Card(Stack(Secondary(L("StatusLabel")),status)),Card(Stack(Secondary(L("ClientsLabel")),clients)),Card(Stack(Secondary(L("AddressLabel")),ip))};
        var stats=Columns(230,3,12,panels);AutomationProperties.SetAutomationId(stats,"HotspotStats");content.Children.Add(stats);
        Detach(mode);Detach(start);Detach(stop);
        content.Children.Add(Card(Stack(mode,Row(start,stop,Button(L("RefreshStatus"),Refresh)))));
        Detach(ssid);Detach(password);
        var fields=Columns(260,2,16,ssid,password);AutomationProperties.SetAutomationId(fields,"HotspotFields");
        content.Children.Add(Card(Stack(fields,Button(L("SaveHotspotInfo"),()=>Command(new("configure",Ssid:ssid.Text,Password:password.Password))))));
        Detach(deviceList);content.Children.Add(Card(Stack(Text(L("DevicesHeading"),16),deviceList)));
        Detach(diagnostic);content.Children.Add(new Expander{Header=L("DiagnosticsHeading"),HorizontalAlignment=HorizontalAlignment.Stretch,Content=diagnostic});
    }
    static void Detach(FrameworkElement e){if(e.Parent is Panel p)p.Children.Remove(e);else if(e.Parent is Border b)b.Child=null;else if(e.Parent is ContentControl c)c.Content=null;}
    void BuildProxy(StackPanel content)
    {
        Detach(transparentStatus);AutomationProperties.SetAutomationId(transparentStatus,"TransparentStatus");
        content.Children.Add(Card(Stack(Text(L("ModeTransparent"),16),Secondary(L("TransparentHelp")),transparentStatus)));
        var addr=latest?.Hotspot?.Address??"192.168.137.1";
        var endpoint=Text($"{addr}:18877",24);endpoint.FlowDirection=FlowDirection.LeftToRight;
        AutomationProperties.SetAutomationId(tunStatus,"TunStatus");
        var check=Button(L("CheckProxyConnection"),async()=>{await Command(new("checkproxy"));Message(L("CheckProxyConnection"),L("ProxyCheckPassed"),InfoBarSeverity.Success);});AutomationProperties.SetAutomationId(check,"CheckProxyConnection");
        content.Children.Add(Card(Stack(Text(L("ProxyComputerHeading"),16),endpoint,Secondary(L("ProxyEndpointsHelp")),tunStatus,Row(Button(L("CopyProxyAddress"),()=>Copy($"{addr}:18877")),check,Button(L("ConfigureFirewall"),()=>ServiceSetup.ElevateAsync("--firewall"))))));
        var url=$"http://{addr}:18080/wpad.dat";
        var pacAddress=new TextBox{Text=url,IsReadOnly=true,FlowDirection=FlowDirection.LeftToRight};
        content.Children.Add(Card(Stack(Text(L("PacHeading"),16),pacAddress,Secondary(L("PacUrlHelp")),Button(L("CopyPacAddress"),()=>Copy(url)))));
        var discoveryRow=SettingRow(Stack(Text(L("DhcpDiscoveryTitle"),16),Secondary(L("DhcpDiscoveryNote"))),dhcp);dhcp.VerticalAlignment=VerticalAlignment.Center;
        content.Children.Add(Card(Stack(discoveryRow,dhcpStatus,Secondary(L("DhcpCompatibility")))));
        content.Children.Add(Card(Stack(Text(L("MobileDevicesHeading"),16),Secondary(L("MobileDevicesHelp")),Button(L("ExportMobileProfile"),ExportMobile))));
    }
    void BuildSettings(StackPanel content)
    {
        Detach(startup);Detach(bootMode);
        var row=SettingRow(Stack(Text(L("StartupHeading"),16),Secondary(L("StartupHelp"))),startup);startup.VerticalAlignment=VerticalAlignment.Center;
        content.Children.Add(Card(Stack(row)));
        content.Children.Add(Card(Stack(Text(L("StartupActionHeading"),16),Secondary(L("StartupActionHelp")),bootMode)));
        content.Children.Add(Card(Stack(Text(L("LanguageHeading"),16),language,Secondary(L("LanguageHelp")))));
    }
    void BuildAbout(StackPanel content)
    {
        var version=typeof(MainWindow).Assembly.GetName().Version;
        content.Children.Add(Card(Stack(Text("EasyHotspot",24),Secondary(Localization.Format("VersionLine",$"{version?.Major}.{version?.Minor}.{version?.Build}")),Secondary("© 2026 RiversLee9312 and EasyHotspot contributors"),
            Row(Button(L("RepositoryLink"),async()=>{await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/RiversLee9312/EasyHotspot"));}),
                Button(L("ReleaseLink"),async()=>{await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/RiversLee9312/EasyHotspot/releases/latest"));})))));
        content.Children.Add(Card(Stack(Text(L("ProjectLicense"),16),Text("MIT"),Secondary(L("LicenseScope")))));
        content.Children.Add(Card(Stack(Text(L("DocumentationHeading"),16),new DocumentationView())));
    }
    async Task ExportMobile()
    {
        var picker=new FileSavePicker{SuggestedFileName="EasyHotspot-Mobile"};picker.FileTypeChoices.Add(L("JsonProfile"),new List<string>{".json"});picker.FileTypeChoices.Add(L("YamlProfile"),new List<string>{".yaml"});InitializeWithWindow.Initialize(picker,WindowNative.GetWindowHandle(this));var file=await picker.PickSaveFileAsync();if(file is null)return;
        var address=latest?.Hotspot?.Address??"192.168.137.1";
        var text=file.FileType.Equals(".yaml",StringComparison.OrdinalIgnoreCase)?MobileProfiles.Clash(address):(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Assets","Mobile-Relay.json"))).Replace("192.168.137.1",address);
        await File.WriteAllTextAsync(file.Path,text);Message(L("ExportedTitle"),L("ExportedMessage"),InfoBarSeverity.Success);
    }
    Task Copy(string value){var data=new DataPackage();data.SetText(value);Clipboard.SetContent(data);Message(L("CopiedTitle"),L("CopiedMessage"),InfoBarSeverity.Success);return Task.CompletedTask;}
    void Message(string title,string value,InfoBarSeverity severity){notice.ActionButton=null;notice.Title=title;notice.Message=value;notice.Severity=severity;notice.IsOpen=true;}
    internal Task RunOperationAsync(Func<Task> action)=>Action(action);
    async Task Action(Func<Task> action,string? description=null)
    {
        if(busy)return;busy=true;operationStatus.Text=description??L("Working");UpdateControls();
        try{await Task.Yield();await action();await Refresh();}catch(Exception ex){Message(L("OperationFailed"),Localization.Error(ex.Message),InfoBarSeverity.Error);}finally{busy=false;UpdateControls();}
    }
    async Task Command(ControlRequest request)
    {
        var installed=await Task.Run(()=>ServiceSetup.Installed);
        if(installed && await ControlProtocol.Send(new("status"),500) is null)await ServiceSetup.ElevateAsync("--ensure-service");
        var reply=await ControlProtocol.Send(request,45000);
        if(reply is not null){if(!reply.Success)throw new InvalidOperationException(reply.Error);return;}
        if(installed)throw new InvalidOperationException(L("ServiceNotReady"));
        await Task.Run(async()=>{switch(request.Command){case "start":await local.StartAsync(request.Mode??"offline");break;case "stop":await local.StopAsync();break;case "configure":await local.Hotspot.ConfigureAsync(request.Ssid??"",request.Password??"");break;case "checkproxy":await local.CheckProxyAsync();break;}});
    }
    public Task Refresh()=>refreshTask is {IsCompleted:false}?refreshTask:refreshTask=RefreshCore();
    async Task RefreshCore()
    {
        refreshing=true;UpdateControls();
        try {
            var installationTask=Task.Run(()=>new{Installed=ServiceSetup.Installed,Automatic=ServiceSetup.AutoStartEnabled,Settings=ServiceSettings.Read()});
            var installation=await installationTask;serviceInstalled=installation.Installed;
            var reply=await ControlProtocol.Send(new("status"),serviceInstalled?2500:400);latest=reply?.State??(serviceInstalled?latest:null)??await Task.Run(local.Read);
            var h=latest.Hotspot;status.Text=h?.State=="On"?L("StatusOn"):L("StatusOff");clients.Text=(h?.Clients??0).ToString(Localization.FormattingCulture);ip.Text=h?.Address??"—";
            if(h?.State=="On"){updatingMode=true;mode.SelectedIndex=latest.Mode switch{"proxy"=>1,"sharing"=>2,"transparent"=>3,_=>0};updatingMode=false;}
            deviceList.Text=h?.Devices.Length>0?string.Join("\n",h.Devices):L("NoDevicesConnected");
            if(initial&&h is not null){ssid.Text=h.Ssid;password.Password=h.Password;updatingStartup=true;startup.IsOn=installation.Automatic;bootMode.SelectedIndex=installation.Settings.StartupMode switch{"offline"=>1,"proxy"=>2,"transparent"=>3,_=>0};updatingStartup=false;initial=false;}
            tunStatus.Text=latest.Tun is null?L("NoTunDetected"):Localization.Format("TunDetectedLine",latest.Tun);
            var transparent=latest.Transparent;transparentStatus.Text=transparent?.Error is not null?Localization.Error(transparent.Error):transparent?.Running==true?Localization.Format("TransparentRunning",transparent.Received,transparent.Sent,transparent.Upstream):L("StatusOff");
            diagnostic.Text=string.Join("\n",new[]{Localization.Format("InternetStatusLine",h?.Connectivity??L("Unknown")),Localization.Format("CurrentSourceLine",h?.Source??"—"),tunStatus.Text,reply is null?L("RunModeApp"):L("RunModeService")}.Concat(latest.Logs.Select(Localization.Log)).Concat(latest.Error is null?[]:new[]{Localization.Error(latest.Error)}));
            bootMode.IsEnabled=serviceInstalled&&!busy;
            var discovery=latest.Dhcp;updatingDhcp=true;dhcp.IsOn=discovery.Enabled;updatingDhcp=false;
            dhcpStatus.Text=discovery.Error is not null?Localization.Error(discovery.Error):discovery.Running?Localization.Format("DhcpRunning",discovery.Advertisements):L(discovery.Enabled?"DhcpWaiting":"DhcpOff");
            UpdateControls();
        } finally {refreshing=false;UpdateControls();}
    }
    void UpdateControls(){var on=latest?.Hotspot?.State=="On";bool waiting=busy||(initial&&refreshing);foreach(var button in actionButtons)button.IsEnabled=!waiting;start.IsEnabled=!waiting&&!on;stop.IsEnabled=!waiting&&on;mode.IsEnabled=!waiting&&!on;ssid.IsEnabled=password.IsEnabled=!waiting&&!on;dhcp.IsEnabled=!waiting&&!on;startup.IsEnabled=language.IsEnabled=!waiting;bootMode.IsEnabled=serviceInstalled&&!waiting;progress.IsActive=waiting;operationPanel.Visibility=waiting?Visibility.Visible:Visibility.Collapsed;if(waiting && !busy)operationStatus.Text=L("Working");}
}
