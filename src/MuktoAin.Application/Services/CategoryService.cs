using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// T-2.5 (FR-6): lets citizens browse case categories without submitting a case.
// CaseCategory is a flat lookup table (no ParentCategoryId in the SSMS schema), so
// there's no tree to walk here despite the task's "Category Hierarchy" label -- just
// CRUD passthrough over the generic repository.
// ponytail: this exists so Web controllers don't inject IRepository<CaseCategory>
// directly (Application layer boundary), not because it does more than the repo.
public class CategoryService
{
    private readonly IRepository<CaseCategory> _categoryRepo;

    public CategoryService(IRepository<CaseCategory> categoryRepo)
    {
        _categoryRepo = categoryRepo;
    }

    public async Task<IEnumerable<CaseCategory>> GetAllCategoriesAsync()
        => await _categoryRepo.GetAllAsync();

    public async Task<CaseCategory?> GetByIdAsync(int id)
        => await _categoryRepo.GetByIdAsync(id);
}
