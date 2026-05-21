using Microsoft.EntityFrameworkCore;

namespace MusicApp.Data;

public sealed class MusicStoreDbContextFactory : IDbContextFactory<MusicStoreDbContext>
{
    public MusicStoreDbContext CreateDbContext() => new();
}
