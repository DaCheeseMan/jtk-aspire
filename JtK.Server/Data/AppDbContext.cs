using JtK.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JtK.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Court>().HasData(
            new Court { Id = 1, Name = "Banan", Surface = "Asphalt", Description = "Utomhusbana med asfaltunderlag.", IsActive = true }
        );
    }
}
