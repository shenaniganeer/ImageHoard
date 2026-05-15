using System.Text;

namespace ImageHoard.Core.Slideshow;

/// <summary>
/// Append-only store for discovered slideshow paths: in-memory list up to a cap, then length-prefixed UTF-8 records in a temp file with O(1) offset lookup.
/// All methods must be called under the same external lock as <see cref="TreeSlideshowSession"/>.
/// </summary>
internal sealed class SlideshowDiscoveredPathStore
{
    private readonly int _maxRamPaths;
    private readonly List<string> _ram = new();
    private readonly List<long> _spillOffsets = new();
    private string? _spillFilePath;
    private FileStream? _spillStream;
    private BinaryWriter? _spillWriter;

    public SlideshowDiscoveredPathStore(int maxRamPaths) => _maxRamPaths = maxRamPaths;

    public int Count => _ram.Count + _spillOffsets.Count;

    public void Append(string path)
    {
        if (_ram.Count < _maxRamPaths)
        {
            _ram.Add(path);
            return;
        }

        EnsureSpillWriter();
        if (_spillStream == null || _spillWriter == null)
            throw new InvalidOperationException("Spill writer failed to initialize.");
        var start = _spillStream.Position;
        var utf8 = Encoding.UTF8.GetBytes(path);
        _spillWriter.Write(utf8.Length);
        _spillWriter.Write(utf8);
        _spillWriter.Flush();
        _spillOffsets.Add(start);
    }

    public string GetAt(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index < _ram.Count)
            return _ram[index];

        var spillIdx = index - _ram.Count;
        var offset = _spillOffsets[spillIdx];
        _spillWriter?.Flush();
        using var fs = new FileStream(_spillFilePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(offset, SeekOrigin.Begin);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: false);
        var len = br.ReadInt32();
        if (len < 0 || len > 1024 * 1024)
            throw new InvalidDataException("Corrupt slideshow spill record.");
        var bytes = br.ReadBytes(len);
        if (bytes.Length != len)
            throw new EndOfStreamException("Truncated slideshow spill record.");
        return Encoding.UTF8.GetString(bytes);
    }

    public void Clear()
    {
        _ram.Clear();
        _spillOffsets.Clear();
        try
        {
            _spillWriter?.Dispose();
        }
        catch
        {
            // ignored
        }

        _spillWriter = null;
        try
        {
            _spillStream?.Dispose();
        }
        catch
        {
            // ignored
        }

        _spillStream = null;
        if (!string.IsNullOrEmpty(_spillFilePath))
        {
            try
            {
                if (File.Exists(_spillFilePath))
                    File.Delete(_spillFilePath);
            }
            catch
            {
                // ignored
            }

            _spillFilePath = null;
        }
    }

    private void EnsureSpillWriter()
    {
        if (_spillStream != null)
            return;

        _spillFilePath = Path.Combine(
            Path.GetTempPath(),
            "ImageHoard_slideshow_" + Guid.NewGuid().ToString("N") + ".paths");
        _spillStream = new FileStream(
            _spillFilePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);
        _spillWriter = new BinaryWriter(_spillStream, Encoding.UTF8, leaveOpen: true);
    }
}
