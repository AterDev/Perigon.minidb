namespace Perigon.MiniDb;

/// <summary>
/// Interface that all entities must implement.
/// Provides a strongly-typed Id property for efficient data access without reflection.
/// </summary>
public interface IMicroEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity.
    /// The ID will be automatically assigned when adding new entities if not set (Id = 0).
    /// </summary>
    int Id { get; set; }
}
