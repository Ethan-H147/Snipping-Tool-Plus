using System.Text.RegularExpressions;

namespace SnippingToolPlus;

internal static partial class VersionTextCorrection
{
    // Restrict O/0 disambiguation to explicit dotted v-prefixed version numbers.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])v[0-9Oo]+(?:\.[0-9Oo]+){1,3}(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Version();

    internal static TextRecognitionResult Apply(TextRecognitionResult raw)
    {
        var words = raw.Words.Select(word => word with
        {
            Text = Version().Replace(word.Text, match => match.Value.Any(char.IsDigit)
                ? match.Value.Replace('O', '0').Replace('o', '0') : match.Value)
        }).ToArray();
        return (raw with { Words = words }).Format();
    }
}
