using OpenAudioOrchestrator.Web.Hubs;

namespace OpenAudioOrchestrator.Web.Services;

public static class GpuMetricsParser
{
    public static GpuMetricsEvent? Parse(string nvidiaSmiOutput)
    {
        if (string.IsNullOrWhiteSpace(nvidiaSmiOutput))
            return null;

        var parts = nvidiaSmiOutput.Trim().Split(',');
        if (parts.Length < 3)
            return null;

        if (!int.TryParse(parts[0].Trim(), out var memUsed) ||
            !int.TryParse(parts[1].Trim(), out var memTotal) ||
            !int.TryParse(parts[2].Trim(), out var util))
            return null;

        return new GpuMetricsEvent(memUsed, memTotal, util);
    }

    public static async Task<GpuMetricsEvent?> CollectAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                return Parse(output);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                return null;
            }
        }
        catch
        {
            return null;
        }
    }
}
