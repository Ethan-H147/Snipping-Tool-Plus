using System.Text.RegularExpressions;

namespace SnippingToolPlus;

internal static partial class DurationTextCorrection
{
    // I/l and 1 share a shape in many UI fonts. Only resolve them as seconds when
    // a preceding numeric duration supplies the context and the duration ends the line.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])\d{1,4}[hm]\s+(?<seconds>[0-9Il]{1,2}s)(?=[\s.,;:!?)\]]*$)", RegexOptions.CultureInvariant)]
    private static partial Regex AmbiguousSeconds();

    internal static TextRecognitionResult Apply(TextRecognitionResult raw)
    {
        string[] textLines = raw.Text.Replace("\r", "").Split('\n');
        var words = raw.Words.ToArray();
        bool changed = false;
        foreach (var line in words.Select((word, index) => (word, index)).GroupBy(item => item.word.Line))
        {
            if (line.Key < 0 || line.Key >= textLines.Length) continue;
            string text = textLines[line.Key];
            var replacements = new List<(int Offset, string Text)>();
            foreach (Match match in AmbiguousSeconds().Matches(text))
            {
                Group seconds = match.Groups["seconds"];
                string amount = seconds.Value[..^1];
                if (!amount.Any(c => c is 'I' or 'l')) continue;
                string digits = amount.Replace('I', '1').Replace('l', '1');
                if (!int.TryParse(digits, out int number) || number >= 60) continue;
                int cursor = 0;
                foreach (var item in line)
                {
                    int offset = text.IndexOf(item.word.Text, cursor, StringComparison.Ordinal);
                    if (offset < 0) break;
                    cursor = offset + item.word.Text.Length;
                    if (offset > seconds.Index || cursor < seconds.Index + seconds.Length) continue;
                    string corrected = digits + "s";
                    int withinWord = seconds.Index - offset;
                    words[item.index] = item.word with
                    {
                        Text = item.word.Text.Remove(withinWord, seconds.Length).Insert(withinWord, corrected)
                    };
                    replacements.Add((seconds.Index, corrected));
                    break;
                }
            }
            foreach (var replacement in replacements.OrderByDescending(item => item.Offset))
            {
                // Substitutions retain length, geometry, and all other original text.
                text = text.Remove(replacement.Offset, replacement.Text.Length).Insert(replacement.Offset, replacement.Text);
                changed = true;
            }
            textLines[line.Key] = text;
        }
        return changed ? raw with { Text = string.Join(Environment.NewLine, textLines), Words = words } : raw;
    }
}
