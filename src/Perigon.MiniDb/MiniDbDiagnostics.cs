namespace Perigon.MiniDb;

/// <summary>
/// Lightweight diagnostics for MiniDb runtime behavior.
/// Disabled by default; enable with env MINIDB_TRACE=1 or by setting <see cref="Enabled"/>.
/// </summary>
public static class MiniDbDiagnostics
{
    private static volatile bool _enabled =
        string.Equals(Environment.GetEnvironmentVariable("MINIDB_TRACE"), "1", StringComparison.Ordinal);

    /// <summary>
    /// Enable/disable MiniDb diagnostics globally.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Optional custom sink. If null, logs go to Console.Error.
    /// </summary>
    public static Action<string>? Sink { get; set; }

    internal static void Info(string message) => Write("INFO", message);
    internal static void Warn(string message) => Write("WARN", message);
    internal static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        var line = $"[MiniDb][{DateTime.Now:HH:mm:ss.fff}][{level}] {message}";
        try
        {
            if (Sink is not null)
            {
                Sink(line);
            }
            else
            {
                Console.Error.WriteLine(line);
            }
        }
        catch
        {
            // Diagnostics must never affect database behavior.
        }
    }
}
