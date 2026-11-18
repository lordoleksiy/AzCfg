namespace AzCfg.Ui.Features.Platform;

/// <summary>Case-insensitive fuzzy matching for the palette: substring and in-order subsequence matches.</summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Score of <paramref name="query"/> against <paramref name="text"/>, higher is better; <c>null</c> when the query
    /// characters do not occur in order. An empty query matches everything with score 0. Exact and prefix matches rank above
    /// word-start substring matches, above other substrings, above scattered subsequences.
    /// </summary>
    public static int? Score(string query, string text)
    {
        if (query.Length == 0)
        {
            return 0;
        }

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return 10_000 + (index == 0 ? 2_000 : 0) + (text.Length == query.Length ? 2_000 : 0) + (IsWordStart(text, index) ? 1_000 : 0)
                - index - (text.Length - query.Length);
        }

        return SubsequenceScore(query, text);
    }

    /// <summary>Items that match, best first; equal scores keep their original order.</summary>
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> items, string query, Func<T, string> text) =>
        [.. items
            .Select((item, position) => (item, position, score: Score(query, text(item))))
            .Where(x => x.score is not null)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.position)
            .Select(x => x.item)];

    private static int? SubsequenceScore(string query, string text)
    {
        var score = 0;
        var position = 0;
        var previous = -2;
        foreach (var c in query)
        {
            var found = text.IndexOf(c.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return null;
            }

            score += found == previous + 1 ? 15 : 5;
            score += IsWordStart(text, found) ? 10 : 0;
            score -= found - position;
            previous = found;
            position = found + 1;
        }

        return score;
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0 || !char.IsLetterOrDigit(text[index - 1]) || (char.IsLower(text[index - 1]) && char.IsUpper(text[index]));
}
