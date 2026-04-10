using Microsoft.EntityFrameworkCore;
using VideoCV.Core.Entities;

namespace VideoCV.Core.Data;

public class VideoCvDbContext : DbContext
{
    public VideoCvDbContext(DbContextOptions<VideoCvDbContext> options) : base(options) { }

    public DbSet<Video> Videos => Set<Video>();
    public DbSet<VideoShare> VideoShares => Set<VideoShare>();
    public DbSet<VideoTemplate> VideoTemplates => Set<VideoTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Video>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasIndex(v => v.ShortId).IsUnique();
            e.HasIndex(v => v.Email);
            e.Property(v => v.Name).HasMaxLength(200).IsRequired();
            e.Property(v => v.ShortId).HasMaxLength(20).IsRequired();
            e.Property(v => v.Email).HasMaxLength(300);
            e.Property(v => v.Title).HasMaxLength(300);
            e.Property(v => v.TemplateName).HasMaxLength(100);
            e.Property(v => v.CvFileName).HasMaxLength(500);
            e.Property(v => v.LinkedInUrl).HasMaxLength(500);
            e.Property(v => v.AudioPath).HasMaxLength(500);
            e.Property(v => v.VideoPath).HasMaxLength(500);
            e.Property(v => v.ThumbnailPath).HasMaxLength(500);
        });

        modelBuilder.Entity<VideoShare>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.VideoId, s.SharedWithEmail });
            e.Property(s => s.SharedWithEmail).HasMaxLength(300).IsRequired();
            e.Property(s => s.SharedWithName).HasMaxLength(200);
            e.HasOne(s => s.Video).WithMany(v => v.Shares).HasForeignKey(s => s.VideoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VideoTemplate>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Name).HasMaxLength(100).IsRequired();
            e.Property(t => t.DisplayName).HasMaxLength(200).IsRequired();

            e.HasData(
                new VideoTemplate { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "professional-dark", DisplayName = "Professional Dark", Description = "Sleek dark theme with blue accents", BackgroundColor = "#0f172a", AccentColor = "#3b82f6", SortOrder = 1 },
                new VideoTemplate { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "modern-light", DisplayName = "Modern Light", Description = "Clean white theme with gradient accents", BackgroundColor = "#ffffff", AccentColor = "#6366f1", SortOrder = 2 },
                new VideoTemplate { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "creative-gradient", DisplayName = "Creative Gradient", Description = "Bold gradient background with modern typography", BackgroundColor = "#1e1b4b", AccentColor = "#a78bfa", SortOrder = 3 }
            );
        });
    }
}
