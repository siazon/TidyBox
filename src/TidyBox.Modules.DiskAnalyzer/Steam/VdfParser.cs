using System.Text;

namespace TidyBox.Modules.DiskAnalyzer.Steam;

/// <summary>
/// Minimal parser for Valve's text KeyValues format (.vdf/.acf): quoted strings and nested { } blocks.
/// Values are either <see cref="string"/> or a nested case-insensitive <c>Dictionary&lt;string, object&gt;</c>.
/// Throws <see cref="FormatException"/> on anything it does not understand.
/// </summary>
internal static class VdfParser
{
    private enum Kind { Text, Open, Close, End }

    public static Dictionary<string, object> Parse(string text)
    {
        var pos = 0;
        return ParseBlock(text, ref pos, topLevel: true);
    }

    private static Dictionary<string, object> ParseBlock(string text, ref int pos, bool topLevel)
    {
        var block = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var (kind, key) = Next(text, ref pos);
            switch (kind)
            {
                case Kind.End when topLevel:
                    return block;
                case Kind.End:
                    throw new FormatException("unexpected end of file inside a block");
                case Kind.Close when !topLevel:
                    return block;
                case Kind.Close:
                    throw new FormatException("unbalanced '}'");
                case Kind.Open:
                    throw new FormatException("block without a key");
            }

            var (valueKind, value) = Next(text, ref pos);
            block[key] = valueKind switch
            {
                Kind.Text => value,
                Kind.Open => ParseBlock(text, ref pos, topLevel: false),
                _ => throw new FormatException($"missing value for key '{key}'"),
            };
        }
    }

    private static (Kind Kind, string Value) Next(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
            }
            else
            {
                break;
            }
        }

        if (pos >= text.Length) return (Kind.End, "");

        switch (text[pos])
        {
            case '{':
                pos++;
                return (Kind.Open, "");
            case '}':
                pos++;
                return (Kind.Close, "");
            case '"':
                return (Kind.Text, ReadQuoted(text, ref pos));
            default:
                throw new FormatException($"unexpected character '{text[pos]}' at {pos}");
        }
    }

    private static string ReadQuoted(string text, ref int pos)
    {
        var sb = new StringBuilder();
        pos++; // opening quote
        while (pos < text.Length)
        {
            var c = text[pos++];
            if (c == '"') return sb.ToString();
            if (c == '\\' && pos < text.Length)
            {
                var escaped = text[pos++];
                sb.Append(escaped switch { 'n' => '\n', 't' => '\t', _ => escaped }); // \\ and \" map to themselves
            }
            else
            {
                sb.Append(c);
            }
        }

        throw new FormatException("unterminated string");
    }
}
