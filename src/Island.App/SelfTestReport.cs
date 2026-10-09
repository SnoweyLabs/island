using System.IO;
using System.Text.Json;

namespace Island.App;

/// <summary>
/// What the self-test found, written to selftest.json. Holds no window title, no process
/// list and no account name; folders under the profile are never written out.
/// </summary>
internal sealed class SelfTestReport(DateTimeOffset startedAt)
{
    public sealed record CheckResult(string Name, bool Passed, string Detail);

    public DateTimeOffset StartedAt { get; } = startedAt;
    public List<CheckResult> Checks { get; } = [];
    public SortedDictionary<string, object?> Info { get; } = [];
    public string Verify { get; set; } = "RED — self-test did not finish";
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Things only a person can judge. They never fail the run; they are carried into the close-out.</summary>
    public List<string> NeedsHumanVerify { get; } = [];

    public bool AllPassed => Checks.Count > 0 && Checks.All(c => c.Passed);

    public bool Check(string name, bool passed, string detail = "")
    {
        Checks.Add(new CheckResult(name, passed, detail));
        return passed;
    }

    public void Finish() => FinishedAt = DateTimeOffset.Now;

    public void Save(string path)
    {
        var body = new
        {
            startedAt = StartedAt.ToString("o"),
            finishedAt = FinishedAt?.ToString("o"),
            verify = Verify,
            passed = AllPassed,
            needsHumanVerify = NeedsHumanVerify,
            checks = Checks,
            info = Info,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true }));
    }
}
