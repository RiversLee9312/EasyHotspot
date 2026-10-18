using EasyHotspot.Core;

namespace EasyHotspot.Tests;

[TestClass]
public sealed class TrayRegistrationTests
{
    [TestMethod] public async Task EarlyStartupFailureCanRecoverOnALaterAttempt()
    {
        bool ready=false;var commands=new List<TrayCommand>();
        await using var tray=new TrayRegistration(command=>{commands.Add(command);return Task.FromResult(ready);});
        Assert.IsFalse(await tray.EnsureAsync());Assert.IsFalse(tray.IsAdded);
        ready=true;Assert.IsTrue(await tray.EnsureAsync());Assert.IsTrue(tray.IsAdded);Assert.AreEqual(TrayCommand.SetVersion,commands[^1]);
    }
    [TestMethod] public async Task ExplorerRestartReRegistersAndRestoresTheCallbackVersion()
    {
        var commands=new List<TrayCommand>();await using var tray=new TrayRegistration(command=>{commands.Add(command);return Task.FromResult(true);});
        await tray.EnsureAsync();commands.Clear();Assert.IsTrue(await tray.EnsureAsync(true));
        CollectionAssert.AreEqual(new[]{TrayCommand.Add,TrayCommand.SetVersion},commands);
    }
    [TestMethod] public async Task LostIconRecoversEvenIfRestartBroadcastWasMissed()
    {
        bool lose=false;var commands=new List<TrayCommand>();await using var tray=new TrayRegistration(command=>{commands.Add(command);bool ok=!(lose && command==TrayCommand.Modify);lose=false;return Task.FromResult(ok);});
        await tray.EnsureAsync();commands.Clear();lose=true;Assert.IsTrue(await tray.EnsureAsync());
        CollectionAssert.AreEqual(new[]{TrayCommand.Modify,TrayCommand.Add,TrayCommand.SetVersion},commands);
    }
    [TestMethod] public async Task ExitWaitsForPendingCreationThenDeletesAndPreventsFurtherRetries()
    {
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var commands=new List<TrayCommand>();
        var tray=new TrayRegistration(async command=>{commands.Add(command);if(command==TrayCommand.Add){started.SetResult();return await release.Task;}return true;});
        var add=tray.EnsureAsync();await started.Task;var exit=tray.DisposeAsync().AsTask();Assert.IsFalse(exit.IsCompleted);release.SetResult(true);await add;await exit;
        Assert.IsFalse(tray.IsAdded);Assert.AreEqual(TrayCommand.Delete,commands[^1]);int count=commands.Count;Assert.IsFalse(await tray.EnsureAsync());Assert.AreEqual(count,commands.Count);
    }
}
