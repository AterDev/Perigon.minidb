using System.Collections.Concurrent;

namespace Perigon.MiniDb;

/// <summary>
/// Global configuration for MiniDb.
/// </summary>
public static class MiniDbConfiguration
{
    private static readonly ConcurrentDictionary<Type, MiniDbOptions> _configurations = new();

    /// <summary>
    /// Registers and configures a DbContext type.
    /// </summary>
    /// <typeparam name="TContext">The type of the DbContext.</typeparam>
    /// <param name="configure">Action to configure the options.</param>
    public static void AddDbContext<TContext>(Action<MiniDbOptions> configure) where TContext : MiniDbContext
    {
        var options = new MiniDbOptions();
        configure(options);
        
        if (string.IsNullOrWhiteSpace(options.FilePath))
        {
            throw new InvalidOperationException($"File path not configured for {typeof(TContext).Name}. Call options.UseMiniDb(path).");
        }

        _configurations[typeof(TContext)] = options;
    }

    /// <summary>
    /// Gets the configuration options for a specific DbContext type.
    /// </summary>
    internal static MiniDbOptions GetOptions(Type contextType)
    {
        if (_configurations.TryGetValue(contextType, out var options))
        {
            return options;
        }
        
        throw new InvalidOperationException($"No configuration found for DbContext type '{contextType.Name}'. Please configure it using MiniDbConfiguration.AddDbContext<{contextType.Name}>(...).");
    }
}
