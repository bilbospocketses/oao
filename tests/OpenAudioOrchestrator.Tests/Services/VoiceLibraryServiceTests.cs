using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using OpenAudioOrchestrator.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace OpenAudioOrchestrator.Tests.Services;

public class VoiceLibraryServiceTests : IDisposable
{
    private readonly string _testDataRoot;
    private readonly AppDbContext _context;
    private readonly VoiceLibraryService _service;

    public VoiceLibraryServiceTests()
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), $"oao-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDataRoot, "References"));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAudioOrchestrator:DataRoot"] = _testDataRoot
            })
            .Build();

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        _service = new VoiceLibraryService(config, _context, httpFactory.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_testDataRoot))
            Directory.Delete(_testDataRoot, true);
    }

    [Fact]
    public async Task AddVoiceAsync_CreatesFilesAndDbRecord()
    {
        var audioBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 };
        using var stream = new MemoryStream(audioBytes);

        await _service.AddVoiceAsync("Narrator", "narrator", stream, "Hello world");

        var voice = await _context.ReferenceVoices.FirstAsync(v => v.VoiceId == "narrator");
        Assert.Equal("Narrator", voice.DisplayName);
        Assert.Equal("sample.wav", voice.AudioFileName);
        Assert.Equal("Hello world", voice.TranscriptText);

        var voiceDir = Path.Combine(_testDataRoot, "References", "narrator");
        Assert.True(Directory.Exists(voiceDir));
        Assert.True(File.Exists(Path.Combine(voiceDir, "sample.wav")));
        Assert.True(File.Exists(Path.Combine(voiceDir, "sample.lab")));
        Assert.Equal("Hello world", await File.ReadAllTextAsync(Path.Combine(voiceDir, "sample.lab")));
    }

    [Fact]
    public async Task DeleteVoiceAsync_RemovesFilesAndDbRecord()
    {
        var audioBytes = new byte[] { 0x52, 0x49, 0x46, 0x46 };
        using var stream = new MemoryStream(audioBytes);
        await _service.AddVoiceAsync("ToDelete", "to-delete", stream, "Delete me");

        var voice = await _context.ReferenceVoices.FirstAsync(v => v.VoiceId == "to-delete");
        await _service.DeleteVoiceAsync(voice.Id);

        Assert.False(await _context.ReferenceVoices.AnyAsync(v => v.VoiceId == "to-delete"));
        Assert.False(Directory.Exists(Path.Combine(_testDataRoot, "References", "to-delete")));
    }

    [Fact]
    public async Task ListVoicesAsync_ReturnsAllVoices()
    {
        _context.ReferenceVoices.AddRange(
            new ReferenceVoice { VoiceId = "voice-a", DisplayName = "Voice A", AudioFileName = "sample.wav", TranscriptText = "A" },
            new ReferenceVoice { VoiceId = "voice-b", DisplayName = "Voice B", AudioFileName = "sample.wav", TranscriptText = "B" });
        await _context.SaveChangesAsync();

        var voices = await _service.ListVoicesAsync();
        Assert.Equal(2, voices.Count);
    }

    [Fact]
    public async Task AddVoiceAsync_RejectsPathTraversal()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.AddVoiceAsync("test", "../escape", stream, "text"));
    }

    [Fact]
    public async Task UpdateVoiceAsync_UpdatesMetadata()
    {
        _context.ReferenceVoices.Add(new ReferenceVoice
        {
            VoiceId = "update-me", DisplayName = "Old Name",
            AudioFileName = "sample.wav", TranscriptText = "Old text", Tags = "old"
        });
        await _context.SaveChangesAsync();

        var voice = await _context.ReferenceVoices.FirstAsync(v => v.VoiceId == "update-me");
        await _service.UpdateVoiceAsync(voice.Id, "New Name", "Updated text", "new,tags");

        var updated = await _context.ReferenceVoices.FirstAsync(v => v.Id == voice.Id);
        Assert.Equal("New Name", updated.DisplayName);
        Assert.Equal("Updated text", updated.TranscriptText);
        Assert.Equal("new,tags", updated.Tags);
        Assert.NotNull(updated.UpdatedAt);
    }
}
