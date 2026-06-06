using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

public interface ICurrentUserService
{
    Task<User> GetOrCreateAsync(CancellationToken ct = default);
}
