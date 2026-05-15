using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class MouseButtonClickChainTrackerTests
{
    [Fact]
    public void Same_button_same_spot_within_time_increments_chain()
    {
        long t = 0;
        var metrics = new MouseButtonClickMetrics(500, 100, 100);
        var tr = new MouseButtonClickChainTracker(() => t);
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 10, 20, metrics));
        t = 100;
        Assert.Equal(2, tr.OnMouseButtonDown("Left", 10, 20, metrics));
        t = 200;
        Assert.Equal(3, tr.OnMouseButtonDown("Left", 10, 20, metrics));
        t = 300;
        Assert.Equal(3, tr.OnMouseButtonDown("Left", 10, 20, metrics));
    }

    [Fact]
    public void Time_gap_resets_chain_to_one()
    {
        long t = 0;
        var metrics = new MouseButtonClickMetrics(500, 100, 100);
        var tr = new MouseButtonClickChainTracker(() => t);
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 0, 0, metrics));
        t = 600;
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 0, 0, metrics));
    }

    [Fact]
    public void Distance_from_anchor_resets_chain()
    {
        long t = 0;
        var metrics = new MouseButtonClickMetrics(500, 100, 100);
        var tr = new MouseButtonClickChainTracker(() => t);
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 0, 0, metrics));
        t = 50;
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 150, 0, metrics));
    }

    [Fact]
    public void Different_button_resets_chain()
    {
        long t = 0;
        var metrics = new MouseButtonClickMetrics(500, 100, 100);
        var tr = new MouseButtonClickChainTracker(() => t);
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 0, 0, metrics));
        t = 50;
        Assert.Equal(1, tr.OnMouseButtonDown("Right", 0, 0, metrics));
    }

    [Fact]
    public void Reset_clears_state()
    {
        long t = 0;
        var metrics = new MouseButtonClickMetrics(500, 100, 100);
        var tr = new MouseButtonClickChainTracker(() => t);
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 0, 0, metrics));
        t = 10;
        Assert.Equal(2, tr.OnMouseButtonDown("Left", 0, 0, metrics));
        tr.Reset();
        t = 20;
        Assert.Equal(1, tr.OnMouseButtonDown("Left", 0, 0, metrics));
    }
}
