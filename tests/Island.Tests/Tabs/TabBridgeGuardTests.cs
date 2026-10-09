using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>The listener for the browser add-on can only ever be reached from this computer (WORK-ORDER-4 section 2).</summary>
public class TabBridgeGuardTests
{
    private static readonly string[] Forbidden =
    [
        "IPAddress.Any", "IPv6Any", "0.0.0.0", "IPAddress.Parse", "IPAddress.IPv6Loopback", "Dns.", "DnsEndPoint",
        "\"localhost\"", "TcpListener.Create", "HttpListener", ".Bind(", "DualMode", "ExclusiveAddressUse = false",
    ];

    [Fact]
    public void Listener_Binds_Loopback_Only()
    {
        var dir = RepoPaths.File("src", "Island.Bridge");
        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .ToDictionary(f => Path.GetFileName(f), f => OutsideGuardTests.StripComments(File.ReadAllText(f)));
        Assert.Contains("TabBridge.cs", files.Keys);

        var hits = from f in files from word in Forbidden where f.Value.Contains(word, StringComparison.Ordinal) select $"{f.Key}: {word}";
        Assert.Empty(hits);

        // Every listener is made on the loopback address, and there is exactly one such place.
        var listeners = files.SelectMany(f => Regex.Matches(f.Value, @"new\s+TcpListener\s*\(([^,]*),").Select(m => m.Groups[1].Value.Trim())).ToList();
        Assert.Equal(["IPAddress.Loopback"], listeners);

        // No raw socket is bound by hand anywhere else.
        Assert.DoesNotContain(files.Values, code => Regex.IsMatch(code, @"new\s+Socket\s*\("));
    }
}
