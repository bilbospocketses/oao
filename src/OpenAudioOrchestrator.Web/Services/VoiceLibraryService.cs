using OpenAudioOrchestrator.Web.Data;
using OpenAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace OpenAudioOrchestrator.Web.Services;

public class VoiceLibraryService : IVoiceLibraryService
{
    private readonly string _referencesPath;
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;

    public VoiceLibraryService(IConfiguration config, AppDbContext context, IHttpClientFactory httpClientFactory)
    {
        var dataRoot = config["OpenAudioOrchestrator:DataRoot"]!;
        _referencesPath = Path.GetFullPath(Path.Combine(dataRoot, "References"));
        _context = context;
        _httpClientFactory = httpClientFactory;
    }

    public async Task AddVoiceAsync(string displayName, string voiceId, Stream audioFile, string transcript, string? tags = null)
    {
        var voiceDir = Path.GetFullPath(Path.Combine(_referencesPath, voiceId));
        if (!voiceDir.StartsWith(_referencesPath + Path.DirectorySeparatorChar))
            throw new ArgumentException("Invalid voice ID.", nameof(voiceId));

        Directory.CreateDirectory(voiceDir);

        var wavPath = Path.Combine(voiceDir, "sample.wav");
        await using (var fs = File.Create(wavPath))
        {
            await audioFile.CopyToAsync(fs);
        }

        var labPath = Path.Combine(voiceDir, "sample.lab");
        await File.WriteAllTextAsync(labPath, transcript);

        _context.ReferenceVoices.Add(new ReferenceVoice
        {
            VoiceId = voiceId,
            DisplayName = displayName,
            AudioFileName = "sample.wav",
            TranscriptText = transcript,
            Tags = string.IsNullOrWhiteSpace(tags) ? null : tags
        });
        await _context.SaveChangesAsync();
    }

    public async Task UpdateVoiceAsync(int id, string displayName, string transcriptText, string? tags)
    {
        var voice = await _context.ReferenceVoices.FindAsync(id)
            ?? throw new InvalidOperationException($"Voice with ID {id} not found");

        voice.DisplayName = displayName;
        voice.TranscriptText = transcriptText;
        voice.Tags = tags;
        voice.UpdatedAt = DateTimeOffset.UtcNow;

        var labPath = Path.Combine(_referencesPath, voice.VoiceId, "sample.lab");
        if (File.Exists(labPath))
        {
            await File.WriteAllTextAsync(labPath, transcriptText);
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteVoiceAsync(int id)
    {
        var voice = await _context.ReferenceVoices.FindAsync(id)
            ?? throw new InvalidOperationException($"Voice with ID {id} not found");

        var voiceDir = Path.GetFullPath(Path.Combine(_referencesPath, voice.VoiceId));
        if (!voiceDir.StartsWith(_referencesPath + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Stored voice ID contains an invalid path.");

        if (Directory.Exists(voiceDir))
        {
            Directory.Delete(voiceDir, true);
        }

        _context.ReferenceVoices.Remove(voice);
        await _context.SaveChangesAsync();
    }

    public async Task<List<ReferenceVoice>> ListVoicesAsync()
    {
        return await _context.ReferenceVoices.OrderBy(v => v.DisplayName).ToListAsync();
    }

    public async Task SyncVoicesToContainerAsync(string containerBaseUrl)
    {
        var voices = await _context.ReferenceVoices.ToListAsync();
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(containerBaseUrl);

        foreach (var voice in voices)
        {
            var wavPath = Path.Combine(_referencesPath, voice.VoiceId, voice.AudioFileName);
            if (!File.Exists(wavPath)) continue;

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(voice.VoiceId), "id");
            form.Add(new StringContent(voice.TranscriptText), "text");

            var audioBytes = await File.ReadAllBytesAsync(wavPath);
            form.Add(new ByteArrayContent(audioBytes), "audio", voice.AudioFileName);

            await httpClient.PostAsync("/v1/references/add", form);
        }
    }
}
