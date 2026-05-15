namespace ImageHoard.Core.Slideshow;

/// <summary>Defaults from docs/design-decisions/slideshow-algorithm-p0.md</summary>
public static class SlideshowAlgorithmDefaults
{
    public const int MinPoolBeforeStart = 24;

    /// <summary>Legacy name: superseded by <see cref="DiscoveredPathsInMemoryMax"/> for the full discovered-path store.</summary>
    public const int ReservoirMax = 2000;

    /// <summary>Hold this many discovered paths in RAM; additional paths spill to a temp file (uniform random still uses full count).</summary>
    public const int DiscoveredPathsInMemoryMax = 50_000;
}
