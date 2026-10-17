using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Xml.Linq;
using EasyHotspot.Core;

namespace EasyHotspot.UITests;
[TestClass,DoNotParallelize]
public sealed class NativeUiTests
{
    MainWindow Window=>UnitTestApp.Main!;
    [UITestMethod] public async Task RunsOnActualWinUiDispatcher()
    {
        await Task.Yield();Assert.IsTrue(Window.DispatcherQueue.HasThreadAccess);Assert.IsInstanceOfType<MicaBackdrop>(Window.SystemBackdrop);Assert.AreEqual(3,Window.Navigation.MenuItems.Count);
    }
    [UITestMethod] public void OfflineIsTheDefaultMode()
    {
        Show("hotspot");var combo=Elements(Window.Content).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="HotspotMode");Assert.AreEqual(0,combo.SelectedIndex);
    }
    [UITestMethod] public void ProxyPageShowsPacAndCompatibilityLimits()
    {
        Show("proxy");var nodes=Elements(Window.Content).ToList();Assert.IsTrue(nodes.OfType<TextBox>().Any(x=>x.Text.Contains(":18080/wpad.dat")));Assert.IsTrue(nodes.OfType<InfoBar>().Any(x=>x.Message==Localization.Get("DhcpDiscoveryNote")));
    }
    [UITestMethod] public void StartupIsExplicitAndMatchesSystemState()
    {
        Show("settings");var toggle=Elements(Window.Content).OfType<ToggleSwitch>().Single(x=>AutomationProperties.GetAutomationId(x)=="HighPriorityStartup");Assert.AreEqual(ServiceSetup.Installed,toggle.IsOn);var picker=Elements(Window.Content).OfType<ComboBox>().Single(x=>AutomationProperties.GetAutomationId(x)=="AppLanguage");Assert.AreEqual(24,picker.Items.Count);Assert.AreEqual(Localization.CurrentLanguage,((ComboBoxItem)picker.SelectedItem).Tag);
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
    [UITestMethod] public void GeneratedIconsExistAndFollowNativeTint()
    {
        foreach(var item in Window.Navigation.MenuItems.Cast<NavigationViewItem>()){Assert.IsInstanceOfType<BitmapIcon>(item.Icon);var icon=(BitmapIcon)item.Icon;Assert.IsTrue(icon.ShowAsMonochrome);Assert.IsTrue(File.Exists(icon.UriSource.LocalPath));}
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
    void Show(string page){Window.ShowPage(page);((FrameworkElement)Window.Content).UpdateLayout();}
    static Microsoft.Windows.ApplicationModel.Resources.ResourceContext CreateContext(ResourceManager manager,string language){var context=manager.CreateResourceContext();context.QualifierValues["Language"]=language;return context;}
    static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;for(int n=0;n<VisualTreeHelper.GetChildrenCount(root);n++)foreach(var child in Elements(VisualTreeHelper.GetChild(root,n)))yield return child;
    }
}
