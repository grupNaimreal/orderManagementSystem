// Services/CustomerNameValidator.cs
// Validates customer names and table/counter labels against profanity, slurs,
// and explicit/offensive words (English & Filipino/Tagalog + leetspeak variants).

using System.Text;
using System.Text.RegularExpressions;

namespace orderManagementSystem.Services;

public static partial class CustomerNameValidator
{
    private static readonly HashSet<string> BlockedExactTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // English profanity / slurs / explicit terms
        "fuck", "fucker", "fucking", "motherfucker", "shit", "bullshit", "bitch", "bitches",
        "asshole", "bastard", "cunt", "dick", "dickhead", "cock", "pussy", "slut", "whore",
        "nigger", "nigga", "faggot", "fag", "retard", "retarded", "rapist", "rape", "porn",
        "penis", "vagina", "boobs", "tits", "cum", "jizz", "dildo", "sex", "nazi", "hitler",
        // Filipino / Tagalog profanity & slurs
        "puta", "putangina", "putanginamo", "tangina", "tanginamo", "gago", "gaga",
        "tarantado", "ulol", "bobo", "tanga", "inutil", "pakyu", "pakyo", "kantot",
        "kantutan", "tite", "titi", "puke", "pepe", "pekpek", "burat", "bayag", "jakol",
        "tamod", "kupal", "leche", "letse", "bwisit", "hinayupak", "hayopka", "pokpok",
        "malandi", "libog", "malibog", "chupa", "tsupa", "hindot", "ungas", "siraulo"
    };

    private static readonly string[] BlockedSubstrings =
    {
        "fuck", "shit", "bitch", "cunt", "nigger", "nigga", "faggot", "motherfuck",
        "putangina", "tangina", "kantot", "pakyu", "pakyo", "hindot", "siraulo"
    };

    public static (bool IsValid, string? ErrorMessage) Validate(string? value, string fieldLabel = "Name")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (true, null);
        }

        var trimmed = value.Trim();

        // Split on whitespace/punctuation to inspect individual words
        var rawTokens = TokenSplitRegex().Split(trimmed)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        foreach (var token in rawTokens)
        {
            var normalized = NormalizeLeetspeak(token);
            var collapsed = CollapseRepeats(normalized);

            if (BlockedExactTokens.Contains(normalized) || BlockedExactTokens.Contains(collapsed))
            {
                return (false, $"{fieldLabel} contains inappropriate language. Please enter a respectful name.");
            }
        }

        // Also check compacted letters across the whole string to catch spaced-out evasion (e.g., "f u c k")
        var compacted = new string(
            NormalizeLeetspeak(trimmed)
                .Where(char.IsLetter)
                .ToArray());

        var compactedCollapsed = CollapseRepeats(compacted);

        foreach (var bad in BlockedSubstrings)
        {
            if (compacted.Contains(bad, StringComparison.OrdinalIgnoreCase)
                || compactedCollapsed.Contains(bad, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"{fieldLabel} contains inappropriate language. Please enter a respectful name.");
            }
        }

        return (true, null);
    }

    private static string NormalizeLeetspeak(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input.ToLowerInvariant())
        {
            sb.Append(ch switch
            {
                '@' or '4' => 'a',
                '3' => 'e',
                '1' or '!' or '|' => 'i',
                '0' => 'o',
                '$' or '5' => 's',
                '7' => 't',
                _ => ch
            });
        }
        return sb.ToString();
    }

    private static string CollapseRepeats(string input)
    {
        if (input.Length <= 2) return input;
        var sb = new StringBuilder(input.Length);
        char prev = '\0';
        int run = 0;

        foreach (var ch in input)
        {
            if (ch == prev)
            {
                run++;
                if (run <= 1) sb.Append(ch);
            }
            else
            {
                prev = ch;
                run = 1;
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[^a-zA-Z0-9@$!|]+")]
    private static partial Regex TokenSplitRegex();
}