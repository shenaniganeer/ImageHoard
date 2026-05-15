namespace ImageHoard.Core.Input;

/// <summary>OS-style multi-click chain (same button, time between downs, distance from first down in DIPs).</summary>
public sealed class MouseButtonClickChainTracker
{
    private readonly Func<long> _clockMs;
    private string? _chainButton;
    private double _anchorXDip;
    private double _anchorYDip;
    private long _lastDownMs;
    private int _chainCount;

    public MouseButtonClickChainTracker(Func<long> clockMs) => _clockMs = clockMs;

    /// <summary>Maximum clicks in a chain (schema allows 1–3).</summary>
    public const int MaxSchemaClickCount = 3;

    /// <param name="button">Schema button name (e.g. Left).</param>
    /// <param name="xDip">Horizontal position in DIPs (stable space across the gesture).</param>
    /// <param name="yDip">Vertical position in DIPs.</param>
    /// <param name="metrics">Windows double-click time and rectangle converted to DIPs.</param>
    /// <returns>1-based click index in the current chain, capped at <see cref="MaxSchemaClickCount"/>.</returns>
    public int OnMouseButtonDown(string button, double xDip, double yDip, in MouseButtonClickMetrics metrics)
    {
        var now = _clockMs();
        if (_chainButton != button
            || now - _lastDownMs > metrics.DoubleClickTimeMs
            || Math.Abs(xDip - _anchorXDip) > metrics.MaxDistanceDipX
            || Math.Abs(yDip - _anchorYDip) > metrics.MaxDistanceDipY)
        {
            _chainButton = button;
            _anchorXDip = xDip;
            _anchorYDip = yDip;
            _lastDownMs = now;
            _chainCount = 1;
            return 1;
        }

        _lastDownMs = now;
        _chainCount = Math.Min(_chainCount + 1, MaxSchemaClickCount);
        return _chainCount;
    }

    public void Reset()
    {
        _chainButton = null;
        _chainCount = 0;
        _lastDownMs = 0;
    }
}

/// <summary>Double-click timing and hit slop in DIPs (from Win32 metrics + rasterization scale).</summary>
public readonly record struct MouseButtonClickMetrics(int DoubleClickTimeMs, double MaxDistanceDipX, double MaxDistanceDipY);
