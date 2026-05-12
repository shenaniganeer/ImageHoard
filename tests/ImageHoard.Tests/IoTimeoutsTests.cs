using ImageHoard.Core.Io;

namespace ImageHoard.Tests;

public sealed class IoTimeoutsTests
{
    [Fact]
    public void CreateLinkedTimeout_cancels_linked_token_after_timeout()
    {
        using var parent = new CancellationTokenSource();
        using var linked = IoTimeouts.CreateLinkedTimeout(parent.Token, TimeSpan.FromMilliseconds(50), out var token);
        Assert.False(token.IsCancellationRequested);
        Thread.Sleep(120);
        Assert.True(token.IsCancellationRequested);
    }
}
