using System.Collections.Generic;

namespace MusicApp.Services.Search;

public enum CompareOp { Equal, Lt, Lte, Gt, Gte }

public abstract record SearchTerm(bool Excluded);

public sealed record FreeTextTerm(string Text, bool Excluded) : SearchTerm(Excluded);
public sealed record PhraseTerm(string Phrase, bool Excluded) : SearchTerm(Excluded);
public sealed record FieldTextTerm(string Field, string Value, bool Excluded) : SearchTerm(Excluded);
public sealed record FieldPhraseTerm(string Field, string Phrase, bool Excluded) : SearchTerm(Excluded);
public sealed record RangeTerm(string Field, double? Min, double? Max, bool Excluded) : SearchTerm(Excluded);
public sealed record CompareTerm(string Field, CompareOp Op, double Value, bool Excluded) : SearchTerm(Excluded);

public sealed class SearchQuery
{
    public List<SearchTerm> Terms { get; } = new();
    public string Raw { get; init; } = string.Empty;
    public bool IsEmpty => Terms.Count == 0;
}
