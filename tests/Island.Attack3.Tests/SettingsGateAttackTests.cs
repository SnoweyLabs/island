using Island.Core;

namespace Island.Attack3.Tests;

/// <summary>ATTACK3 on the glass setting and the outside gate.</summary>
public class SettingsGateAttackTests
{
    // ---- Defects ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1")] // read as Darker
    [InlineData("2")] // read as Blur
    [InlineData("Approved, Darker")] // a flags-style list, read as 0|1 = Darker
    public void Glass_Given_As_A_Number_Or_A_List_Is_Refused(string glass)
    {
        // The refusal text says "glass" must be "approved", "darker" or "blur"; Enum.TryParse also takes numbers
        // and comma lists.
        var load = Settings.Parse($$"""{ "glass": "{{glass}}" }""");

        Assert.Equal(SettingsStatus.Unreadable, load.Status);
    }

    // ---- Coverage that holds -------------------------------------------------------------------------------

    [Theory]
    [InlineData("approved", GlassKind.Approved)]
    [InlineData("DARKER", GlassKind.Darker)]
    [InlineData("Blur", GlassKind.Blur)]
    public void Every_Glass_Word_Loads_And_Round_Trips(string word, GlassKind kind)
    {
        var load = Settings.Parse($$"""{ "glass": "{{word}}" }""");

        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(kind, load.Settings.Glass);
        Assert.Equal(load.Settings, Settings.Parse(load.Settings.ToJson()).Settings);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"\"")]
    [InlineData("\"frosted\"")]
    [InlineData("\"3\"")]
    [InlineData("\"Darker, Blur\"")]
    [InlineData("\"\\uD800\"")]
    [InlineData("1e400")]
    public void A_Glass_Of_The_Wrong_Type_Or_Value_Gives_The_Defaults(string value)
    {
        var load = Settings.Parse($$"""{ "glass": {{value}}, "idleSeconds": 9 }""");

        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
        Assert.False(string.IsNullOrEmpty(load.Detail));
    }

    [Fact]
    public async Task Outside_Gate_Counts_Exactly_Under_Many_Threads()
    {
        const int Threads = 16;
        const int PerThread = 20_000;
        var gate = new OutsideGate(selfTest: true, ownProcessId: 4242);
        var kinds = Enum.GetValues<OutsideKind>();

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                _ = gate.RefusedCounts();
                _ = gate.RefusedTotal;
            }
        });
        var allowed = new int[Threads];
        Parallel.For(0, Threads, t =>
        {
            for (var i = 0; i < PerThread; i++)
            {
                var kind = kinds[(t + i) % kinds.Length];
                var pid = i % 3 == 0 ? 4242 : i % 3 == 1 ? 0 : 99;
                if (gate.Allow(kind, pid)) allowed[t]++;
            }
        });
        await reader;

        Assert.Equal(Threads * PerThread, gate.AllowedCount + gate.RefusedTotal);
        Assert.Equal(allowed.Sum(), gate.AllowedCount);
        Assert.Equal(0, gate.Refused(OutsideKind.StartOwnCopy));
        Assert.Equal(gate.RefusedTotal, gate.RefusedCounts().Values.Sum());
        foreach (var kind in new[] { OutsideKind.StartProgram, OutsideKind.OpenFolder, OutsideKind.OpenAddress, OutsideKind.MediaCommand, OutsideKind.TabCommand, OutsideKind.OpenFile })
            Assert.True(gate.Refused(kind) > 0);

        // Only a bring-forward of the gate's own process passes; an unknown owner (0) does not.
        var exact = new OutsideGate(selfTest: true, ownProcessId: 4242);
        Assert.True(exact.Allow(OutsideKind.BringForward, 4242));
        Assert.False(exact.Allow(OutsideKind.BringForward, 0));
        Assert.False(exact.Allow(OutsideKind.BringForward, -4242));
        Assert.False(exact.Allow(OutsideKind.StartProgram, 4242));
        Assert.False(exact.Allow((OutsideKind)999, 4242));
    }
}
