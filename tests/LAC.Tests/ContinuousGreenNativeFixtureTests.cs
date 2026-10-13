namespace LAC.Tests;

using LAC.Infrastructure;
using Xunit;

// Explicit opt-in native fixtures. No database, identity changes or mocked native success.
public sealed class ContinuousGreenNativeFixtureTests
{
    private sealed class NativeFixtureFactAttribute : FactAttribute
    {
        public NativeFixtureFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LAC_ENVELOPE_NATIVE_ROOT") is null) Skip = "Requires explicit private native artifact root; not a mocked native test.";
        }
    }
    private static string? Root()
    {
        var value = Environment.GetEnvironmentVariable("LAC_ENVELOPE_NATIVE_ROOT");
        if (value is null) return null;
        var path = Path.GetFullPath(value);
        var allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "artifacts") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Native fixtures must use a private task artifact directory.");
        Directory.CreateDirectory(path); return path;
    }
    [NativeFixtureFact]
    public void Generate_explicitly_requested_native_security_and_print_fixtures()
    {
        var root = Root(); if (root is null) return;
        var m = ContinuousGreenEnvelopeTests.Manifest();
        var json = MatterNotingTextTests.Text("Synthetic native current note. ");
        using var doc = ContinuousGreenEnvelope.Compose(m, json);
        File.WriteAllBytes(Path.Combine(root, "native-envelope-seed.docx"), doc.ToArray());
        File.WriteAllText(Path.Combine(root, "native-envelope-manifest.json"), ContinuousGreenEnvelope.Serialize(m));
        // Mixed marks, digits and long repeated text exercise multiple odd/even pages.
        const string paragraph = "Tj fj 0123456789 Synthetic officer continuation. Reference 17/2026. Same repeated evidence; only synthetic office content. ";
        var longText = string.Join("\n", Enumerable.Range(1, 55).Select(i => $"{i}. {paragraph}{paragraph}"));
        var longJson = System.Text.Json.JsonSerializer.Serialize(new { type = "doc", content = longText.Split('\n').Select(t => new {
            type = "paragraph", content = new[] { new { type = "text", text = t, marks = new[] { new { type = "italic" } } } } }) });
        var printableNotes = m.Notes.Select(n => {
            var content = n.ContentJson.Replace("😀", "reference 19/2026", StringComparison.Ordinal);
            var text = NotingText.Canonicalize(content); return n with { ContentJson = content, CanonicalText = text, TextHash = NotingText.Hash(text) };
        }).ToArray();
        using var multi = ContinuousGreenEnvelope.Compose(m with { Notes = printableNotes }, longJson);
        File.WriteAllBytes(Path.Combine(root, "native-envelope-multipage.docx"), multi.ToArray());
        File.WriteAllText(Path.Combine(root, "native-envelope-print-expected.txt"), "");
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(multi, false);
        File.WriteAllText(Path.Combine(root, "native-envelope-print-expected.txt"), package.MainDocumentPart!.Document!.Body!.InnerText);
    }
    [NativeFixtureFact]
    public void Validate_actual_native_saved_bytes_when_explicitly_supplied()
    {
        var root = Root(); if (root is null) return;
        var path = Path.Combine(root, "native-envelope-saved.docx"); Assert.True(File.Exists(path), "Actual native saved bytes must exist; no simulated success.");
        var json = File.ReadAllText(Path.Combine(root, "native-envelope-manifest.json"));
        var manifest = ContinuousGreenEnvelope.Read(json, NotingText.Hash(json));
        using var bytes = File.OpenRead(path); var result = ContinuousGreenEnvelope.Convert(bytes, manifest);
        Assert.DoesNotContain("Historical", result.CanonicalText);
        File.WriteAllText(Path.Combine(root, "native-envelope-canonical.json"), System.Text.Json.JsonSerializer.Serialize(result));
        var marker = Environment.GetEnvironmentVariable("LAC_ENVELOPE_EXPECTED_MARKER");
        if (marker is not null) Assert.Contains(marker, result.CanonicalText);
    }
}
