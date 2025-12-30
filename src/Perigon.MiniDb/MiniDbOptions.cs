namespace Perigon.MiniDb;

/// <summary>
/// Configuration options for MiniDb.
/// </summary>
public class MiniDbOptions
{
    /// <summary>
    /// Gets the configured file path for the database.
    /// </summary>
    public string? FilePath { get; private set; }

    /// <summary>
    /// Configures the database to use the specified file path.
    /// </summary>
    /// <param name="filePath">The path to the database file.</param>
    public void UseMiniDb(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            
        FilePath = filePath;
    }
}
