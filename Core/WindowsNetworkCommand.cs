using System.Diagnostics;
using System.Text;

namespace EasyHotspot.Core;

internal static class WindowsNetworkCommand
{
    public static async Task Run(string script,string errorKey="Error_TransparentCapture")
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-OutputFormat");start.ArgumentList.Add("Text");start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';try{"+script+"}catch{[Console]::Error.WriteLine($captureStage+': '+$_.Exception.Message);exit 1};exit 0")));
        using var process=Process.Start(start)!;var error=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));}catch(TimeoutException){process.Kill(true);await process.WaitForExitAsync();throw new IOException(errorKey+"|Windows network configuration timed out");}
        await output;var detail=await error;if(process.ExitCode!=0)throw new IOException(errorKey+"|"+detail.Replace('|',':'));
    }
}
