using InstantTranslate.Services;

namespace InstantTranslate.Tests;

public sealed class RequestVersionGateTests
{
    [Fact]
    public void NewRequest_CancelsAndInvalidatesPreviousRequest()
    {
        using var gate = new RequestVersionGate();
        var first = gate.BeginRequest();

        var second = gate.BeginRequest();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(first.Version));
        Assert.True(gate.IsCurrent(second.Version));
        Assert.False(second.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void CancelActive_InvalidatesCurrentRequest()
    {
        using var gate = new RequestVersionGate();
        var lease = gate.BeginRequest();

        gate.CancelActive();

        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.False(gate.IsCurrent(lease.Version));
    }
}
