using ImageHoard.Core.Input;

namespace ImageHoard.Tests;

public sealed class InputBindingActivationTests
{
    [Fact]
    public void InferMaskForCommand_slideshow_start_is_browse()
    {
        Assert.Equal(InputBindingActivationMask.Browse, InputBindingActivation.InferMaskForCommand("slideshow.start"));
    }

    [Fact]
    public void InferMaskForCommand_other_slideshow_ids_are_slideshow()
    {
        Assert.Equal(InputBindingActivationMask.Slideshow, InputBindingActivation.InferMaskForCommand("slideshow.nextTreeImage"));
        Assert.Equal(InputBindingActivationMask.Slideshow, InputBindingActivation.InferMaskForCommand("slideshow.switchToBrowseAtCurrentLocation"));
    }

    [Fact]
    public void InferMaskForCommand_nav_is_browse()
    {
        Assert.Equal(InputBindingActivationMask.Browse, InputBindingActivation.InferMaskForCommand("nav.nextImage"));
    }

    [Fact]
    public void InferMaskForCommand_view_is_global()
    {
        Assert.Equal(InputBindingActivationMask.Global, InputBindingActivation.InferMaskForCommand("view.zoomIn"));
    }
}
