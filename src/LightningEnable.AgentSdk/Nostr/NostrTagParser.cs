namespace LightningEnable.AgentSdk.Nostr;

/// <summary>
/// Utilities for working with Nostr event tags.
/// </summary>
public static class NostrTagParser
{
    /// <summary>
    /// Get the first value for a given tag name.
    /// </summary>
    public static string? GetTagValue(string[][] tags, string tagName)
    {
        foreach (var tag in tags)
        {
            if (tag.Length >= 2 && tag[0] == tagName)
                return tag[1];
        }
        return null;
    }

    /// <summary>
    /// Get all values for a given tag name.
    /// </summary>
    public static List<string> GetTagValues(string[][] tags, string tagName)
    {
        var values = new List<string>();
        foreach (var tag in tags)
        {
            if (tag.Length >= 2 && tag[0] == tagName)
                values.Add(tag[1]);
        }
        return values;
    }

    /// <summary>
    /// Get all tag entries for a given tag name (full arrays).
    /// </summary>
    public static List<string[]> GetTags(string[][] tags, string tagName)
    {
        return tags.Where(t => t.Length >= 1 && t[0] == tagName).ToList();
    }

    /// <summary>
    /// Check if a specific tag exists.
    /// </summary>
    public static bool HasTag(string[][] tags, string tagName)
    {
        return tags.Any(t => t.Length >= 1 && t[0] == tagName);
    }

    /// <summary>
    /// Get key-value parameters from "param" tags.
    /// Tags formatted as ["param", "key", "value"].
    /// </summary>
    public static Dictionary<string, string> GetParams(string[][] tags)
    {
        var result = new Dictionary<string, string>();
        foreach (var tag in tags)
        {
            if (tag.Length >= 3 && tag[0] == "param")
                result[tag[1]] = tag[2];
        }
        return result;
    }

    /// <summary>
    /// Build a filter object for relay subscriptions.
    /// </summary>
    public static Dictionary<string, object> BuildFilter(
        int[]? kinds = null,
        string[]? authors = null,
        Dictionary<string, string[]>? tagFilters = null,
        int? limit = null,
        long? since = null,
        long? until = null)
    {
        var filter = new Dictionary<string, object>();

        if (kinds != null)
            filter["kinds"] = kinds;

        if (authors != null)
            filter["authors"] = authors;

        if (tagFilters != null)
        {
            foreach (var kvp in tagFilters)
                filter[$"#{kvp.Key}"] = kvp.Value;
        }

        if (limit.HasValue)
            filter["limit"] = limit.Value;

        if (since.HasValue)
            filter["since"] = since.Value;

        if (until.HasValue)
            filter["until"] = until.Value;

        return filter;
    }
}
