using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.ComponentModel;
using Microsoft.Win32;

namespace EasyHotspot.Core;
public static class ServiceSetup
{
    public const string ServiceName="EasyHotspotService";
    public static string InstallDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"EasyHotspot");
    public static string DataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"EasyHotspot");
    public static bool Installed {get{using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServiceName);return key is not null;}}
    public static bool IsAdmin=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static string OwnerSid {get{using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EasyHotspot");return key?.GetValue("OwnerSid")?.ToString() ?? WindowsIdentity.GetCurrent().User!.Value;}}
    public static int Run(string exe,params string[] args)
    {
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var p=Process.Start(start)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();p.WaitForExit();Task.WhenAll(output,error).GetAwaiter().GetResult();return p.ExitCode;
    }
    public static async Task ElevateAsync(string action)
    {
        var helper=Path.Combine(AppContext.BaseDirectory,"EasyHotspot.Service.exe");
        if(!File.Exists(helper))throw new FileNotFoundException("Error_HelperMissing",helper);
        var args=$"{action} --source \"{AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)}\" --owner {WindowsIdentity.GetCurrent().User!.Value}";
        using var p=Process.Start(new ProcessStartInfo(helper,args){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden}) ?? throw new IOException("Error_ElevationStart");
        await p.WaitForExitAsync();if(p.ExitCode!=0)throw new InvalidOperationException("Error_AdminIncomplete");
    }
    public static void Install(string source,string owner)
    {
        if(!IsAdmin)throw new UnauthorizedAccessException();
        var fresh=!Installed;
        _=new SecurityIdentifier(owner);
        var dest=InstallDirectory;Directory.CreateDirectory(dest);
        if(!Path.GetFullPath(source).TrimEnd('\\').Equals(Path.GetFullPath(dest).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
            foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)) {
                var relative=Path.GetRelativePath(source,file);var target=Path.Combine(dest,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target,true);
            }
        using(var key=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\EasyHotspot"))key.SetValue("OwnerSid",owner);
        Directory.CreateDirectory(DataDirectory);
        if(fresh)ServiceSettings.Save("none");
        Firewall(Path.Combine(dest,"EasyHotspot.exe"));Firewall(Path.Combine(dest,"EasyHotspot.Service.exe"),"Service");
        var binary=$"\"{Path.Combine(dest,"EasyHotspot.Service.exe")}\" --service";
        var command=Installed?"config":"create";
        if(Run("sc.exe",command,ServiceName,"binPath=",binary,"start=","auto","obj=","LocalSystem","DisplayName=","EasyHotspot")!=0)throw new IOException("Error_RegisterService");
        Run("sc.exe","description",ServiceName,"EasyHotspot background service");
        Run("sc.exe","failure",ServiceName,"reset=","86400","actions=","restart/10000/restart/30000/none/0");
        var started=Run("sc.exe","start",ServiceName);
        if(started!=0&&started!=1056)throw new IOException("Error_StartService");
        using var service=new ServiceController(ServiceName);
        service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));
    }
    public static void Uninstall()
    {
        if(!IsAdmin)throw new UnauthorizedAccessException();
        if(!Installed)return;
        try {
            using var service=new ServiceController(ServiceName);
            service.Refresh();
            if(service.Status!=ServiceControllerStatus.Stopped){
                if(service.Status!=ServiceControllerStatus.StopPending)service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));
            }
        } catch(InvalidOperationException ex) when(ex.InnerException is Win32Exception {NativeErrorCode:1060}) {return;}
        var removed=Run("sc.exe","delete",ServiceName);
        if(removed!=0&&removed!=1060)throw new IOException("Error_RemoveService");
    }
    public static void Cleanup()
    {
        Uninstall();
        var script="$ErrorActionPreference='Stop';Get-NetFirewallRule -Name 'EasyHotspot-*' -ErrorAction SilentlyContinue|Remove-NetFirewallRule -ErrorAction Stop";
        var encoded=Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        if(Run(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile","-EncodedCommand",encoded)!=0)throw new IOException("Error_Firewall");
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\EasyHotspot",false);
        foreach(var name in new[]{"settings.json","setup-error.txt"})File.Delete(Path.Combine(DataDirectory,name));
        if(Directory.Exists(DataDirectory)&&!Directory.EnumerateFileSystemEntries(DataDirectory).Any())Directory.Delete(DataDirectory);
    }
    public static void Firewall(string program,string suffix="App")
    {
        if(!IsAdmin)throw new UnauthorizedAccessException();
        static string Q(string value)=>"'"+value.Replace("'","''")+"'";
        var script=$"$ErrorActionPreference='Stop';Get-NetFirewallRule -Name 'EasyHotspot-{suffix}-*' -ErrorAction SilentlyContinue|Remove-NetFirewallRule;"+
            $"$p=@{{Direction='Inbound';Action='Allow';Program={Q(program)};LocalAddress='192.168.137.1';RemoteAddress='192.168.137.0/24';Profile='Any'}};"+
            $"New-NetFirewallRule @p -Name 'EasyHotspot-{suffix}-TCP' -DisplayName 'EasyHotspot TCP' -Protocol TCP -LocalPort 18877,18080|Out-Null;"+
            $"New-NetFirewallRule @p -Name 'EasyHotspot-{suffix}-UDP' -DisplayName 'EasyHotspot SOCKS UDP' -Protocol UDP|Out-Null;";
        var encoded=Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        if(Run(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile","-EncodedCommand",encoded)!=0)throw new IOException("Error_Firewall");
    }
}
