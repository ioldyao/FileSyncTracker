using Microsoft.EntityFrameworkCore;

namespace FileSyncTracker.Core.Database;

public class SyncStateDbContext : DbContext
{
    public DbSet<FileNode> FileNodes => Set<FileNode>();
    public DbSet<SyncJournal> SyncJournals => Set<SyncJournal>();

    private readonly string _dbPath;

    public SyncStateDbContext()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "FileSyncTracker");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "sync_state.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileNode>(entity =>
        {
            entity.HasKey(e => new { e.TaskId, e.RelativePath });
            entity.Property(e => e.RelativePath).HasMaxLength(2048);
            entity.Property(e => e.LocalContentHash).HasMaxLength(64);
            entity.Property(e => e.RemoteETag).HasMaxLength(256);
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => new { e.TaskId, e.SyncStatus });
        });

        modelBuilder.Entity<SyncJournal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePath).HasMaxLength(2048);
            entity.Property(e => e.LocalHash).HasMaxLength(64);
            entity.Property(e => e.RemoteHash).HasMaxLength(64);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1024);
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => e.Status);
        });
    }
}
