using OpenAudioOrchestrator.Web.Hubs;
using OpenAudioOrchestrator.Web.Services;

namespace OpenAudioOrchestrator.Tests.SignalR;

public class GpuMetricsParserTests
{
    [Fact]
    public void Parse_ValidOutput_ReturnsMetrics()
    {
        var output = "2048, 12288, 35";
        var result = GpuMetricsParser.Parse(output);

        Assert.NotNull(result);
        Assert.Equal(2048, result!.MemoryUsedMb);
        Assert.Equal(12288, result.MemoryTotalMb);
        Assert.Equal(35, result.UtilizationPercent);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNull()
    {
        var result = GpuMetricsParser.Parse("");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_MalformedOutput_ReturnsNull()
    {
        var result = GpuMetricsParser.Parse("not a number, also not");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_TwoFieldsOnly_ReturnsNull()
    {
        var result = GpuMetricsParser.Parse("2048, 12288");
        Assert.Null(result);
    }

    [Fact]
    public void GpuMetricsState_UpdateAndNotify()
    {
        var state = new GpuMetricsState();
        var notified = false;
        state.OnChange += () => notified = true;

        state.Update(new GpuMetricsEvent(4096, 12288, 50));

        Assert.True(notified);
        Assert.Equal(4096, state.MemoryUsedMb);
        Assert.Equal(12288, state.MemoryTotalMb);
        Assert.Equal(50, state.UtilizationPercent);
    }
}
