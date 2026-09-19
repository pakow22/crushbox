using DatingMatchBot.Models;
using Microsoft.EntityFrameworkCore;

namespace DatingMatchBot.Data;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<BotUser> Users => Set<BotUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<BotUser>();
        user.ToTable("BotUsers");
        user.HasKey(item => item.ChatId);
        user.Property(item => item.TelegramUsername).HasMaxLength(64);
        user.Property(item => item.Name).HasMaxLength(100);
        user.Property(item => item.City).HasMaxLength(100);
        user.Property(item => item.About).HasMaxLength(500);
        user.Property(item => item.PhotoFileId).HasMaxLength(512);
        user.Ignore(item => item.IsRegistered);
    }
}
