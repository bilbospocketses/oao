using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Tests.Data;

public class AppDbContextTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CanInsertAndRetrieveModelProfile()
    {
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var profile = new ModelProfile
        {
            Name = "s2-pro",
            CheckpointPath = @"D:\DockerData\OpenAudio\Checkpoints\s2-pro",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            EnableHalf = true,
            Status = ModelStatus.Created
        };

        context.ModelProfiles.Add(profile);
        await context.SaveChangesAsync();

        var retrieved = await context.ModelProfiles.FirstAsync(m => m.Name == "s2-pro");
        Assert.Equal("s2-pro", retrieved.Name);
        Assert.Equal(9001, retrieved.HostPort);
        Assert.True(retrieved.EnableHalf);
        Assert.Equal(ModelStatus.Created, retrieved.Status);
    }

    [Fact]
    public async Task NameMustBeUnique()
    {
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        context.ModelProfiles.Add(new ModelProfile
        {
            Name = "duplicate",
            CheckpointPath = @"D:\path1",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9001,
            Status = ModelStatus.Created
        });
        await context.SaveChangesAsync();

        context.ModelProfiles.Add(new ModelProfile
        {
            Name = "duplicate",
            CheckpointPath = @"D:\path2",
            ImageTag = "fishaudio/fish-speech:server-cuda",
            HostPort = 9002,
            Status = ModelStatus.Created
        });

        var entityType = context.Model.FindEntityType(typeof(ModelProfile))!;
        var nameIndex = entityType.GetIndexes().FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == "Name"));
        Assert.NotNull(nameIndex);
        Assert.True(nameIndex.IsUnique);
    }

    [Fact]
    public async Task AppUser_ThemePreference_DefaultsToDark()
    {
        await using var context = CreateInMemoryContext();
        await context.Database.EnsureCreatedAsync();

        var user = new AppUser
        {
            UserName = "themetest",
            DisplayName = "Theme Test"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var loaded = await context.Users.OfType<AppUser>().FirstAsync(u => u.UserName == "themetest");
        Assert.Equal("dark", loaded.ThemePreference);
    }
}
