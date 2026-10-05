using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Configurator.Core.Architecture;

/// <summary>Complex entities are intentionally separated from generic CRUD.</summary>
public interface IComplexEntityHandler
{
    string EntityName { get; }
    bool CanHandle(DatabaseTable table);
    Task ValidateDeleteAsync(DeletionPlan plan, CancellationToken cancellationToken = default);
    Task ValidateReinitializationAsync(IReadOnlyDictionary<string, object> rootKey, CancellationToken cancellationToken = default);
}

public interface IEntityClonePolicy
{
    string EntityName { get; }
    bool CanHandle(DatabaseTable table);
    Task<IReadOnlyDictionary<string, object>> PrepareCloneAsync(IReadOnlyDictionary<string, object> sourceKey, CancellationToken cancellationToken = default);
}
