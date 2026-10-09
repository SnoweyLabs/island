namespace Island.Core;

/// <summary>Reading a setting that is one of a few words: only the words themselves are accepted, never a number or a list.</summary>
public static class EnumNames
{
    /// <summary>True when <paramref name="text"/> is exactly the name of one value (any case); "2" and "Approved, Darker" are not.</summary>
    public static bool TryParse<T>(string? text, out T value) where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var name in Enum.GetNames<T>())
        {
            if (!string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) continue;
            value = Enum.Parse<T>(name);
            return true;
        }

        return false;
    }
}
