using OpenAudioOrchestrator.Web.Services;

namespace OpenAudioOrchestrator.Tests.Services;

public class TtsClientServiceTests
{
    [Fact]
    public void BuildTtsRequestBody_IncludesRequiredFields()
    {
        var request = new TtsRequest
        {
            Text = "Hello world",
            ReferenceId = "narrator",
            Format = "wav"
        };

        var json = TtsClientService.BuildRequestJson(request);

        Assert.Contains("\"text\"", json);
        Assert.Contains("Hello world", json);
        Assert.Contains("\"reference_id\"", json);
        Assert.Contains("narrator", json);
        Assert.Contains("\"format\"", json);
        Assert.Contains("wav", json);
    }

    [Fact]
    public void BuildTtsRequestBody_OmitsReferenceIdWhenNull()
    {
        var request = new TtsRequest
        {
            Text = "No voice",
            ReferenceId = null,
            Format = "mp3"
        };

        var json = TtsClientService.BuildRequestJson(request);

        Assert.Contains("No voice", json);
        Assert.DoesNotContain("reference_id", json);
        Assert.Contains("mp3", json);
    }

    [Fact]
    public void GenerateOutputFileName_IncludesTimestamp()
    {
        var fileName = TtsClientService.GenerateOutputFileName("wav");

        Assert.StartsWith("gen_", fileName);
        Assert.EndsWith(".wav", fileName);
        Assert.True(fileName.Length > 10);
    }
}
