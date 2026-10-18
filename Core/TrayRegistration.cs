namespace EasyHotspot.Core;

public enum TrayCommand : uint { Add=0, Modify=1, Delete=2, SetVersion=4 }

// Serialize Shell calls, including shutdown, so a late startup retry cannot
// recreate an icon after Exit or use an icon handle that has been destroyed.
public sealed class TrayRegistration(Func<TrayCommand,Task<bool>> notify) : IAsyncDisposable
{
    readonly SemaphoreSlim gate=new(1,1);
    volatile bool added,disposed;
    public bool IsAdded=>added;
    public async Task<bool> EnsureAsync(bool shellRestarted=false)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try {
            if(disposed)return false;
            if(shellRestarted)added=false;
            if(added && await notify(TrayCommand.Modify).ConfigureAwait(false))return true;
            added=false;
            // Modify also handles an existing registration after an ambiguous
            // Add result without creating a second icon.
            if(!await notify(TrayCommand.Add).ConfigureAwait(false) && !await notify(TrayCommand.Modify).ConfigureAwait(false))return false;
            await notify(TrayCommand.SetVersion).ConfigureAwait(false);
            added=!disposed;return added;
        }finally{gate.Release();}
    }
    public async ValueTask DisposeAsync()
    {
        disposed=true;
        await gate.WaitAsync().ConfigureAwait(false);
        try {await notify(TrayCommand.Delete).ConfigureAwait(false);added=false;}
        finally{gate.Release();}
    }
}
