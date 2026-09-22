using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Loads the 64 Bangladesh districts from data/districts.json into the DISTRICT table.
public static class SeedDistricts
{
    private sealed record DistrictSeedDto(byte DistrictId, string Name);

    public static async Task SeedAsync(AppDbContext context, string contentRootPath)
    {
        if (await context.Districts.AsNoTracking().AnyAsync()) return; // idempotent

        var dtos = await SeedJsonLoader.LoadAsync<DistrictSeedDto>(contentRootPath, "districts.json");
        var districts = dtos.Select(d => new District { DistrictId = d.DistrictId, Name = d.Name });
        context.Districts.AddRange(districts);
        await context.SaveChangesAsync();
    }
}
