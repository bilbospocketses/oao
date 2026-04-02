using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenAudioOrchestrator.Web.Services;

public class TtsClientService : ITtsClientService
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public TtsClientService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> GetHealthAsync(string baseUrl)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/v1/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildRequestJson(TtsRequest request)
    {
        var body = new Dictionary<string, object?>
        {
            ["text"] = request.Text,
            ["format"] = request.Format,
            ["streaming"] = false
        };

        if (request.ReferenceId is not null)
        {
            body["reference_id"] = request.ReferenceId;
        }

        return JsonSerializer.Serialize(body, s_jsonOptions);
    }

    public static string GenerateOutputFileName(string format)
    {
        return $"gen_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.{format}";
    }
}
