using System;
using System.Collections.Generic;

namespace SsmsHelper.Core.Completion;

/// <summary>One list entry for matching.</summary>
/// <param name="Text">Text shown and matched, e.g. "dbo.Orders", "o.OrderId", "FROM".</param>
/// <param name="Group">Order between kinds when matches are equally good: tables 0, views 1, functions 2, ...</param>
public readonly record struct CompletionCandidate(string Text, int Group);

/// <summary>A candidate that matches the typed text.</summary>
/// <param name="Index">Index into the candidate list.</param>
/// <param name="MatchStart">Start of the matched text in the candidate, for bold highlighting.</param>
/// <param name="MatchLength">Length of the matched text (0 when nothing is typed).</param>
public readonly record struct CompletionMatch(int Index, int MatchStart, int MatchLength);

/// <summary>
/// Filters and ranks list entries for the typed text: "contains", case-insensitive, best match first. Without a dot
/// the typed text is matched against the last part of the name ("gam" finds dbo.Games by "Games"); with a dot,
/// against the whole name ("dbo.Ga"). Allocation-light: runs on every keystroke over up to tens of thousands of
/// entries.
/// </summary>
public static class CompletionMatcher
{
    // Ranks, best first.
    private const int Exact = 0;
    private const int NameStartsWith = 1;
    private const int NameContains = 2;
    private const int FullContains = 3;

    /// <returns>The matching candidates, best first: exact name, name starts with the text (shorter names first),
    /// name contains it, whole text contains it; then by group, then by the original order.</returns>
    public static List<CompletionMatch> Filter(IReadOnlyList<CompletionCandidate> candidates, string typed)
    {
        var ranked = new List<(int Rank, int Group, int Length, CompletionMatch Match)>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var text = candidates[i].Text;
            if (Rank(text, typed, out var start) is { } rank)
            {
                // Shorter names first among "starts with" name matches; otherwise keep the given order.
                var length = rank == NameStartsWith && typed.Length > 0 && typed.IndexOf('.') < 0 ? text.Length - NameStart(text) : 0;
                ranked.Add((rank, candidates[i].Group, length, new CompletionMatch(i, start, typed.Length)));
            }
        }

        ranked.Sort((a, b) =>
        {
            var c = a.Rank.CompareTo(b.Rank);
            if (c == 0)
            {
                c = a.Group.CompareTo(b.Group);
            }

            if (c == 0)
            {
                c = a.Length.CompareTo(b.Length);
            }

            return c != 0 ? c : a.Match.Index.CompareTo(b.Match.Index);
        });

        var result = new List<CompletionMatch>(ranked.Count);
        foreach (var r in ranked)
        {
            result.Add(r.Match);
        }

        return result;
    }

    /// <summary>Rank of one candidate, or null when it does not match.</summary>
    public static int? Rank(string text, string typed, out int matchStart)
    {
        matchStart = 0;
        if (typed.Length == 0)
        {
            return NameStartsWith;
        }

        if (typed.IndexOf('.') >= 0)
        {
            matchStart = text.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
            return matchStart < 0 ? null : matchStart == 0 ? NameStartsWith : FullContains;
        }

        var nameStart = NameStart(text);
        var nameLength = text.Length - nameStart;
        var inName = nameLength >= typed.Length ? IndexOf(text, typed, nameStart) : -1;
        if (inName >= 0)
        {
            matchStart = inName;
            if (inName == nameStart)
            {
                return nameLength == typed.Length ? Exact : NameStartsWith;
            }

            // "[Order Lines]": a match right after the bracket is still the start of the name.
            if (inName == nameStart + 1 && text[nameStart] == '[')
            {
                return nameLength == typed.Length + 2 ? Exact : NameStartsWith;
            }

            return NameContains;
        }

        matchStart = text.IndexOf(typed, StringComparison.OrdinalIgnoreCase);
        return matchStart < 0 ? null : FullContains;
    }

    /// <summary>Index after the last dot that is not inside [brackets].</summary>
    private static int NameStart(string text)
    {
        var inBrackets = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '[')
            {
                inBrackets = true;
            }
            else if (c == ']')
            {
                inBrackets = false;
            }
            else if (c == '.' && !inBrackets)
            {
                start = i + 1;
            }
        }

        return start;
    }

    private static int IndexOf(string text, string typed, int from) =>
        text.IndexOf(typed, from, StringComparison.OrdinalIgnoreCase);
}
