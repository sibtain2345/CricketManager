namespace CricketManager.Data.Repositories;

/// <summary>
/// Storage-agnostic repository contract. The MVP implementation (JsonRepository)
/// persists to flat JSON files. A future SqliteRepository (EF Core) can implement
/// this same interface with zero changes required in calling/game-logic code -
/// this is the seam described in Section 65 (modding/data system) and the
/// "MVP implementations must be replaceable without rewrites" rule.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(Guid id);
    Task SaveChangesAsync();
}
