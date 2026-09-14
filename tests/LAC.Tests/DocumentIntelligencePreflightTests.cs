using LAC.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace LAC.Tests;

public sealed class DocumentIntelligencePreflightTests
{
    [Fact]
    public void Preflight_reports_missing_python_without_touching_any_data_store()
    {
        var result = Client(new() { Enabled = true, PythonExecutable = "Z:\\missing-python.exe", WorkerScript = "worker.py" }).GetPreflight();
        Assert.Equal("Misconfigured", result.Status);
        Assert.Equal("Python runtime not found.", result.Message);
    }

    [Fact]
    public void Preflight_reports_missing_worker_and_invalid_working_directory()
    {
        var python = Environment.ProcessPath!;
        var worker = Client(new() { Enabled = true, PythonExecutable = python, WorkerScript = "Z:\\missing-worker.py" }).GetPreflight();
        Assert.Equal("Worker script not found.", worker.Message);
        var directory = Client(new() { Enabled = true, PythonExecutable = python, WorkerScript = typeof(DocumentIntelligencePreflightTests).Assembly.Location, WorkingDirectory = "Z:\\missing-workdir" }).GetPreflight();
        Assert.Equal("Worker working directory not found.", directory.Message);
    }

    [Fact]
    public void Existing_worker_layout_is_reported_ready_and_missing_input_is_explicit()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var worker = Path.Combine(directory, "worker.py"); File.WriteAllText(worker, "# fixture");
            var client = Client(new() { Enabled = true, PythonExecutable = Environment.ProcessPath!, WorkerScript = worker, WorkingDirectory = directory });
            Assert.True(client.GetPreflight().Ready);
            Assert.Equal("Source document could not be found.", client.GetPreflight(Path.Combine(directory, "missing.pdf")).Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Process_start_failure_is_a_short_actionable_error()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "not-python.exe"); File.WriteAllText(executable, "not executable");
            var worker = Path.Combine(directory, "worker.py"); File.WriteAllText(worker, "# fixture");
            var input = Path.Combine(directory, "input.pdf"); File.WriteAllText(input, "fixture");
            var client = Client(new() { Enabled = true, PythonExecutable = executable, WorkerScript = worker, WorkingDirectory = directory });
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.RunAsync(new(1, Guid.NewGuid(), input, Guid.NewGuid(), null), CancellationToken.None));
            Assert.Equal("Worker could not start.", error.Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static LocalDocumentIntelligenceClient Client(DocumentIntelligenceOptions options) => new(Options.Create(options));
}
