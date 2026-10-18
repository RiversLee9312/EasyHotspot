using System.Security.Principal;

namespace EasyHotspot.Core;

public sealed class SingleInstanceGuard : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle activation;
    RegisteredWaitHandle? wait;
    public bool IsPrimary{get;}
    public event Action? Activated;
    public SingleInstanceGuard(string? key=null)
    {
        var name=@"Local\EasyHotspot."+(key??WindowsIdentity.GetCurrent().User!.Value);
        // Create the event first: a second process can signal it even while
        // the first process is still constructing its XAML window.
        activation=new(false,EventResetMode.AutoReset,name+".Activate");
        mutex=new(true,name+".Instance",out bool created);IsPrimary=created;
        if(IsPrimary)wait=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>Activated?.Invoke(),null,Timeout.Infinite,false);
    }
    public void ActivateExisting()=>activation.Set();
    public void Dispose(){wait?.Unregister(null);activation.Dispose();if(IsPrimary)mutex.ReleaseMutex();mutex.Dispose();}
}
