using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VibeHub.Core.Inject;

/// <summary>
/// Merge / strip vibe-hub managed blocks without touching user content outside the markers.
/// </summary>
public static class ManagedBlock
{
    public const string Begin = "<!-- vibe-hub:begin (managed, do not edit) -->";
    public const string End = "<!-- vibe-hub:end -->";

    private static readonly Regex BlockRx = new(
        @"<!--\s*vibe-hub:begin.*?-->.*?<!--\s*vibe-hub:end\s*-->",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool HasManagedBlock(string text) => BlockRx.IsMatch(text);

    public static string Upsert(string existing, string innerContent)
    {
        var block = Begin + "\n" + innerContent.TrimEnd() + "\n" + End;
        if (string.IsNullOrEmpty(existing))
            return block + "\n";

        if (HasManagedBlock(existing))
            return BlockRx.Replace(existing, block);

        var trimmed = existing.TrimEnd();
        return trimmed + "\n\n" + block + "\n";
    }

    public static string Remove(string existing)
    {
        if (string.IsNullOrEmpty(existing) || !HasManagedBlock(existing))
            return existing;
        var removed = BlockRx.Replace(existing, "").TrimEnd();
        return removed.Length == 0 ? "" : removed + "\n";
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
