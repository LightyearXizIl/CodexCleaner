using CodexCleaner.Core;

namespace CodexCleaner.Core.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1073741824, "1.0 GB")]
    public void Formats_capacity(long bytes, string expected) => Assert.Equal(expected, ByteSizeFormatter.Format(bytes));

    [Theory]
    [InlineData("C:\\project\\node_modules", ItemCategory.Dependency, RiskLevel.Rebuildable)]
    [InlineData("C:\\project\\.next", ItemCategory.BuildArtifact, RiskLevel.Rebuildable)]
    [InlineData("C:\\project\\.env", ItemCategory.Other, RiskLevel.Protected)]
    [InlineData("C:\\project\\__pycache__", ItemCategory.Cache, RiskLevel.Safe)]
    [InlineData("C:\\project\\Program.cs", ItemCategory.Other, RiskLevel.Protected)]
    [InlineData("C:\\project\\appsettings.json", ItemCategory.Configuration, RiskLevel.Protected)]
    [InlineData("C:\\project\\data.sqlite", ItemCategory.Database, RiskLevel.Protected)]
    [InlineData("C:\\project\\report.pdf", ItemCategory.UserFiles, RiskLevel.Protected)]
    public void Classifies_known_paths(string path, ItemCategory category, RiskLevel risk)
    {
        var result = RiskRules.Classify(path);
        Assert.Equal(category, result.Category);
        Assert.Equal(risk, result.Risk);
    }

    [Fact]
    public void Attribution_requires_direct_evidence_for_confirmed()
    {
        var possible = AttributionEngine.Score([new AttributionEvidence("time", "nearby", 40)]);
        var confirmed = AttributionEngine.Score([new AttributionEvidence("command", "direct install", 100)]);
        Assert.Equal(AttributionLevel.Possible, possible.Level);
        Assert.Equal(AttributionLevel.Confirmed, confirmed.Level);
    }

    [Fact]
    public async Task Full_hash_matches_equal_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexCleanerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "a.bin"); var second = Path.Combine(root, "b.bin");
            await File.WriteAllTextAsync(first, "same content"); await File.WriteAllTextAsync(second, "same content");
            Assert.Equal(await Hashing.Sha256Async(first, CancellationToken.None), await Hashing.Sha256Async(second, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }
}
