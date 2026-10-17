using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Xml.Linq;
using EasyHotspot.Core;
using System.Runtime.InteropServices.WindowsRuntime;

namespace EasyHotspot.UITests;
[TestClass,DoNotParallelize]
public sealed class NativeUiTests
{
    MainWindow Window=>UnitTestApp.Main!;
    [UITestMethod] public async Task RunsOnActualWinUiDispatcher()
    {
        await Task.Yield();Assert.IsTrue(Window.DispatcherQueue.HasThreadAccess);Assert.IsInstanceOfType<MicaBackdrop>(Window.SystemBackdrop);Assert.AreEqual(3,Window.Navigation.MenuItems.Count);
    }
    [UITestMethod] public async Task ModeMatchesTheRunningBackendOrDefaultsToOffline()
    {
        await Window.Refresh();var reply=await ControlProtocol.Send(new("status"));Show("hotspot");var combo=Elements(Window.Content).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="HotspotMode");
        var running=reply?.State?.Hotspot?.State=="On";Assert.AreEqual(running?reply!.State!.Mode switch{"proxy"=>1,"sharing"=>2,_=>0}:0,combo.SelectedIndex);Assert.AreEqual(!running,combo.IsEnabled);
    }
    [UITestMethod] public void ProxyPageShowsPacAndCompatibilityLimits()
    {
        Show("proxy");var nodes=Elements(Window.Content).ToList();Assert.IsTrue(nodes.OfType<TextBox>().Any(x=>x.Text.Contains(":18080/wpad.dat")));Assert.IsTrue(nodes.OfType<TextBlock>().Any(x=>x.Text==Localization.Get("DhcpCompatibility")));var toggle=nodes.OfType<ToggleSwitch>().Single(x=>AutomationProperties.GetAutomationId(x)=="DhcpAutoDiscovery");Assert.AreEqual(ServiceSettings.Read().DhcpDiscovery,toggle.IsOn);
    }
    [UITestMethod] public void StartupIsExplicitAndMatchesSystemState()
    {
        Show("settings");var toggle=Elements(Window.Content).OfType<ToggleSwitch>().Single(x=>AutomationProperties.GetAutomationId(x)=="HighPriorityStartup");Assert.AreEqual(ServiceSetup.AutoStartEnabled,toggle.IsOn);var picker=Elements(Window.Content).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="AppLanguage");Assert.AreEqual(24,picker.Items.Count);Assert.AreEqual(Localization.CurrentLanguage,((ComboBoxItem)picker.SelectedItem).Tag);
    }
    [UITestMethod] public void MrtResolvesEveryLocaleAndFallsBackToEnglish()
    {
        var manager=new ResourceManager(ResourceLoader.GetDefaultResourceFilePath());var resources=manager.MainResourceMap.GetSubtree("Resources");
        var keys=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"Strings","en-US","Resources.resw")).Root!.Elements("data").Select(x=>(string)x.Attribute("name")!).ToArray();
        foreach(var language in Localization.SupportedLanguages)
        {
            var context=manager.CreateResourceContext();context.QualifierValues["Language"]=language;
            foreach(var key in keys)
            {
                var candidate=resources.GetValue(key,context);Assert.IsFalse(string.IsNullOrWhiteSpace(candidate.ValueAsString),$"Missing {key} for {language}.");
                var languageQualifier=candidate.QualifierValues.FirstOrDefault(x=>x.Key.Equals("Language",StringComparison.OrdinalIgnoreCase)).Value;
                Assert.IsTrue(string.Equals(language,languageQualifier,StringComparison.OrdinalIgnoreCase),$"{language}/{key} resolved as {languageQualifier}.");
            }
            var languageHelp=resources.GetValue("LanguageHelp",context).ValueAsString;
            if(language!="en-US")Assert.AreNotEqual(resources.GetValue("LanguageHelp",CreateContext(manager,"en-US")).ValueAsString,languageHelp,$"{language} appears to be using the English fallback.");
        }
        Assert.IsTrue(Localization.IsRightToLeft("ar-SA"));Assert.IsTrue(Localization.IsRightToLeft("he-IL"));Assert.IsFalse(Localization.IsRightToLeft("en-US"));
        Assert.AreEqual("Hotspot",resources.GetValue("PageHotspot",CreateContext(manager,"qut-PE")).ValueAsString);
    }
    [UITestMethod] public void RepeatedNavigationKeepsControlsUsable()
    {
        foreach(var page in new[]{"hotspot","proxy","settings","hotspot","proxy","hotspot"})Show(page);
        Assert.AreEqual(1,Elements(Window.Content).OfType<Button>().Count(x=>AutomationProperties.GetAutomationId(x)=="StartHotspot"));
    }
    [UITestMethod] public async Task GeneratedIconsExistAndFollowNativeTint()
    {
        foreach(var item in Window.Navigation.MenuItems.Cast<NavigationViewItem>())Assert.IsInstanceOfType<ImageIcon>(item.Icon);
        foreach(var name in new[]{"hotspot","proxy","settings"})foreach(double scale in new[]{1.0,1.25,1.5,2.0,2.5,3.0,4.0}) {
            using var bitmap=await NavigationIcons.RasterizeAsync(name,scale,Microsoft.UI.Colors.Red);
            int size=(int)Math.Ceiling(20*scale);Assert.AreEqual(size,bitmap.PixelWidth);Assert.AreEqual(size,bitmap.PixelHeight);
            var pixels=new byte[size*size*4];bitmap.CopyToBuffer(pixels.AsBuffer());
            int left=size,right=0,smooth=0;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                int p=(y*size+x)*4,alpha=pixels[p+3];if(alpha==0)continue;
                Assert.AreEqual((byte)0,pixels[p]);Assert.AreEqual((byte)0,pixels[p+1]);Assert.AreEqual((byte)alpha,pixels[p+2]);
                if(alpha>=64){left=Math.Min(left,x);right=Math.Max(right,x+1);}if(alpha<255)smooth++;
            }
            Assert.IsTrue(right-left>=size*0.75,$"{name} is too small at {scale}x.");Assert.IsTrue(smooth>0,$"{name} lost edge transparency at {scale}x.");
            Assert.AreEqual((byte)0,pixels[3]);Assert.AreEqual((byte)0,pixels[^1]);
        }
        Assert.IsTrue(File.Exists(Path.Combine(AppContext.BaseDirectory,"Assets","app.ico")));
    }
    [UITestMethod] public void DefaultWindowHasRoomForItsControlsAtCurrentDpi()
    {
        var root=(FrameworkElement)Window.Content;root.UpdateLayout();Assert.IsTrue(root.ActualWidth>=800,$"Window content is only {root.ActualWidth} logical pixels wide.");
    }
    [UITestMethod] public void WindowChromeStaysLeftToRightWhenContentIsMirrored()
    {
        Assert.AreEqual(FlowDirection.LeftToRight,((FrameworkElement)Window.Content).FlowDirection);
        Assert.AreEqual(Localization.IsRightToLeft(Localization.CurrentLanguage)?FlowDirection.RightToLeft:FlowDirection.LeftToRight,Window.Navigation.FlowDirection);
    }
    [UITestMethod] public void ProxyPageOffersAConnectionCheckAndTunStatus()
    {
        Show("proxy");var nodes=Elements(Window.Content).ToArray();Assert.AreEqual(1,nodes.OfType<Button>().Count(x=>AutomationProperties.GetAutomationId(x)=="CheckProxyConnection"));Assert.AreEqual(1,nodes.OfType<TextBlock>().Count(x=>AutomationProperties.GetAutomationId(x)=="TunStatus"));
        StringAssert.Contains(Localization.Log("12:34:56  Error_ProxyRequest|ConnectionRefused (10061)"),"ConnectionRefused (10061)");Assert.IsFalse(Localization.Log("12:34:56  Error_ProxyRequest|test").Contains("Error_ProxyRequest"));
    }
    [UITestMethod] public async Task PagesStayInsideTheViewportAtDifferentWindowSizes()
    {
        var root=(FrameworkElement)Window.Content;double scale=root.XamlRoot.RasterizationScale;
        var presenter=(Microsoft.UI.Windowing.OverlappedPresenter)Window.AppWindow.Presenter;
        var original=Window.AppWindow.Size;var direction=Window.Navigation.FlowDirection;
        try {
            presenter.Restore();
            foreach(var flow in new[]{FlowDirection.LeftToRight,FlowDirection.RightToLeft}) {
            Window.Navigation.FlowDirection=flow;
            foreach(var dimensions in new[]{(360,480),(480,360),(640,480),(800,600),(1120,760),(1440,820)}) {
                Window.AppWindow.ResizeClient(new((int)(dimensions.Item1*scale),(int)(dimensions.Item2*scale)));await Task.Delay(100);
                foreach(var page in new[]{"hotspot","proxy","settings"}){Show(page);await Task.Delay(60);AssertLayout(page);}
            }
            presenter.Maximize();await Task.Delay(120);
            foreach(var page in new[]{"hotspot","proxy","settings"}){Show(page);await Task.Delay(60);AssertLayout(page);}
            presenter.Restore();}
        }finally{Window.Navigation.FlowDirection=direction;presenter.Restore();Window.AppWindow.Resize(original);Show("hotspot");await Task.Delay(100);}
        void AssertLayout(string page)
        {
            root.UpdateLayout();var nodes=Elements(Window.Content).ToArray();
            var content=nodes.OfType<StackPanel>().Single(x=>AutomationProperties.GetAutomationId(x)=="PageContent-"+page);
            var scroll=nodes.OfType<ScrollViewer>().Single(x=>ReferenceEquals(x.Content,content));
            var bounds=content.TransformToVisual(root).TransformBounds(new(0,0,content.ActualWidth,content.ActualHeight));
            Assert.IsTrue(bounds.Left>=-1&&bounds.Right<=root.ActualWidth+1,$"{page} content leaves window at {root.ActualWidth}: {bounds.Left}..{bounds.Right}.");
            Assert.IsTrue(scroll.ScrollableWidth<=1,$"{page} has horizontal overflow at {root.ActualWidth}.");
            foreach(var control in nodes.OfType<Control>().Where(x=>x is Button or ComboBox or TextBox or PasswordBox or ToggleSwitch)) {
                // Navigation templates contain deliberately clipped flyouts; validate page controls only.
                if(!Elements(content).Contains(control))continue;
                var p=control.TransformToVisual(content).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight));
                Assert.IsTrue(p.Left>=-1&&p.Right<=content.ActualWidth+1,$"{page}/{control.GetType().Name} leaves page at {root.ActualWidth}: {p.Left}..{p.Right}>{content.ActualWidth}.");
            }
        }
    }
    void Show(string page){Window.ShowPage(page);((FrameworkElement)Window.Content).UpdateLayout();}
    static Microsoft.Windows.ApplicationModel.Resources.ResourceContext CreateContext(ResourceManager manager,string language){var context=manager.CreateResourceContext();context.QualifierValues["Language"]=language;return context;}
    static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;for(int n=0;n<VisualTreeHelper.GetChildrenCount(root);n++)foreach(var child in Elements(VisualTreeHelper.GetChild(root,n)))yield return child;
    }
}
