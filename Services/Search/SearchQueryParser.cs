using System;
using System.Collections.Generic;
using System.Globalization;

namespace MusicApp.Services.Search;

/// <summary>
/// Recursive-descent parser for the АІПС query DSL:
///   word                       → FreeTextTerm
///   "exact phrase"             → PhraseTerm
///   field:value                → FieldTextTerm
///   field:"exact phrase"       → FieldPhraseTerm
///   field:a..b | field:..b | field:a..  → RangeTerm
///   field:&lt;x | field:&gt;x | field:&lt;=x | field:&gt;=x  → CompareTerm
///   -term                      → Excluded variant of the next term
/// </summary>
public static class SearchQueryParser
{
    private static readonly Dictionary<string, string> FieldAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["артист"] = "artist", ["виконавець"] = "artist", ["artist"] = "artist",
        ["альбом"] = "album", ["album"] = "album",
        ["трек"] = "track", ["пісня"] = "track", ["track"] = "track",
        ["жанр"] = "genre", ["genre"] = "genre",
        ["формат"] = "format", ["format"] = "format",
        ["рік"] = "year", ["year"] = "year",
        ["ціна"] = "price", ["price"] = "price",
        ["рейтинг"] = "rating", ["rating"] = "rating",
        ["текст"] = "lyrics", ["слова"] = "lyrics", ["lyrics"] = "lyrics",
    };

    private static readonly HashSet<string> NumericFields = new(StringComparer.Ordinal) { "year", "price", "rating" };

    public static SearchQuery Parse(string? input)
    {
        var q = new SearchQuery { Raw = input ?? string.Empty };
        if (string.IsNullOrWhiteSpace(input)) return q;

        var tokens = Tokenize(input);
        int i = 0;
        while (i < tokens.Count)
        {
            bool excluded = false;
            if (tokens[i] is { Kind: TokenKind.Minus })
            {
                excluded = true;
                i++;
                if (i >= tokens.Count) break;
            }

            var tok = tokens[i];

            // field:rest
            if (tok.Kind == TokenKind.Word && i + 1 < tokens.Count
                && tokens[i + 1].Kind == TokenKind.Colon
                && FieldAliases.TryGetValue(tok.Text, out var canonical))
            {
                i += 2; // consume field + :
                if (i >= tokens.Count) break;
                ParseFieldValue(canonical, tokens, ref i, excluded, q);
                continue;
            }

            // standalone phrase
            if (tok.Kind == TokenKind.Phrase)
            {
                q.Terms.Add(new PhraseTerm(tok.Text, excluded));
                i++; continue;
            }

            // standalone word
            if (tok.Kind == TokenKind.Word)
            {
                q.Terms.Add(new FreeTextTerm(tok.Text, excluded));
                i++; continue;
            }

            // skip stray colon / dots / comparators
            i++;
        }
        return q;
    }

    private static void ParseFieldValue(string field, List<Token> tokens, ref int i, bool excluded, SearchQuery q)
    {
        if (i >= tokens.Count) return;
        var first = tokens[i];

        // field:"phrase"
        if (first.Kind == TokenKind.Phrase)
        {
            q.Terms.Add(new FieldPhraseTerm(field, first.Text, excluded));
            i++; return;
        }

        // field:<x  field:<=x  field:>x  field:>=x
        if (first.Kind == TokenKind.Lt || first.Kind == TokenKind.Lte
            || first.Kind == TokenKind.Gt || first.Kind == TokenKind.Gte)
        {
            i++;
            if (i >= tokens.Count) return;
            if (TryParseNumber(tokens[i].Text, out var num))
            {
                var op = first.Kind switch
                {
                    TokenKind.Lt => CompareOp.Lt,
                    TokenKind.Lte => CompareOp.Lte,
                    TokenKind.Gt => CompareOp.Gt,
                    TokenKind.Gte => CompareOp.Gte,
                    _ => CompareOp.Equal
                };
                q.Terms.Add(new CompareTerm(field, op, num, excluded));
                i++;
            }
            return;
        }

        // field:..b
        if (first.Kind == TokenKind.DotDot)
        {
            i++;
            if (i < tokens.Count && TryParseNumber(tokens[i].Text, out var hi))
            {
                q.Terms.Add(new RangeTerm(field, null, hi, excluded));
                i++;
            }
            return;
        }

        // field:value possibly  value..  or  value..b
        if (first.Kind == TokenKind.Word)
        {
            var hasNumber = TryParseNumber(first.Text, out var lo);
            if (i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.DotDot)
            {
                i += 2;
                if (i < tokens.Count && tokens[i].Kind == TokenKind.Word && TryParseNumber(tokens[i].Text, out var hi))
                {
                    q.Terms.Add(new RangeTerm(field, hasNumber ? lo : null, hi, excluded));
                    i++;
                }
                else
                {
                    q.Terms.Add(new RangeTerm(field, hasNumber ? lo : null, null, excluded));
                }
                return;
            }

            if (NumericFields.Contains(field) && hasNumber)
            {
                q.Terms.Add(new CompareTerm(field, CompareOp.Equal, lo, excluded));
            }
            else
            {
                q.Terms.Add(new FieldTextTerm(field, first.Text, excluded));
            }
            i++;
        }
    }

    private static bool TryParseNumber(string raw, out double value)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // --- Tokenizer ---

    private enum TokenKind { Word, Phrase, Colon, DotDot, Minus, Lt, Lte, Gt, Gte }

    private sealed record Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '"')
            {
                int start = ++i;
                while (i < s.Length && s[i] != '"') i++;
                tokens.Add(new Token(TokenKind.Phrase, s[start..i]));
                if (i < s.Length) i++; // closing quote
                continue;
            }

            if (c == ':') { tokens.Add(new Token(TokenKind.Colon, ":")); i++; continue; }
            if (c == '-')
            {
                // dash at start of token (not embedded in word) means exclusion
                bool isTokenStart = tokens.Count == 0
                    || tokens[^1].Kind == TokenKind.Colon
                    || tokens[^1].Kind == TokenKind.DotDot
                    || tokens[^1].Kind == TokenKind.Minus;
                // also: dash with whitespace before (we already consumed whitespace)
                if (isTokenStart || (i > 0 && char.IsWhiteSpace(s[i - 1])) || i == 0)
                {
                    tokens.Add(new Token(TokenKind.Minus, "-"));
                    i++;
                    continue;
                }
            }
            if (c == '.' && i + 1 < s.Length && s[i + 1] == '.')
            {
                tokens.Add(new Token(TokenKind.DotDot, ".."));
                i += 2; continue;
            }
            if (c == '<')
            {
                if (i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new Token(TokenKind.Lte, "<=")); i += 2; }
                else { tokens.Add(new Token(TokenKind.Lt, "<")); i++; }
                continue;
            }
            if (c == '>')
            {
                if (i + 1 < s.Length && s[i + 1] == '=') { tokens.Add(new Token(TokenKind.Gte, ">=")); i += 2; }
                else { tokens.Add(new Token(TokenKind.Gt, ">")); i++; }
                continue;
            }

            // word: run until whitespace/separator
            int wordStart = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ':' && s[i] != '"'
                   && s[i] != '<' && s[i] != '>'
                   && !(s[i] == '.' && i + 1 < s.Length && s[i + 1] == '.'))
            {
                i++;
            }
            if (i > wordStart) tokens.Add(new Token(TokenKind.Word, s[wordStart..i]));
        }
        return tokens;
    }
}
