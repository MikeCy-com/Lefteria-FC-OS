using System.Text.Json;
using Npgsql;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed class FixtureConfiguration
{
    public const string ExpectedDatabase = "lefteria_rewrite_identity_tests";
    public const int ExpectedPort = 54329;

    private FixtureConfiguration(string adminConnectionString, string appConnectionString,
        string adminUsername, string appUsername)
    {
        AdminConnectionString = adminConnectionString;
        AppConnectionString = appConnectionString;
        AdminUsername = adminUsername;
        AppUsername = appUsername;
    }

    public string AdminConnectionString { get; }
    public string AppConnectionString { get; }
    public string AdminUsername { get; }
    public string AppUsername { get; }

    public static async Task<FixtureConfiguration> LoadAsync(string path)
    {
        var document = JsonSerializer.Deserialize<Document>(await File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The disposable fixture document is missing.");
        if (!document.Disposable || document.Host != "127.0.0.1" || document.Port != ExpectedPort ||
            document.Database != ExpectedDatabase || document.Username != "lefteria_tests" ||
            document.Purpose != "Lefteria rewrite identity integration tests only" ||
            string.IsNullOrWhiteSpace(document.ApplicationUsername) ||
            document.ApplicationUsername == document.Username)
        {
            throw new InvalidOperationException("Refusing to use a database outside the exact disposable identity fixture.");
        }
        ValidateConnection(document.ConnectionString, document.Username);
        ValidateConnection(document.ApplicationConnectionString, document.ApplicationUsername);
        return new(document.ConnectionString!, document.ApplicationConnectionString!,
            document.Username!, document.ApplicationUsername!);
    }

    public static void ValidateConnection(string? connectionString, string expectedUsername)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Both fixture and restricted application connection strings are required.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.Host != "127.0.0.1" || settings.Port != ExpectedPort ||
            settings.Database != ExpectedDatabase || settings.Username != expectedUsername)
            throw new InvalidOperationException("Refusing a connection outside the exact disposable identity fixture.");
    }

    public async Task VerifyServerAsync()
    {
        foreach (var (connectionString, user) in new[]
                 { (AdminConnectionString, AdminUsername), (AppConnectionString, AppUsername) })
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT current_database(), host(inet_server_addr()), inet_server_port(), current_user", connection);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            if (reader.GetString(0) != ExpectedDatabase || reader.GetString(1) != "127.0.0.1" ||
                reader.GetInt32(2) != ExpectedPort || reader.GetString(3) != user)
                throw new InvalidOperationException($"The server identity does not match the disposable fixture: database={reader.GetString(0)}, address={reader.GetString(1)}, port={reader.GetInt32(2)}, user={reader.GetString(3)}.");
        }
    }

    // A class intentionally has no record-generated ToString that could print credentials.
    private sealed class Document
    {
        public Document() { }
        public bool Disposable { get; init; }
        public string? Host { get; init; }
        public int Port { get; init; }
        public string? Database { get; init; }
        public string? Username { get; init; }
        public string? ConnectionString { get; init; }
        public string? ApplicationUsername { get; init; }
        public string? ApplicationConnectionString { get; init; }
        public string? Purpose { get; init; }
    }
}
