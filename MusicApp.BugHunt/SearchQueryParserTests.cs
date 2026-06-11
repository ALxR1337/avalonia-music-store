using System.Linq;
using MusicApp.Services.Search;
using Xunit;

namespace MusicApp.BugHunt;

public class SearchQueryParserTests
{
    [Fact]
    public void Empty_and_whitespace_input_gives_empty_query()
    {
        Assert.True(SearchQueryParser.Parse(null).IsEmpty);
        Assert.True(SearchQueryParser.Parse("").IsEmpty);
        Assert.True(SearchQueryParser.Parse("   ").IsEmpty);
    }

    [Fact]
    public void Free_text_words_become_free_text_terms()
    {
        var q = SearchQueryParser.Parse("pink floyd");
        Assert.Equal(2, q.Terms.Count);
        var words = q.Terms.OfType<FreeTextTerm>().Select(t => t.Text).ToList();
        Assert.Equal(new[] { "pink", "floyd" }, words);
        Assert.All(q.Terms, t => Assert.False(t.Excluded));
    }

    [Fact]
    public void Quoted_phrase_becomes_phrase_term()
    {
        var q = SearchQueryParser.Parse("\"dark side of the moon\"");
        var term = Assert.IsType<PhraseTerm>(Assert.Single(q.Terms));
        Assert.Equal("dark side of the moon", term.Phrase);
    }

    [Fact]
    public void Unterminated_phrase_is_tolerated()
    {
        var q = SearchQueryParser.Parse("\"abbey road");
        var term = Assert.IsType<PhraseTerm>(Assert.Single(q.Terms));
        Assert.Equal("abbey road", term.Phrase);
    }

    [Theory]
    [InlineData("artist:Beatles", "artist", "Beatles")]
    [InlineData("genre:Rock", "genre", "Rock")]
    [InlineData("format:CD", "format", "CD")]
    public void Field_value_becomes_field_text_term(string input, string field, string value)
    {
        var q = SearchQueryParser.Parse(input);
        var term = Assert.IsType<FieldTextTerm>(Assert.Single(q.Terms));
        Assert.Equal(field, term.Field);
        Assert.Equal(value, term.Value);
    }

    [Theory]
    [InlineData("виконавець:Beatles", "artist")]
    [InlineData("артист:Beatles", "artist")]
    [InlineData("жанр:Rock", "genre")]
    [InlineData("ЖАНР:Rock", "genre")]
    [InlineData("альбом:Yeezus", "album")]
    [InlineData("трек:Lithium", "track")]
    [InlineData("пісня:Lithium", "track")]
    [InlineData("формат:LP", "format")]
    [InlineData("слова:love", "lyrics")]
    public void Ukrainian_aliases_map_to_canonical_fields(string input, string canonical)
    {
        var q = SearchQueryParser.Parse(input);
        var term = Assert.IsType<FieldTextTerm>(Assert.Single(q.Terms));
        Assert.Equal(canonical, term.Field);
    }

    [Fact]
    public void Field_phrase_becomes_field_phrase_term()
    {
        var q = SearchQueryParser.Parse("album:\"Kind of Blue\"");
        var term = Assert.IsType<FieldPhraseTerm>(Assert.Single(q.Terms));
        Assert.Equal("album", term.Field);
        Assert.Equal("Kind of Blue", term.Phrase);
    }

    [Fact]
    public void Numeric_field_with_plain_number_becomes_equality_compare()
    {
        var q = SearchQueryParser.Parse("рік:1973");
        var term = Assert.IsType<CompareTerm>(Assert.Single(q.Terms));
        Assert.Equal("year", term.Field);
        Assert.Equal(CompareOp.Equal, term.Op);
        Assert.Equal(1973, term.Value);
    }

    [Fact]
    public void Full_range_parses_min_and_max()
    {
        var q = SearchQueryParser.Parse("year:1990..2000");
        var term = Assert.IsType<RangeTerm>(Assert.Single(q.Terms));
        Assert.Equal("year", term.Field);
        Assert.Equal(1990, term.Min);
        Assert.Equal(2000, term.Max);
    }

    [Fact]
    public void Open_ended_range_to_max_only()
    {
        var q = SearchQueryParser.Parse("ціна:..500");
        var term = Assert.IsType<RangeTerm>(Assert.Single(q.Terms));
        Assert.Equal("price", term.Field);
        Assert.Null(term.Min);
        Assert.Equal(500, term.Max);
    }

