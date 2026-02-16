using Perigon.MiniDb.Client.Models;

namespace Perigon.MiniDb.Client.Services;

public enum ConnectionOpenErrorKind
{
    None = 0,
    FileNotFound = 1,
    InvalidDatabaseFile = 2,
    UnsupportedVersion = 3,
    Unknown = 4
}

public sealed class ConnectionSessionService
{
    private readonly MiniDbFileDriver _driver = new();
    private MiniDbDriverSession? _session;

    public ConnectionOpenResult OpenConnection(DatabaseConnection connection)
    {
        var dbPath = Path.GetFullPath(connection.Path);
        if (!File.Exists(dbPath))
        {
            return ConnectionOpenResult.FileNotFound(dbPath);
        }

        try
        {
            _session = _driver.Open(dbPath);
            var tableNames = _session.GetTableNames();
            return ConnectionOpenResult.Success(dbPath, tableNames);
        }
        catch (InvalidDataException ex)
        {
            _session = null;
            if (ex.Message.Contains("Unsupported MiniDb version", StringComparison.OrdinalIgnoreCase))
            {
                return ConnectionOpenResult.UnsupportedVersion(ex.Message);
            }

            return ConnectionOpenResult.InvalidDatabaseFile(ex.Message);
        }
        catch (Exception ex)
        {
            _session = null;
            return ConnectionOpenResult.Failed(ex.Message);
        }
    }

    public string? CloseConnection()
    {
        _session = null;
        return null;
    }

    public IReadOnlyList<Dictionary<string, object?>> ReadTableRows(string tableName)
    {
        if (_session is null)
        {
            return [];
        }

        return _session.ReadTableRows(tableName);
    }

    public ConnectionDiagnosticsInfo GetDiagnostics(string? tableName = null)
    {
        if (_session is null)
        {
            return ConnectionDiagnosticsInfo.Empty;
        }

        var resolvedTable = string.IsNullOrWhiteSpace(tableName)
            ? null
            : tableName;

        var hasSchema = resolvedTable is not null && _session.HasSchemaForTable(resolvedTable);
        var schemaFieldCount = resolvedTable is not null
            ? _session.GetSchemaFieldCount(resolvedTable)
            : 0;

        return new ConnectionDiagnosticsInfo(
            true,
            _session.FilePath,
            _session.FileVersion,
            _session.TableMetadata.Count,
            _session.TableSchemas.Count,
            resolvedTable,
            hasSchema,
            schemaFieldCount);
    }
}

public sealed record ConnectionDiagnosticsInfo(
    bool IsConnected,
    string? FilePath,
    short FileVersion,
    int TableCount,
    int SchemaTableCount,
    string? SelectedTable,
    bool HasSchemaForSelectedTable,
    int SelectedTableSchemaFieldCount)
{
    public static ConnectionDiagnosticsInfo Empty { get; } = new(
        false,
        null,
        0,
        0,
        0,
        null,
        false,
        0);
}

public sealed record ConnectionOpenResult(
    bool IsSuccess,
    bool IsFileNotFound,
    ConnectionOpenErrorKind ErrorKind,
    string? DatabasePath,
    string? ErrorMessage,
    IReadOnlyList<string> TableNames)
{
    public static ConnectionOpenResult Success(string databasePath, IReadOnlyList<string> tableNames)
        => new(true, false, ConnectionOpenErrorKind.None, databasePath, null, tableNames);

    public static ConnectionOpenResult FileNotFound(string databasePath)
        => new(false, true, ConnectionOpenErrorKind.FileNotFound, databasePath, null, []);

    public static ConnectionOpenResult InvalidDatabaseFile(string errorMessage)
        => new(false, false, ConnectionOpenErrorKind.InvalidDatabaseFile, null, errorMessage, []);

    public static ConnectionOpenResult UnsupportedVersion(string errorMessage)
        => new(false, false, ConnectionOpenErrorKind.UnsupportedVersion, null, errorMessage, []);

    public static ConnectionOpenResult Failed(string errorMessage)
        => new(false, false, ConnectionOpenErrorKind.Unknown, null, errorMessage, []);
}
