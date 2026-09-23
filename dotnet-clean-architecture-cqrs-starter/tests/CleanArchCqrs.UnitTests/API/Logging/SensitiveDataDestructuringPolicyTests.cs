using CleanArchCqrs.API.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Logging;

sealed class CollectingSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();
    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}

public class SensitiveDataDestructuringPolicyTests
{
    private static string Render(object value)
    {
        var sink = new CollectingSink();
        using (var logger = new LoggerConfiguration()
                   .Destructure.With<SensitiveDataDestructuringPolicy>()
                   .WriteTo.Sink(sink)
                   .CreateLogger())
        {
            logger.Information("Value {@Value}", value);
        }
        return sink.Events.Single().Properties["Value"].ToString();
    }

    [Fact]
    public void MasksPasswordAndTokens_KeepsOtherFields()
    {
        var rendered = Render(new { Email = "a@b.vn", Password = "secret-1", RefreshToken = "rt-value", AccessToken = "at-value" });

        Assert.Contains("a@b.vn", rendered);
        Assert.DoesNotContain("secret-1", rendered);
        Assert.DoesNotContain("rt-value", rendered);
        Assert.DoesNotContain("at-value", rendered);
        Assert.Contains("***", rendered);
    }

    [Fact]
    public void MatchesPropertyNamesCaseInsensitively()
    {
        var rendered = Render(new { newPassword = "n-secret", currentPassword = "c-secret" });

        Assert.DoesNotContain("n-secret", rendered);
        Assert.DoesNotContain("c-secret", rendered);
    }

    [Fact]
    public void ObjectsWithoutSensitiveFields_UseDefaultDestructuring()
    {
        var rendered = Render(new { PageNumber = 2, SearchTerm = "abc" });

        Assert.Contains("2", rendered);
        Assert.Contains("abc", rendered);
        Assert.DoesNotContain("***", rendered);
    }
}
