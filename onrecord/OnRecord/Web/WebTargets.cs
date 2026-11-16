namespace OnRecord.Web;

/// <summary>What a WEB line may open: only http/https pages. Everything else becomes a Google search for that text.</summary>
public static class WebTargets
{
    public const int MaxLength = 4096;
    public const string SearchPrefix = "https://www.google.com/search?q=";

    /// <summary>
    /// A real http/https address (absolute, with a host, no spaces) opens as is; anything else, including file:,
    /// javascript:, data: and custom protocols, is searched for instead. Never returns a non-web URL.
    /// </summary>
    public static string ToUrl(string? target)
    {
        string t = new string((target ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim().Trim('<', '>', '`', '"', '\'').Trim();
        if (t.Length > MaxLength) t = t[..MaxLength];
        if (t.Length == 0) return "https://www.google.com/";
        if (!t.Any(char.IsWhiteSpace) && IsWebUrl(t, out var u)) return u!.AbsoluteUri;
        return SearchPrefix + Uri.EscapeDataString(t);
    }

    /// <summary>Absolute http or https URL with a host.</summary>
    public static bool IsWebUrl(string? s, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || string.IsNullOrEmpty(u.Host)) return false;
        uri = u;
        return true;
    }

    public static bool IsWebUrl(string? s) => IsWebUrl(s, out _);

    /// <summary>Short label for the chip/header before the page has a title: the host, or the search text.</summary>
    public static string Label(string url)
    {
        if (url.StartsWith(SearchPrefix, StringComparison.Ordinal))
            return "Search: " + Uri.UnescapeDataString(url[SearchPrefix.Length..]);
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
    }
}
