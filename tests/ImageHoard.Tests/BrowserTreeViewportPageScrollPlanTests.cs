using ImageHoard.Core.Browse;
using Xunit;

namespace ImageHoard.Tests;

public sealed class BrowserTreeViewportPageScrollPlanTests
{
    [Fact]
    public void Compute_target_fully_inside_viewport_is_visible_with_same_offset()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 100,
            viewportHeight: 200,
            targetTop: 120,
            targetHeight: 40,
            scrollableHeight: 500);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetVisible, d.Result);
        Assert.Equal(100, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_target_one_row_below_bottom_scrolls_one_page()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 100,
            viewportHeight: 200,
            targetTop: 290,
            targetHeight: 20,
            scrollableHeight: 1000);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetBelowViewport, d.Result);
        Assert.Equal(300, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_target_three_pages_above_scrolls_up_and_clamps_to_zero()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 600,
            viewportHeight: 100,
            targetTop: 50,
            targetHeight: 30,
            scrollableHeight: 800);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetAboveViewport, d.Result);
        Assert.Equal(0, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_partially_below_triggers_below_scroll()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 0,
            viewportHeight: 100,
            targetTop: 80,
            targetHeight: 40,
            scrollableHeight: 500);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetBelowViewport, d.Result);
        Assert.Equal(100, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_partially_above_triggers_above_scroll()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 150,
            viewportHeight: 100,
            targetTop: 120,
            targetHeight: 80,
            scrollableHeight: 500);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetAboveViewport, d.Result);
        Assert.Equal(50, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_degenerate_viewport_height_is_visible()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 10,
            viewportHeight: 0,
            targetTop: 999,
            targetHeight: 20,
            scrollableHeight: 1000);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetVisible, d.Result);
        Assert.Equal(10, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_target_taller_than_viewport_is_visible()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 0,
            viewportHeight: 100,
            targetTop: 10,
            targetHeight: 150,
            scrollableHeight: 500);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetVisible, d.Result);
        Assert.Equal(0, d.NewVerticalOffset);
    }

    [Fact]
    public void Compute_below_clamps_to_scrollable_height()
    {
        var d = BrowserTreeViewportPageScrollPlan.Compute(
            viewportTop: 80,
            viewportHeight: 100,
            targetTop: 200,
            targetHeight: 20,
            scrollableHeight: 50);
        Assert.Equal(BrowserTreeViewportPageScrollResult.TargetBelowViewport, d.Result);
        Assert.Equal(50, d.NewVerticalOffset);
    }
}
