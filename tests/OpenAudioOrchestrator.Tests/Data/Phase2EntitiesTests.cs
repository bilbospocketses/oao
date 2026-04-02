using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Tests.Data;

public class Phase2EntitiesTests
{
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task CanInsertAndRetrieveReferenceVoice()
    {
        await using var context = CreateInMemoryContext();
        var voice = new ReferenceVoice
        {
            VoiceId = "narrator", DisplayName = "Narrator Voice",
            AudioFileName = "sample.wav", TranscriptText = "Hello, this is a test."
        };
        context.ReferenceVoices.Add(voice);
        await context.SaveChangesAsync();
        var retrieved = await context.ReferenceVoices.FirstAsync(v => v.VoiceId == "narrator");
        Assert.Equal("Narrator Voice", retrieved.DisplayName);
        Assert.Equal("sample.wav", retrieved.AudioFileName);
    }

    [Fact]
    public async Task VoiceIdMustBeUnique()
    {
        await using var context = CreateInMemoryContext();
        var entityType = context.Model.FindEntityType(typeof(ReferenceVoice))!;
        var voiceIdIndex = entityType.GetIndexes().FirstOrDefault(i =>
            i.Properties.Any(p => p.Name == "VoiceId"));
        Assert.NotNull(voiceIdIndex);
        Assert.True(voiceIdIndex.IsUnique);
    }

    [Fact]
    public async Task CanInsertGenerationLogWithRelationships()
    {
        await using var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "test-model", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001, Status = ModelStatus.Created
        };
        var voice = new ReferenceVoice
        {
            VoiceId = "test-voice", DisplayName = "Test",
            AudioFileName = "test.wav", TranscriptText = "Test transcript"
        };
        context.ModelProfiles.Add(model);
        context.ReferenceVoices.Add(voice);
        await context.SaveChangesAsync();
        var log = new GenerationLog
        {
            ModelProfileId = model.Id, ReferenceVoiceId = voice.Id,
            InputText = "Hello world", OutputFileName = "gen_001.wav",
            Format = "wav", DurationMs = 1500
        };
        context.GenerationLogs.Add(log);
        await context.SaveChangesAsync();
        var retrieved = await context.GenerationLogs.Include(g => g.ModelProfile).Include(g => g.ReferenceVoice).FirstAsync();
        Assert.Equal("test-model", retrieved.ModelProfile.Name);
        Assert.NotNull(retrieved.ReferenceVoice);
        Assert.Equal("test-voice", retrieved.ReferenceVoice!.VoiceId);
    }

    [Fact]
    public async Task GenerationLog_ReferenceVoiceIsOptional()
    {
        await using var context = CreateInMemoryContext();
        var model = new ModelProfile
        {
            Name = "model-no-voice", CheckpointPath = @"D:\path",
            ImageTag = "fishaudio/fish-speech:server-cuda", HostPort = 9001, Status = ModelStatus.Created
        };
        context.ModelProfiles.Add(model);
        await context.SaveChangesAsync();
        var log = new GenerationLog
        {
            ModelProfileId = model.Id, ReferenceVoiceId = null,
            InputText = "No voice used", OutputFileName = "gen_002.wav",
            Format = "wav", DurationMs = 800
        };
        context.GenerationLogs.Add(log);
        await context.SaveChangesAsync();
        var retrieved = await context.GenerationLogs.FirstAsync();
        Assert.Null(retrieved.ReferenceVoiceId);
    }
}
