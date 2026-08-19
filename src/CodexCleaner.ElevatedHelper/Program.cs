using System.IO.Pipes;
using System.Text.Json;
using CodexCleaner.Core;

var options = args.Chunk(2).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1], StringComparer.OrdinalIgnoreCase);
if (!options.TryGetValue("--pipe", out var pipeName) || !options.TryGetValue("--nonce", out var nonce)) return 2;

using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
await pipe.ConnectAsync(20_000, CancellationToken.None);
var request = await JsonSerializer.DeserializeAsync<ElevatedRequest>(pipe);
if (request is null || !string.Equals(request.Nonce, nonce, StringComparison.Ordinal) || request.Items.Count == 0) return 3;

// The helper accepts only frozen Safe cache plan items. It deliberately refuses
// arbitrary paths, project artifacts, reparse points and protected locations.
foreach (var item in request.Items)
{
    try
    {
        if (item.Risk != RiskLevel.Safe || item.Disposition != CleanupDisposition.PermanentDelete) continue;
        var path = Path.GetFullPath(item.NormalizedPath);
        if (!path.Equals(item.Path, StringComparison.OrdinalIgnoreCase) && !path.Equals(Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase)) continue;
        if (RiskRules.IsProtectedPath(path) || IsReparsePoint(path)) continue;
        if (Directory.Exists(path) && !File.GetLastWriteTimeUtc(path).Equals(item.ExpectedLastWriteTime.UtcDateTime)) continue;
        if (File.Exists(path) && !File.GetLastWriteTimeUtc(path).Equals(item.ExpectedLastWriteTime.UtcDateTime)) continue;
        if (File.Exists(path)) File.Delete(path); else if (Directory.Exists(path)) Directory.Delete(path, true);
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

static bool IsReparsePoint(string path)
{
    try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
    catch (IOException) { return true; }
    catch (UnauthorizedAccessException) { return true; }
}

return 0;

internal sealed record ElevatedRequest(string Nonce, IReadOnlyList<CleanupPlanItem> Items);
