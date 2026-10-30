using System.Text.RegularExpressions;

namespace SnippingToolPlus;

internal static partial class DurationTextCorrection
{
    // I/l and 1 share a shape in many UI fonts. A preceding numeric duration and
    // the end of the line constrain the seconds component, including split OCR tokens.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])\d{1,4}[hm]\s+(?<seconds>[0-9Il](?:\s*[0-9Il])?\s*s)(?=[\s.,;:!?)\]]*$)", RegexOptions.CultureInvariant)]
    private static partial Regex AmbiguousSeconds();

    internal static TextRecognitionResult Apply(TextRecognitionResult raw)
    {
        var output = new List<RecognizedWord>();
        bool changed = false;
        foreach (var line in raw.Words.GroupBy(word => word.Line))
        {
            var words = line.ToList();
            string text = TextRecognitionResult.JoinLine(words);
            var seconds = AmbiguousSeconds().Match(text).Groups["seconds"];
            if (!seconds.Success) { output.AddRange(words); continue; }
            string digits = string.Concat(seconds.Value[..^1].Where(c => !char.IsWhiteSpace(c)))
                .Replace('I', '1').Replace('l', '1');
            string corrected = digits + "s";
            if (!int.TryParse(digits, out int number) || number >= 60 || seconds.Value == corrected)
            { output.AddRange(words); continue; }

            int cursor = 0, first = -1, last = -1, withinFirst = 0, withinLast = 0;
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) cursor += words[i].Separator.Length;
                int end = cursor + words[i].Text.Length;
                if (seconds.Index >= cursor && seconds.Index < end)
                { first = i; withinFirst = seconds.Index - cursor; }
                if (seconds.Index + seconds.Length > cursor && seconds.Index + seconds.Length <= end)
                { last = i; withinLast = seconds.Index + seconds.Length - cursor; }
                cursor = end;
            }
            if (first < 0 || last < first) { output.AddRange(words); continue; }
            string merged = words[first].Text[..withinFirst] + corrected + words[last].Text[withinLast..];
            var bounds = words.Skip(first).Take(last - first + 1).Select(word => word.Bounds).Aggregate(RectangleF.Union);
            var replacement = words[first] with { Text = merged, Bounds = bounds };
            words.RemoveRange(first, last - first + 1);
            words.Insert(first, replacement);
            output.AddRange(words); changed = true;
        }
        return changed ? (raw with { Words = output }).Format() : raw;
    }
}
