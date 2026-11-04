namespace Wubi86.Models;
public sealed record CharacterEntry(string Character, IReadOnlyList<string> WubiCodes, IReadOnlyList<string> Pinyins);
public sealed record PhraseEntry(string Phrase, string WubiCode);
public enum SearchKind { Character, Phrase, Pinyin, Wubi }
public sealed record SearchResult(SearchKind Kind, string DisplayText, IReadOnlyList<string>? WubiCodes=null, IReadOnlyList<string>? Pinyins=null, string? GlyphKey=null, string? WubiCode=null);
