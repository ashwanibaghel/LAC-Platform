using System.Text.Json;
using LAC.Api;
using Xunit;

namespace LAC.Tests;

public sealed class CourtIntelligenceArtifactTests
{
    [Fact]
    public async Task MissingArtifact_IsOptionalAndDoesNotCreateStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "court-artifact-test-" + Guid.NewGuid());
        Assert.Null(await CourtIntelligenceArtifactReader.ReadAsync(root, Guid.NewGuid(), default));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Artifact_IsReadOnly_AndCaseScoped()
    {
        var root = Path.Combine(Path.GetTempPath(), "court-artifact-test-" + Guid.NewGuid());
        var id = Guid.NewGuid();
        var folder = Path.Combine(root, "court-intelligence", "v1", id.ToString());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "current.json");
            var text = JsonSerializer.Serialize(new { version = 1, caseId = id, currentPosition = new[] { "source fact" } });
            await File.WriteAllTextAsync(path, text);
            var read = await CourtIntelligenceArtifactReader.ReadAsync(root, id, default);
            Assert.NotNull(read);
            Assert.Equal(text, await File.ReadAllTextAsync(path));
            Assert.Null(await CourtIntelligenceArtifactReader.ReadAsync(root, Guid.NewGuid(), default));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { version = 1, caseId = Guid.NewGuid() }));
            await Assert.ThrowsAsync<InvalidDataException>(() => CourtIntelligenceArtifactReader.ReadAsync(root, id, default));
        }
        finally { Directory.Delete(root, true); }
    }
}
