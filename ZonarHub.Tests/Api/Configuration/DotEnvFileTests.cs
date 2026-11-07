using ZonarHub.ApiService.Configuration;

namespace ZonarHub.Tests.Api.Configuration;

public sealed class DotEnvFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zonarhub-dotenv-tests", Guid.NewGuid().ToString("N"));

    public DotEnvFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Parse_ReadsKeyValuePairs_SkippingCommentsBlankAndMalformedLines()
    {
        var entries = DotEnvFile.Parse(
        [
            "# comment",
            "",
            "   ",
            "Supabase__Key=sb_secret_value",
            "  Cors__AllowedOrigins__0 = https://app.example.com  ",
            "export Email__Smtp__Host=smtp.example.com",
            "DOUBLE=\"quoted value\"",
            "SINGLE='single # not a comment'",
            "EQUALS=a=b=c",
            "no-separator-line",
            "=missing-key",
            "1STARTS_WITH_DIGIT=x",
            "HAS SPACE=x",
            "   # indented comment",
        ]);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Supabase__Key"] = "sb_secret_value",
                ["Cors__AllowedOrigins__0"] = "https://app.example.com",
                ["Email__Smtp__Host"] = "smtp.example.com",
                ["DOUBLE"] = "quoted value",
                ["SINGLE"] = "single # not a comment",
                ["EQUALS"] = "a=b=c",
            },
            entries);
    }

    [Fact]
    public void Parse_KeepsUnbalancedQuotesAndEmptyValues_AndTheLastDuplicateWins()
    {
        var entries = DotEnvFile.Parse(
        [
            "OPEN=\"unbalanced",
            "MIXED=\"value'",
            "EMPTY=",
            "EMPTY_QUOTED=\"\"",
            "LONE_QUOTE=\"",
            "DUP=first",
            "DUP=second",
        ]);

        Assert.Equal("\"unbalanced", entries["OPEN"]);
        Assert.Equal("\"value'", entries["MIXED"]);
        Assert.Equal(string.Empty, entries["EMPTY"]);
        Assert.Equal(string.Empty, entries["EMPTY_QUOTED"]);
        Assert.Equal("\"", entries["LONE_QUOTE"]);
        Assert.Equal("second", entries["DUP"]);
    }

    [Fact]
    public void Load_SetsOnlyVariablesThatAreNotAlreadySetAndHaveAValue()
    {
        var path = WriteEnvFile(_root, "NEW_KEY=new", "EXISTING=from-file", "EMPTY=", "# comment");
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["EXISTING"] = "from-process" };

        var result = DotEnvFile.Load(path, environment.GetValueOrDefault, (key, value) => environment[key] = value);

        Assert.Equal(path, result.Path);
        Assert.Equal(1, result.LoadedCount);
        Assert.Equal("new", environment["NEW_KEY"]);
        Assert.Equal("from-process", environment["EXISTING"]);
        Assert.False(environment.ContainsKey("EMPTY"));
    }

    [Fact]
    public void Load_MissingFile_IsANoOp()
    {
        var environment = new Dictionary<string, string?>();

        var result = DotEnvFile.Load(Path.Combine(_root, ".env"), environment.GetValueOrDefault, (key, value) => environment[key] = value);

        Assert.Same(DotEnvLoadResult.NotLoaded, result);
        Assert.Null(result.Path);
        Assert.Equal(0, result.LoadedCount);
        Assert.Empty(environment);
    }

    [Fact]
    public void Load_WithDefaultEnvironment_SetsProcessVariableWithoutOverridingExistingOnes()
    {
        var newKey = $"ZONARHUB_DOTENV_TEST_{Guid.NewGuid():N}";
        var existingKey = $"ZONARHUB_DOTENV_TEST_{Guid.NewGuid():N}";
        var path = WriteEnvFile(_root, $"{newKey}=from-file", $"{existingKey}=from-file");
        Environment.SetEnvironmentVariable(existingKey, "from-process");
        try
        {
            var result = DotEnvFile.Load(path);

            Assert.Equal(1, result.LoadedCount);
            Assert.Equal("from-file", Environment.GetEnvironmentVariable(newKey));
            Assert.Equal("from-process", Environment.GetEnvironmentVariable(existingKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(newKey, null);
            Environment.SetEnvironmentVariable(existingKey, null);
        }
    }

    [Fact]
    public void FindRepositoryFile_WalksUpToTheFolderContainingTheSolution()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "ZonarHub.ApiService", "bin", "Debug")).FullName;
        File.WriteAllText(Path.Combine(_root, DotEnvFile.RepositoryMarker), string.Empty);

        var path = DotEnvFile.FindRepositoryFile(nested);

        Assert.Equal(Path.Combine(_root, DotEnvFile.FileName), path);
    }

    [Fact]
    public void FindRepositoryFile_TriesEachStartDirectoryInOrder_AndReturnsNullWithoutASolution()
    {
        var withoutSolution = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        var repository = Directory.CreateDirectory(Path.Combine(_root, "repository")).FullName;
        File.WriteAllText(Path.Combine(repository, DotEnvFile.RepositoryMarker), string.Empty);

        Assert.Equal(Path.Combine(repository, DotEnvFile.FileName), DotEnvFile.FindRepositoryFile(withoutSolution, repository));
        Assert.Null(DotEnvFile.FindRepositoryFile(withoutSolution));
        Assert.Null(DotEnvFile.FindRepositoryFile(string.Empty));
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("development", null, true)]
    [InlineData(null, "Development", true)]
    [InlineData("Production", "Development", false)]
    [InlineData("Staging", null, false)]
    [InlineData(null, null, false)]
    public void LoadForDevelopment_LoadsOnlyWhenTheEnvironmentIsDevelopment(string? aspNetCoreEnvironment, string? dotNetEnvironment, bool expectLoaded)
    {
        File.WriteAllText(Path.Combine(_root, DotEnvFile.RepositoryMarker), string.Empty);
        WriteEnvFile(_root, "Supabase__Key=sb_secret_local");
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ASPNETCORE_ENVIRONMENT"] = aspNetCoreEnvironment,
            ["DOTNET_ENVIRONMENT"] = dotNetEnvironment,
        };

        var result = DotEnvFile.LoadForDevelopment([_root], environment.GetValueOrDefault, (key, value) => environment[key] = value);

        Assert.Equal(expectLoaded ? 1 : 0, result.LoadedCount);
        Assert.Equal(expectLoaded ? Path.Combine(_root, DotEnvFile.FileName) : null, result.Path);
        Assert.Equal(expectLoaded ? "sb_secret_local" : null, environment.GetValueOrDefault("Supabase__Key"));
    }

    [Fact]
    public void LoadForDevelopment_WithProcessEnvironment_LoadsTheRepositoryFileInDevelopment()
    {
        var key = $"ZONARHUB_DOTENV_TEST_{Guid.NewGuid():N}";
        File.WriteAllText(Path.Combine(_root, DotEnvFile.RepositoryMarker), string.Empty);
        WriteEnvFile(_root, $"{key}=from-file");
        var previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        try
        {
            var result = DotEnvFile.LoadForDevelopment([_root]);

            Assert.Equal(1, result.LoadedCount);
            Assert.Equal("from-file", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void LoadForDevelopment_WithoutAnEnvFile_IsANoOp()
    {
        File.WriteAllText(Path.Combine(_root, DotEnvFile.RepositoryMarker), string.Empty);
        var environment = new Dictionary<string, string?> { ["ASPNETCORE_ENVIRONMENT"] = "Development" };

        var result = DotEnvFile.LoadForDevelopment([_root], environment.GetValueOrDefault, (key, value) => environment[key] = value);

        Assert.Same(DotEnvLoadResult.NotLoaded, result);
        Assert.Single(environment);
    }

    private static string WriteEnvFile(string directory, params string[] lines)
    {
        var path = Path.Combine(directory, DotEnvFile.FileName);
        File.WriteAllLines(path, lines);
        return path;
    }
}
