using EasyCapture.Core;

namespace EasyCapture.Tests;

public class SingleInstanceTests
{
    private static string NewName() => $@"Local\EasyCapture.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void FirstInstance_Succeeds()
    {
        using var first = new SingleInstance(NewName());

        Assert.True(first.IsFirstInstance);
    }

    [Fact]
    public void SecondInstance_WithSameName_Fails()
    {
        var name = NewName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void AfterDispose_CanBeAcquiredAgain()
    {
        var name = NewName();
        var first = new SingleInstance(name);
        first.Dispose();

        using var again = new SingleInstance(name);

        Assert.True(again.IsFirstInstance);
    }

    [Fact]
    public void DefaultName_IsLocalEasyCaptureSingleInstance()
    {
        Assert.Equal(@"Local\EasyCapture.SingleInstance", SingleInstance.DefaultMutexName);
    }
}