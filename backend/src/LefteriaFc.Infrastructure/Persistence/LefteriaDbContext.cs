using Microsoft.EntityFrameworkCore;

namespace LefteriaFc.Infrastructure.Persistence;

public sealed class LefteriaDbContext(DbContextOptions<LefteriaDbContext> options)
    : DbContext(options)
{
}
