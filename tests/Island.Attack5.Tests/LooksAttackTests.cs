using Island.Core;

namespace Island.Attack5.Tests;

/// <summary>WORK-ORDER-5 sections 2, 3 and 7: GreyIcons, WindowDots, PlayingTile and Equalizer. (IconFit and its two tests are gone: replaced by the round icons of WORK-ORDER-10 section 1.)</summary>
public class LooksAttackTests
{
    // ---- GreyIcons ----

    [Fact]
    public void Holds_GreyIcons_Empty_One_Pixel_And_Alpha_Kept()
    {
        var empty = GreyIcons.Make(new IconImage(0, 0, []));
        Assert.Empty(empty.Bgra);

        var one = new IconImage(1, 1, [255, 0, 0, 128]); // blue, half transparent
        var grey = GreyIcons.Make(one);
        Assert.Equal(128, grey.Bgra[3]);
        Assert.True(grey.Bgra[0] == grey.Bgra[1] && grey.Bgra[1] == grey.Bgra[2]);
        Assert.Equal([255, 0, 0, 128], one.Bgra); // the original is untouched
        Assert.NotSame(one.Bgra, grey.Bgra);
        Assert.Equal((1, 1), (grey.Width, grey.Height));

        // every alpha, a spread of colours: no colour left, nothing brighter than the brightness cap allows, alpha identical
        var pixels = new List<byte>();
        for (var a = 0; a < 256; a++)
            foreach (var (b, g, r) in new[] { (0, 0, 0), (255, 255, 255), (255, 0, 0), (0, 255, 0), (0, 0, 255), (17, 99, 201), (254, 1, 77) })
                pixels.AddRange([(byte)b, (byte)g, (byte)r, (byte)a]);
        var big = GreyIcons.Make(new IconImage(7, 256, [.. pixels]));
        for (var i = 0; i < big.Bgra.Length; i += 4)
        {
            Assert.True(big.Bgra[i] == big.Bgra[i + 1] && big.Bgra[i + 1] == big.Bgra[i + 2], $"pixel {i / 4} has colour");
            Assert.Equal(pixels[i + 3], big.Bgra[i + 3]);
            Assert.True(big.Bgra[i] <= 242, $"pixel {i / 4} is brighter than 95% of white"); // brightness 95 % of 255
        }

        Assert.Equal(0, GreyIcons.Make(new IconImage(1, 1, [0, 0, 0, 255])).Bgra[0]);
        Assert.Equal(242, GreyIcons.Make(new IconImage(1, 1, [255, 255, 255, 255])).Bgra[0]);
    }

    [Fact]
    public void Holds_GreyIcons_Mismatched_Lengths_And_Huge_Sizes_Do_Not_Throw_Or_Allocate_By_Size()
    {
        // The grey copy is made from the bytes, never from Width * Height: a lying size cannot overflow or allocate.
        var short7 = GreyIcons.Make(new IconImage(2, 1, [1, 2, 3, 4, 5, 6, 7])); // one whole pixel and a stub
        Assert.Equal(7, short7.Bgra.Length);
        Assert.True(short7.Bgra[0] == short7.Bgra[1] && short7.Bgra[1] == short7.Bgra[2]);
        Assert.Equal(4, short7.Bgra[3]);

        var long12 = GreyIcons.Make(new IconImage(1, 1, [9, 9, 9, 9, 8, 8, 8, 8, 1, 2, 3, 4]));
        Assert.Equal(12, long12.Bgra.Length);

        var lying = GreyIcons.Make(new IconImage(65536, 65536, [1, 2, 3, 4])); // Width * Height * 4 overflows an int
        Assert.Equal(4, lying.Bgra.Length);
        var negative = GreyIcons.Make(new IconImage(-5, int.MinValue, []));
        Assert.Empty(negative.Bgra);
    }

