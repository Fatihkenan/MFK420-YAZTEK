using Microsoft.EntityFrameworkCore;
using YAZTEK.API.Entities;

namespace YAZTEK.API.Persistence
{
    public class YaztekDbContext : DbContext
    {
        public YaztekDbContext(DbContextOptions<YaztekDbContext> options) : base(options)
        {
        }
        public DbSet<Student> Students { get; set; }
    }
}
