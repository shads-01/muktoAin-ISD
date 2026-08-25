using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface ICaseRepository : IRepository<Case>
{
    Task<IEnumerable<Case>> GetByUserIdAsync(int userId);
    Task<Case?> GetWithDocumentsAsync(int caseId);
}
