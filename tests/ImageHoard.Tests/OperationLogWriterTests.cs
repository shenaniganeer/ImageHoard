using ImageHoard.Core.Logging;

namespace ImageHoard.Tests;

public sealed class OperationLogWriterTests
{
    [Fact]
    public async Task AppendAsync_writes_one_json_line()
    {
        var path = Path.Combine(Path.GetTempPath(), "ih_oplog_" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var rec = new OperationLogBatchRecord
            {
                Operation = "BatchDelete",
                Summary = new OperationLogSummary { Ok = 1, Failed = 0, Skipped = 0 },
                Entries =
                {
                    new OperationLogEntry { Path = "C:\\a.jpg", Result = "Ok" },
                },
            };

            await OperationLogWriter.AppendAsync(path, rec);

            var lines = await File.ReadAllLinesAsync(path);
            Assert.Single(lines);
            Assert.Contains("BatchDelete", lines[0], StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