    [Fact]
    public async Task Holds_GreyIcons_Of_Gives_One_Copy_Per_Icon_Even_From_Many_Threads()
    {
        var icon = new IconImage(2, 2, [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 15))]);
        var copies = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => GreyIcons.Of(icon))));
        Assert.All(copies, c => Assert.Same(copies[0], c));
        Assert.Same(copies[0], GreyIcons.Of(icon));
        Assert.NotSame(icon, copies[0]);

        var other = new IconImage(2, 2, [.. icon.Bgra]); // equal pixels, a different icon: its own copy
        Assert.NotSame(copies[0], GreyIcons.Of(other));
    }

    // ---- WindowDots ----

    [Fact]
    public void Holds_WindowDots_Over_The_Whole_Int_Range()
    {
        foreach (var n in new[] { int.MinValue, -5, -1, 0, 1 }) Assert.Equal(0, WindowDots.For(n));
        for (var n = 2; n <= 5; n++) Assert.Equal(n, WindowDots.For(n));
        foreach (var n in new[] { 6, 7, 100, int.MaxValue }) Assert.Equal(5, WindowDots.For(n));
    }

    // ---- PlayingTile.PickIdFor ----

    private static readonly Pick Alpha = Pick.ForProgram("Alpha", PageIds.Media, "alpha.exe", null);
    private static readonly Pick Beta = Pick.ForSite("Beta", "example.org", PageIds.Media);

    [Fact]
    public void Holds_PickIdFor_Matches_Only_The_Picked_Site_Not_Look_Alikes()
    {
        IReadOnlyList<Pick> picks = [Alpha, Beta];
        foreach (var host in new[] { "example.org", "www.example.org", "WWW.Example.ORG", "example.org." })
            Assert.Equal(Beta.Id, PlayingTile.PickIdFor(Make.Tab(host), picks));
        foreach (var host in new[] { "notexample.org", "example.org.evil.net", "sub.example.org", "example.com", "", " ", "example.orgg" })
            Assert.Null(PlayingTile.PickIdFor(Make.Tab(host), picks));
        Assert.Null(PlayingTile.PickIdFor(Make.Tab("example.org") with { Host = null }, picks)); // a tab with no host
    }

    [Fact]
    public void Holds_PickIdFor_Null_Empty_And_Browser_Inputs()
    {
        IReadOnlyList<Pick> picks = [Alpha, Beta];
        Assert.Null(PlayingTile.PickIdFor(null, picks));
        Assert.Null(PlayingTile.PickIdFor(Make.Session("alpha.exe"), []));
        Assert.Null(PlayingTile.PickIdFor(Make.Tab("example.org"), []));
        Assert.Null(PlayingTile.PickIdFor(Make.Session(""), picks));
        Assert.Null(PlayingTile.PickIdFor(Make.Session("alpha.exe") with { SourceApp = null }, picks));
        Assert.Null(PlayingTile.PickIdFor(Make.Session("alpha.exe") with { IsBrowserSession = true }, picks)); // the whole browser belongs to no pick
        Assert.Null(PlayingTile.PickIdFor(Make.Tab("example.org") with { IsBrowserSession = true }, picks));

        // a site pick never claims a desktop player, and a program pick never claims a tab
        Assert.Null(PlayingTile.PickIdFor(Make.Session("example.org"), [Beta]));
        Assert.Null(PlayingTile.PickIdFor(Make.Tab("alpha.exe"), [Alpha]));

        Assert.Equal(Alpha.Id, PlayingTile.PickIdFor(Make.Session("ALPHA.EXE"), picks));
        Assert.Equal(Alpha.Id, PlayingTile.PickIdFor(Make.Session("Alpha"), picks));
        Assert.Equal(Alpha.Id, PlayingTile.PickIdFor(Make.Session("alpha"), [Beta, Alpha]));
    }

    [Fact]
    public void Holds_SecondLine_Says_Tab_Or_App_And_Paused()
    {
        Assert.Equal("Beta · tab", PlayingTile.SecondLine(Make.Tab("example.org")));
        Assert.Equal("Alpha · app", PlayingTile.SecondLine(Make.Session("alpha.exe")));
        Assert.Equal("Beta · tab · paused", PlayingTile.SecondLine(Make.Tab("example.org", paused: true)));
        Assert.Equal("Alpha · app · paused", PlayingTile.SecondLine(Make.Session("alpha.exe", paused: true)));
        Assert.DoesNotContain("NOW PLAYING", PlayingTile.SecondLine(Make.Session("alpha.exe")), StringComparison.OrdinalIgnoreCase);
    }

    // ---- Equalizer ----

    [Fact]
    public void Holds_Equalizer_Heights_Stay_In_Range_For_Every_Kind_Of_Time()
    {
        double[] times =
        [
            0, -0.0, 0.2, 0.45, 0.9, 1.35, 1.8, 0.4500000000000001, 1e-300, double.Epsilon, -1e-300, -1, -1e300, 1e15, 1e17, 1e300,
            double.MaxValue, double.MinValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.9 * 1e6, 0.45 + 0.9 * 123456,
        ];
        foreach (var t in times)
        {
            var playing = Equalizer.Heights(t, playing: true);
            Assert.Equal(3, playing.Length);
            Assert.All(playing, h => Assert.True(double.IsFinite(h) && h >= Equalizer.Lowest && h <= Equalizer.Highest, $"t={t}: {h}"));
            Assert.Equal([5.0, 5.0, 5.0], Equalizer.Heights(t, playing: false)); // paused: standing still, whatever the time
        }

        var rng = new Random(9);
        for (var i = 0; i < 100_000; i++)
        {
            var t = (rng.NextDouble() - 0.1) * Math.Pow(10, rng.Next(-3, 12));
            Assert.All(Equalizer.Heights(t, true), h => Assert.InRange(h, Equalizer.Lowest, Equalizer.Highest));
        }
    }

    [Fact]
    public void Holds_Equalizer_Is_Periodic_Continuous_And_Staggered()
    {
        for (var t = 0.0; t < 10; t += 0.0137)
        {
            var now = Equalizer.Heights(t, true);
            var later = Equalizer.Heights(t + Equalizer.Seconds, true);
            for (var bar = 0; bar < 3; bar++)
                if (t - Equalizer.Delays[bar] > 0) Assert.Equal(now[bar], later[bar], 6); // 0.9 s later the same height
            if (t > 0.2) Assert.Equal(Equalizer.Heights(t - 0.2, true)[0], now[1], 9); // the second bar is the first, 0.2 s behind
            if (t > 0.45) Assert.Equal(Equalizer.Heights(t - 0.45, true)[0], now[2], 9);
        }

        // No jump anywhere: a millisecond moves a bar by well under one unit, including the start and the wrap.
        var previous = Equalizer.Heights(-1, true);
        for (var t = -1.0; t < 6; t += 0.001)
        {
            var h = Equalizer.Heights(t, true);
            for (var bar = 0; bar < 3; bar++) Assert.True(Math.Abs(h[bar] - previous[bar]) < 0.1, $"bar {bar} jumped at t={t}");
            previous = h;
        }

        // It reaches both ends and a bar that has not started yet waits at the bottom.
        Assert.Equal(5, Equalizer.Heights(0, true)[2]);
        Assert.Equal(5, Equalizer.Heights(0.4, true)[2]);
        Assert.Equal(17, Equalizer.Heights(0.45, true)[0], 9); // half a period: the top
        Assert.Equal(5, Equalizer.Heights(0.9, true)[0], 9);
    }

    // ---- what broke ----

    [Fact]
    public void Defect_A_Package_Family_That_Starts_With_An_Underscore_Claims_Every_Playing_App()
    {
        // The Store match takes the part of the family name before the first underscore and tests StartsWith. For "_abc1234"
        // that part is empty, and every app id starts with an empty string: this pick dances for whatever plays.
        var odd = Pick.ForProgram("Alpha", PageIds.Media, null, "_abc1234");
        Assert.True(odd.IsStorable(out _)); // it can be stored, so it can happen
        Assert.Null(PlayingTile.PickIdFor(Make.Session("other.exe"), [odd]));
    }

    [Fact]
    public void Defect_The_Package_Prefix_Match_Has_No_Word_Boundary()
    {
        // "Alpha_pub12345" claims "AlphaBeta_pub999!App": the match is a bare StartsWith on the package name, with no check that
        // the name ends there (a "_" or "!" or "." follows). Two Store apps whose names begin alike take each other's tile.
        var alpha = Pick.ForProgram("Alpha", PageIds.Media, null, "Alpha_pub12345");
        Assert.Null(PlayingTile.PickIdFor(Make.Session("AlphaBeta_pub999!App"), [alpha]));
        Assert.Equal(alpha.Id, PlayingTile.PickIdFor(Make.Session("Alpha_pub12345!App"), [alpha])); // and the real one still matches
    }
}
