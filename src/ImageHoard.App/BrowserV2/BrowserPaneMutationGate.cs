namespace ImageHoard.App.BrowserV2;

/// <summary>
/// Replaces legacy <c>EnterBrowserPaneMutation</c> depth: wizard / bulk IO holds the gate so navigation helpers can defer work until the scope exits.
/// Map mutations remain single-writer via <see cref="ImageHoard.Core.Browse2.FsMapWorkspace"/>; UI application is serialized on the WinUI dispatcher by <see cref="TreeController"/>.
/// </summary>
internal sealed class BrowserPaneMutationGate
{
    private int _depth;

    public bool IsActive => Volatile.Read(ref _depth) > 0;

    public IDisposable Enter()
    {
        _ = Interlocked.Increment(ref _depth);
        return new Scope(this);
    }

    private sealed class Scope : IDisposable
    {
        private BrowserPaneMutationGate? _owner;

        public Scope(BrowserPaneMutationGate owner) => _owner = owner;

        public void Dispose()
        {
            var o = Interlocked.Exchange(ref _owner, null);
            if (o != null)
                _ = Interlocked.Decrement(ref o._depth);
        }
    }
}
