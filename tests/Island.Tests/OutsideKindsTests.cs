using Island.Core;

namespace Island.Tests;

/// <summary>NEXT-PLAN.md, "WHO BUILDS WHAT": the kinds of outside action that work orders 6 and 7 will ask for exist and are refused by default.</summary>
public class OutsideKindsTests
{
    private static readonly OutsideKind[] New =
        [OutsideKind.WriteStartupValue, OutsideKind.PlaySound, OutsideKind.EditAgentSettings, OutsideKind.CloseWindow];

    [Fact]
    public void New_Kinds_Are_Refused_Under_The_Self_Test_And_Counted()
    {
        var gate = new OutsideGate(selfTest: true, ownProcessId: 100);
        foreach (var kind in New)
        {
            Assert.False(gate.Allow(kind));
            Assert.False(gate.Allow(kind, 200)); // not even a window of another process
            Assert.Equal(2, gate.Refused(kind));
        }

        Assert.Equal(8, gate.RefusedTotal);
    }

    [Fact]
    public void Only_A_Window_Of_The_Self_Tests_Own_Process_May_Be_Closed_Under_The_Self_Test()
    {
        // WORK-ORDER-6 section 5: the one change to the table above. Every other new kind stays refused whatever it is aimed at.
        var gate = new OutsideGate(selfTest: true, ownProcessId: 100);
        Assert.True(gate.Allow(OutsideKind.CloseWindow, 100));
        Assert.False(gate.Allow(OutsideKind.CloseWindow, 200));
        Assert.False(gate.Allow(OutsideKind.CloseWindow));
        Assert.False(gate.Allow(OutsideKind.WriteStartupValue, 100));
        Assert.False(gate.Allow(OutsideKind.PlaySound, 100));
        Assert.False(gate.Allow(OutsideKind.EditAgentSettings, 100));
        Assert.Equal(2, gate.Refused(OutsideKind.CloseWindow));
    }

    [Fact]
    public void New_Kinds_Are_Allowed_In_A_Normal_Run()
    {
        var gate = new OutsideGate(selfTest: false);
        Assert.All(New, kind => Assert.True(gate.Allow(kind)));
        Assert.Equal(0, gate.RefusedTotal);
    }
}
