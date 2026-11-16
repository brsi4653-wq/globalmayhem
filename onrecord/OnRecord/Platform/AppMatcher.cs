using System.Text;
using System.Text.RegularExpressions;

namespace OnRecord.Platform;

/// <summary>
/// Finds the app a LAUNCH name means in the installed apps list. Case-insensitive, closest match first:
/// exact name, then names containing it as whole words, then names it contains, then near spellings.
/// One app at the first step that finds anything = launch it; several = ask; none = say so. Never guesses between apps.
/// </summary>
public static partial class AppMatcher
{
    public const int MaxChoices = 8;

    public enum Kind { One, Several, None }

    public sealed record Result(Kind Kind, IReadOnlyList<AppEntry> Apps);

    public static Result Match(string query, IReadOnlyList<AppEntry> apps)
    {
        string q = Normalize(query);
        if (q.Length == 0 || apps.Count == 0) return new(Kind.None, []);
        var named = apps.Select(a => (App: a, N: Normalize(a.Name))).Where(x => x.N.Length > 0).ToList();

        // 1. The exact name as written, ignoring case ("notepad" = "Notepad", not "Notepad++"),
        //    then ignoring punctuation too ("spotify music" = "Spotify - Music").
        string raw = query.Trim();
        var hits = named.Where(x => string.Equals(x.App.Name, raw, StringComparison.OrdinalIgnoreCase)).Select(x => x.App).ToList();
        if (hits.Count == 0) hits = named.Where(x => x.N == q).Select(x => x.App).ToList();
        // 2. A name that contains it as whole words ("visual studio" -> "Visual Studio Code", "Visual Studio 2022").
        if (hits.Count == 0) hits = named.Where(x => ContainsWords(x.N, q)).Select(x => x.App).ToList();
        // 3. A name it contains as whole words ("spotify app" -> "Spotify").
        if (hits.Count == 0) hits = named.Where(x => x.N.Length >= 3 && ContainsWords(q, x.N)).Select(x => x.App).ToList();
        // 4. Near spellings of the whole name ("spotfy", "dicsord", "note pad").
        if (hits.Count == 0 && q.Length >= 4)
        {
            int allowed = q.Length >= 8 ? 2 : 1;
            string qs = q.Replace(" ", "");
            hits = named.Where(x => Distance(x.N, q) <= allowed || x.N.Replace(" ", "") == qs).Select(x => x.App).ToList();
        }

        hits = hits.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList();
        return hits.Count switch
        {
            0 => new(Kind.None, []),
            1 => new(Kind.One, hits),
            _ => new(Kind.Several, hits.OrderBy(a => a.Name.Length).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(MaxChoices).ToList()),
        };
    }

    /// <summary>True if the text looks like a path, URL or command rather than an app name (LAUNCH refuses those).</summary>
    public static bool LooksLikePath(string name) =>
        name.Contains('\\') || name.Contains('/') || name.Contains(':') || name.Contains('%')
        || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lower case, letters and digits only, single spaces ("Spotify™ - Music!" -> "spotify music").</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormKC))
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    private static bool ContainsWords(string haystack, string needle) =>
        (" " + haystack + " ").Contains(" " + needle + " ", StringComparison.Ordinal);

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return int.MaxValue;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>Did the user's own message ask to open something? LAUNCH lines in replies to anything else are ignored.</summary>
public static partial class LaunchIntent
{
    [GeneratedRegex(@"\b(open|opening|launch|start|run|play|load|boot|fire\s+up|bring\s+up|pull\s+up)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Verb();

    public static bool Asked(string? userMessage) => !string.IsNullOrWhiteSpace(userMessage) && Verb().IsMatch(userMessage);
}
