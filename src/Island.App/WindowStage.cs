using System.Globalization;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

/// <summary>Section 3 checks: the window styles, focus, click-through and a snapshot of the test shape.</summary>
internal static class WindowStage
{
    private const long RequiredStyles = Native.WsExToolWindow | Native.WsExNoActivate | Native.WsExTopmost;

    public static void Run(SelfTestReport report, IslandHost host, IntPtr foregroundBefore)
    {
        CheckStyles(report, host);
        CheckFocus(report, host, foregroundBefore);
        CheckClickThrough(report, host);
        CheckSnapshots(report, host);

        report.Info["windowSizeDip"] = $"{host.WidthDip.ToString(CultureInfo.InvariantCulture)} x {host.HeightDip.ToString(CultureInfo.InvariantCulture)}";
        report.Info["clickCheckNote"] =
            "WindowFromPoint is used as a stand-in for where a real click lands on a layered window; whether it tells the truth is UNVERIFIED. " +
            "A real click on a browser tab right under the island is NEEDS-HUMAN-VERIFY.";
    }

    private static void CheckStyles(SelfTestReport report, IslandHost host)
    {
        var capsule = Native.GetExStyle(host.Capsule.Handle);
        var shadow = Native.GetExStyle(host.Shadow.Handle);
        report.Check("capsule window has tool-window, no-activate and topmost styles",
            (capsule & RequiredStyles) == RequiredStyles, $"extended style 0x{capsule:X8}");
        report.Check("capsule window does not ignore the mouse itself",
            (capsule & Native.WsExTransparent) == 0, "it must catch clicks on the capsule");
        report.Check("shadow window has tool-window, no-activate and topmost styles",
            (shadow & RequiredStyles) == RequiredStyles, $"extended style 0x{shadow:X8}");
        report.Check("shadow window ignores the mouse entirely",
            (shadow & Native.WsExTransparent) != 0, $"extended style 0x{shadow:X8}");
    }

    private static void CheckFocus(SelfTestReport report, IslandHost host, IntPtr before)
    {
        var after = Native.GetForegroundWindow();
        var same = before == after;
        report.Info["focusAfterShow"] = same ? "same" : "different";
        report.Check("the window that had focus before the island appeared still has it", same,
            same ? "same" : "different");
        report.Check("neither island window is the active window",
            !host.Capsule.IsActive && !host.Shadow.IsActive, "IsActive false for both");
        report.Check("neither island window is the foreground window",
            after != host.Capsule.Handle && after != host.Shadow.Handle, "foreground is not ours");
    }

    private static void CheckClickThrough(SelfTestReport report, IslandHost host)
    {
        Native.GetWindowRect(host.Capsule.Handle, out var rect);
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(host.Capsule).DpiScaleX;
        var pid = (uint)Environment.ProcessId;
        int Px(double dip) => (int)Math.Round(dip * scale);

        var centreX = rect.Left + (rect.Right - rect.Left) / 2;
        var shapeCentre = new Native.Point(centreX, rect.Top + Px(TestShape.Top + TestShape.Height / 2));
        var belowShape = new Native.Point(centreX, rect.Top + Px(TestShape.Top + TestShape.Height + 10));
        var besideShape = new Native.Point(centreX - Px(TestShape.Width / 2 + 10), rect.Top + Px(TestShape.Top + TestShape.Height / 2));
        var cornerTopLeft = new Native.Point(rect.Left + 1, rect.Top + 1);
        var cornerBottomRight = new Native.Point(rect.Right - 2, rect.Bottom - 2);

        bool Ours(Native.Point p) => Native.ProcessIdOf(Native.WindowFromPoint(p)) == pid;

        report.Check("a point at the centre of the shape belongs to this process", Ours(shapeCentre), "WindowFromPoint");
        report.Check("a point in the shadow just below the shape belongs to another window", !Ours(belowShape), "WindowFromPoint");
        report.Check("a point in the shadow just beside the shape belongs to another window", !Ours(besideShape), "WindowFromPoint");
        report.Check("the top-left corner of the window belongs to another window", !Ours(cornerTopLeft), "WindowFromPoint");
        report.Check("the bottom-right corner of the window belongs to another window", !Ours(cornerBottomRight), "WindowFromPoint");
    }

    private static void CheckSnapshots(SelfTestReport report, IslandHost host)
    {
        var scale = VisualTreeHelper.GetDpi(host.Capsule).DpiScaleX;
        var w = (int)Math.Round(host.WidthDip * scale);
        var h = (int)Math.Round(host.HeightDip * scale);
        var dpi = 96 * scale;

        var front = Snapshot.Of(host.Capsule.Root, w, h, dpi);
        var corners = new[] { (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1) };
        report.Check("shape snapshot has alpha 0 in all four corners",
            corners.All(c => front.AlphaAt(c.Item1, c.Item2) == 0), "corners of the capsule window");
        var centre = front.AlphaAt(w / 2, (int)Math.Round((TestShape.Top + TestShape.Height / 2) * scale));
        report.Check("shape snapshot has alpha above 0 at the centre of the shape", centre > 0, $"alpha {centre}");

        var back = Snapshot.Of(host.Shadow.Root, w, h, dpi);
        report.Check("shadow snapshot has alpha 0 in all four corners",
            corners.All(c => back.AlphaAt(c.Item1, c.Item2) == 0), "corners of the shadow window");
        var below = back.AlphaAt(w / 2, (int)Math.Round((TestShape.Top + TestShape.Height + 10) * scale));
        report.Check("shadow snapshot has alpha above 0 just below the shape", below > 0, $"alpha {below}");
        var inside = back.AlphaAt(w / 2, (int)Math.Round((TestShape.Top + TestShape.Height / 2) * scale));
        report.Check("shadow snapshot has alpha 0 inside the shape (shadow only outside the outline)", inside == 0, $"alpha {inside}");
    }
}
