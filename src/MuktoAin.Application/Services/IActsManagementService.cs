using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// T-3.1 / FR-17 + FR-18: admin CRUD over the legislative corpus plus the
// SHA-256 content-hash integrity re-index that drives EmbeddingBatchJob's
// incremental re-embedding. Erin's E-3.2 controller consumes this interface,
// never the repositories directly (Application-layer boundary).
public interface IActsManagementService
{
    Task<ActPageDto> GetActsAsync(string? keyword, int page, int pageSize);
    Task<ActDetailDto?> GetActAsync(int actId);
    Task<ActSaveResultDto> CreateActAsync(ActSaveDto dto);
    Task<ActSaveResultDto> UpdateActAsync(int actId, ActSaveDto dto);
    Task<ActDeleteResultDto> DeleteActAsync(int actId);
    Task<ActReindexResultDto?> ReindexActAsync(int actId);
}
