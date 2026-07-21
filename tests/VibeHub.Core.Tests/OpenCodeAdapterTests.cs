using VibeHub.Core.Adapters;
using Microsoft.Data.Sqlite;

namespace VibeHub.Core.Tests;

public sealed class OpenCodeAdapterTests
{
    [Theory]
    [InlineData(null, new[] { "run", "--format", "json", "inspect pending" })]
    [InlineData("Sisyphus - ultraworker", new[] { "run", "--format", "json", "--agent", "Sisyphus - ultraworker", "inspect pending" })]
    public void BuildTask_UsesHeadlessRunContract(string? agent, string[] expectedArguments)
    {
        var cli = Path.Combine(Path.GetTempPath(), "opencode-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllText(cli, "");
        try
        {
            var adapter = new OpenCodeAdapter { CliPathOverride = cli };

            var spec = adapter.BuildTask(@"D:\work", "inspect pending", agent);

            Assert.Equal(cli, spec.FileName);
            Assert.Equal(expectedArguments, spec.Arguments);
            Assert.Equal("true", spec.Environment!["OPENCODE_DISABLE_AUTOUPDATE"]);
        }
        finally
        {
            File.Delete(cli);
        }
    }

    [Fact]
    public void GetTaskDatabaseProblem_ReportsLegacyContextEpochSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibe-hub-opencode-db-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var cli = Path.Combine(root, "opencode.exe");
        File.WriteAllText(cli, "");
        var adapter = new OpenCodeAdapter { CliPathOverride = cli, DataRootOverride = root };
        Directory.CreateDirectory(adapter.DataRoot);
        using (var connection = new SqliteConnection(
                   new SqliteConnectionStringBuilder { DataSource = adapter.DbPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE session_context_epoch (session_id TEXT PRIMARY KEY, baseline TEXT, snapshot TEXT, baseline_seq INTEGER)";
            command.ExecuteNonQuery();
        }

        try
        {
            var problem = adapter.GetTaskDatabaseProblem();

            Assert.Contains("replacement_seq", problem);
            Assert.Contains("revision", problem);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildTask_AddsModelOverride()
    {
        var cli = Path.Combine(Path.GetTempPath(), "opencode-" + Guid.NewGuid().ToString("n") + ".exe");
        File.WriteAllText(cli, "");
        try
        {
            var adapter = new OpenCodeAdapter { CliPathOverride = cli };

            var spec = adapter.BuildTask(@"D:\work", "smoke", "Sisyphus - ultraworker", "openai/gpt-5.1-codex-max");

            Assert.Equal(
                ["run", "--format", "json", "--model", "openai/gpt-5.1-codex-max", "--agent", "Sisyphus - ultraworker", "smoke"],
                spec.Arguments);
        }
        finally
        {
            File.Delete(cli);
        }
    }
}
