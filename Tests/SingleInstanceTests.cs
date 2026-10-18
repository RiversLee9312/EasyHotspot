using EasyHotspot.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class SingleInstanceTests
{
    [TestMethod] public void SecondInstanceSignalsTheOwnerAndCannotOwnTheKey()
    {
        using var first=new SingleInstanceGuard("test-"+Guid.NewGuid());
        Assert.IsTrue(first.IsPrimary);
    }
    [TestMethod] public void DuplicateLaunchActivatesTheExistingInstance()
    {
        var key="test-"+Guid.NewGuid();using var activated=new ManualResetEventSlim();
        using var first=new SingleInstanceGuard(key);first.Activated+=activated.Set;
        using var second=new SingleInstanceGuard(key);Assert.IsFalse(second.IsPrimary);second.ActivateExisting();
        Assert.IsTrue(activated.Wait(TimeSpan.FromSeconds(3)),"The original UI must receive the activation request.");
    }
    [TestMethod] public void CleanExitReleasesTheInstanceKey()
    {
        var key="test-"+Guid.NewGuid();using(var first=new SingleInstanceGuard(key)){Assert.IsTrue(first.IsPrimary);}
        using var next=new SingleInstanceGuard(key);Assert.IsTrue(next.IsPrimary);
    }
}