    [Fact]
    public void Open_ended_range_from_min_only()
    {
        var q = SearchQueryParser.Parse("price:1000..");
        var term = Assert.IsType<RangeTerm>(Assert.Single(q.Terms));
        Assert.Equal(1000, term.Min);
        Assert.Null(term.Max);
    }

    [Theory]
    [InlineData("price:<500", CompareOp.Lt, 500)]
    [InlineData("price:<=500", CompareOp.Lte, 500)]
    [InlineData("рейтинг:>4", CompareOp.Gt, 4)]
    [InlineData("рейтинг:>=4.5", CompareOp.Gte, 4.5)]
    public void Comparators_parse_op_and_value(string input, CompareOp op, double value)
    {
        var q = SearchQueryParser.Parse(input);
        var term = Assert.IsType<CompareTerm>(Assert.Single(q.Terms));
        Assert.Equal(op, term.Op);
        Assert.Equal(value, term.Value);
    }

    [Fact]
    public void Comma_decimal_separator_is_accepted()
    {
        var q = SearchQueryParser.Parse("rating:>=4,5");
        var term = Assert.IsType<CompareTerm>(Assert.Single(q.Terms));
        Assert.Equal(4.5, term.Value);
    }

    [Fact]
    public void Leading_dash_excludes_a_word()
    {
        var q = SearchQueryParser.Parse("rock -metallica");
        Assert.Equal(2, q.Terms.Count);
        Assert.False(q.Terms[0].Excluded);
        var excluded = Assert.IsType<FreeTextTerm>(q.Terms[1]);
        Assert.True(excluded.Excluded);
        Assert.Equal("metallica", excluded.Text);
    }

    [Fact]
    public void Dash_excludes_field_term_and_phrase()
    {
        var q = SearchQueryParser.Parse("-genre:Pop -\"greatest hits\"");
        Assert.Equal(2, q.Terms.Count);
        var field = Assert.IsType<FieldTextTerm>(q.Terms[0]);
        Assert.True(field.Excluded);
        Assert.Equal("genre", field.Field);
        var phrase = Assert.IsType<PhraseTerm>(q.Terms[1]);
        Assert.True(phrase.Excluded);
        Assert.Equal("greatest hits", phrase.Phrase);
    }

    [Fact]
    public void Embedded_hyphen_stays_inside_word()
    {
        var q = SearchQueryParser.Parse("hip-hop");
        var term = Assert.IsType<FreeTextTerm>(Assert.Single(q.Terms));
        Assert.Equal("hip-hop", term.Text);
        Assert.False(term.Excluded);
    }

    [Fact]
    public void Unknown_field_prefix_is_treated_as_free_text()
    {
        var q = SearchQueryParser.Parse("nosuchfield:value");
        // "nosuchfield" не в алиасах — двоеточие игнорируется, обе части как свободный текст
        Assert.Equal(2, q.Terms.Count);
        Assert.All(q.Terms, t => Assert.IsType<FreeTextTerm>(t));
    }

    [Fact]
    public void Mixed_query_combines_all_term_kinds()
    {
        var q = SearchQueryParser.Parse("жанр:Rock рік:1990..2000 ціна:<=800 -виконавець:Nickelback \"the wall\" floyd");
        Assert.Equal(6, q.Terms.Count);
        Assert.IsType<FieldTextTerm>(q.Terms[0]);
        Assert.IsType<RangeTerm>(q.Terms[1]);
        Assert.IsType<CompareTerm>(q.Terms[2]);
        var excl = Assert.IsType<FieldTextTerm>(q.Terms[3]);
        Assert.True(excl.Excluded);
        Assert.Equal("artist", excl.Field);
        Assert.IsType<PhraseTerm>(q.Terms[4]);
        Assert.IsType<FreeTextTerm>(q.Terms[5]);
    }

    [Fact]
    public void Trailing_field_colon_without_value_is_ignored()
    {
        var q = SearchQueryParser.Parse("genre:");
        Assert.True(q.IsEmpty);
    }

    [Fact]
    public void Raw_is_preserved()
    {
        var q = SearchQueryParser.Parse("  artist:Beatles  ");
        Assert.Equal("  artist:Beatles  ", q.Raw);
    }
}
