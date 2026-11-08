using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DownloadMaster.Services.Queue;

/// <summary>
/// Expands IDM-style batch patterns:
/// <c>part_[01-20].rar</c> (zero padded numbers), <c>img_[a-f].jpg</c> (letters),
/// <c>shot_[0-100:5].png</c> (step). Several ranges multiply: <c>[1-3]_[a-b]</c> gives 6 links.
/// </summary>
public static partial class BatchPatternExpander
{
    public const int DefaultMaxResults = 10_000;

    [GeneratedRegex(@"\[(?<from>[0-9]+|[A-Za-z])-(?<to>[0-9]+|[A-Za-z])(?::(?<step>[0-9]+))?\]")]
    private static partial Regex RangeRegex();

    /// <summary>True when the text contains at least one valid <c>[from-to]</c> range.</summary>
    public static bool HasPattern(string? pattern) =>
        !string.IsNullOrWhiteSpace(pattern) && RangeRegex().IsMatch(pattern);

    /// <summary>Number of links the pattern produces (without building them). Throws <see cref="FormatException"/> on bad ranges.</summary>
    public static long Count(string pattern)
    {
        long total = 1;
        foreach (Match match in RangeRegex().Matches(pattern))
        {
            total *= ParseRange(match).Count;
            if (total > int.MaxValue)
                return int.MaxValue;
        }

        return total;
    }

    /// <summary>
    /// Expands every range. A text without ranges returns itself.
    /// Throws <see cref="FormatException"/> for mixed/invalid ranges and <see cref="InvalidOperationException"/>
    /// when the result would exceed <paramref name="maxResults"/>.
    /// </summary>
    public static IReadOnlyList<string> Expand(string pattern, int maxResults = DefaultMaxResults)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        pattern = pattern.Trim();

        var matches = RangeRegex().Matches(pattern);
        if (matches.Count == 0)
            return pattern.Length == 0 ? [] : [pattern];

        var ranges = matches.Select(ParseRange).ToList();
        long total = 1;
        foreach (var range in ranges)
        {
            total *= range.Count;
            if (total > maxResults)
                throw new InvalidOperationException($"The pattern expands to more than {maxResults} links.");
        }

        // Literal pieces between the ranges.
        var literals = new List<string>(matches.Count + 1);
        var last = 0;
        foreach (Match match in matches)
        {
            literals.Add(pattern[last..match.Index]);
            last = match.Index + match.Length;
        }

        literals.Add(pattern[last..]);

        var results = new List<string>((int)total);
        var indices = new int[ranges.Count];
        var builder = new StringBuilder();
        while (true)
        {
            builder.Clear();
            for (var i = 0; i < ranges.Count; i++)
            {
                builder.Append(literals[i]);
                builder.Append(ranges[i].ValueAt(indices[i]));
            }

            builder.Append(literals[^1]);
            results.Add(builder.ToString());

            // Odometer: the last range changes fastest.
            var position = ranges.Count - 1;
            while (position >= 0)
            {
                indices[position]++;
                if (indices[position] < ranges[position].Count)
                    break;
                indices[position] = 0;
                position--;
            }

            if (position < 0)
                break;
        }

        return results;
    }

    private static Range ParseRange(Match match)
    {
        var from = match.Groups["from"].Value;
        var to = match.Groups["to"].Value;
        var step = 1;
        if (match.Groups["step"].Success)
        {
            if (!int.TryParse(match.Groups["step"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out step) || step <= 0)
                throw new FormatException($"Invalid step in {match.Value}.");
        }

        var fromIsDigit = char.IsDigit(from[0]);
        var toIsDigit = char.IsDigit(to[0]);
        if (fromIsDigit != toIsDigit)
            throw new FormatException($"Range {match.Value} mixes numbers and letters.");

        if (fromIsDigit)
        {
            if (!long.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                || !long.TryParse(to, NumberStyles.None, CultureInfo.InvariantCulture, out var end))
            {
                throw new FormatException($"Range {match.Value} is too large.");
            }

            // [01-20] / [001-100] keep the zero padding; [1-10] does not pad.
            var padded = (from.Length > 1 && from[0] == '0') || (to.Length > 1 && to[0] == '0');
            var width = padded ? Math.Max(from.Length, to.Length) : 0;
            return new Range(start, end, step, width, IsLetter: false, UpperCase: false);
        }

        if (char.IsUpper(from[0]) != char.IsUpper(to[0]))
            throw new FormatException($"Range {match.Value} mixes upper and lower case letters.");

        return new Range(from[0], to[0], step, 0, IsLetter: true, UpperCase: char.IsUpper(from[0]));
    }

    private sealed record Range(long Start, long End, int Step, int Width, bool IsLetter, bool UpperCase)
    {
        private int Direction => End >= Start ? 1 : -1;

        public int Count => (int)Math.Min(int.MaxValue, Math.Abs(End - Start) / Step + 1);

        public string ValueAt(int index)
        {
            var value = Start + (long)index * Step * Direction;
            if (IsLetter)
                return ((char)value).ToString();

            var text = value.ToString(CultureInfo.InvariantCulture);
            return Width > 0 ? text.PadLeft(Width, '0') : text;
        }
    }
}
