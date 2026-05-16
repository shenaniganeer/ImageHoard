namespace ImageHoard.Core.Browse2;

/// <summary>Multicast diff hub for <see cref="FsMapRegistry"/> mutations.</summary>
public sealed class FsDiffStream
{
    public event Action<FsMapDiff>? DiffReceived;

    public void Raise(FsMapDiff diff)
    {
        DiffReceived?.Invoke(diff);
    }

    public void RaiseMany(IEnumerable<FsMapDiff> diffs)
    {
        foreach (var d in diffs)
            DiffReceived?.Invoke(d);
    }
}
