using InstantTranslate.Services;

namespace InstantTranslate.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void FirstInstance_IsPrimary_AndSecondInstanceIsNot()
    {
        var instanceName = CreateUniqueInstanceName();
        using var primary = SingleInstanceCoordinator.Acquire(instanceName);
        using var secondary = SingleInstanceCoordinator.Acquire(instanceName);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
    }

    [Fact]
    public void SecondInstance_SignalsPrimary_WhenListenerIsAlreadyRunning()
    {
        var instanceName = CreateUniqueInstanceName();
        using var activationReceived = new ManualResetEventSlim();
        using var primary = SingleInstanceCoordinator.Acquire(instanceName);
        primary.StartListening(activationReceived.Set);

        using var secondary = SingleInstanceCoordinator.Acquire(instanceName);

        Assert.False(secondary.IsPrimary);
        Assert.True(activationReceived.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ActivationSignal_IsRetained_UntilPrimaryStartsListening()
    {
        var instanceName = CreateUniqueInstanceName();
        using var activationReceived = new ManualResetEventSlim();
        using var primary = SingleInstanceCoordinator.Acquire(instanceName);
        using var secondary = SingleInstanceCoordinator.Acquire(instanceName);

        primary.StartListening(activationReceived.Set);

        Assert.True(activationReceived.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Dispose_ReleasesInstanceNameForANewPrimary()
    {
        var instanceName = CreateUniqueInstanceName();
        var first = SingleInstanceCoordinator.Acquire(instanceName);
        Assert.True(first.IsPrimary);

        first.Dispose();
        first.Dispose();

        using var replacement = SingleInstanceCoordinator.Acquire(instanceName);
        Assert.True(replacement.IsPrimary);
    }

    [Fact]
    public void SecondaryInstance_CannotStartListener()
    {
        var instanceName = CreateUniqueInstanceName();
        using var primary = SingleInstanceCoordinator.Acquire(instanceName);
        using var secondary = SingleInstanceCoordinator.Acquire(instanceName);

        Assert.Throws<InvalidOperationException>(() => secondary.StartListening(() => { }));
    }

    [Fact]
    public void DefaultName_ContainsCurrentSessionId()
    {
        var defaultName = SingleInstanceCoordinator.CreateDefaultInstanceName();

        Assert.Contains($"Session{System.Diagnostics.Process.GetCurrentProcess().SessionId}", defaultName);
    }

    private static string CreateUniqueInstanceName()
    {
        return $"InstantTranslate.Tests.{Guid.NewGuid():N}";
    }
}
