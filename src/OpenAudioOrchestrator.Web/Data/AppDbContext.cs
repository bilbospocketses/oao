using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ModelProfile> ModelProfiles => Set<ModelProfile>();
    public DbSet<ReferenceVoice> ReferenceVoices => Set<ReferenceVoice>();
    public DbSet<GenerationLog> GenerationLogs => Set<GenerationLog>();
    public DbSet<TtsJob> TtsJobs => Set<TtsJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ModelProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CheckpointPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ImageTag).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });

        modelBuilder.Entity<ReferenceVoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.VoiceId).IsUnique();
            entity.Property(e => e.VoiceId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AudioFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.TranscriptText).IsRequired();
            entity.Property(e => e.Tags).HasMaxLength(500);
        });

        modelBuilder.Entity<GenerationLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InputText).IsRequired();
            entity.Property(e => e.OutputFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Format).IsRequired().HasMaxLength(10);
            entity.HasOne(e => e.ModelProfile)
                .WithMany()
                .HasForeignKey(e => e.ModelProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ReferenceVoice)
                .WithMany()
                .HasForeignKey(e => e.ReferenceVoiceId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TtsJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InputText).IsRequired();
            entity.Property(e => e.OutputFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Format).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.HasOne(e => e.ModelProfile)
                .WithMany()
                .HasForeignKey(e => e.ModelProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ReferenceVoice)
                .WithMany()
                .HasForeignKey(e => e.ReferenceVoiceId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.Property(e => e.DisplayName).HasMaxLength(100);
        });
    }
}
