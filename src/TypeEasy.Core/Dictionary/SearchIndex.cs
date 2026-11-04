using Wubi86.Models;

namespace Wubi86.Core;

public sealed class SearchIndex
{
    public IReadOnlyDictionary<string, CharacterEntry> Characters { get; }
    public IReadOnlyDictionary<string, PhraseEntry> Phrases { get; }

    private readonly Dictionary<string, List<CharacterEntry>> _byPinyin =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<CharacterEntry>> _byWubi =
        new(StringComparer.OrdinalIgnoreCase);

    public SearchIndex(DictionaryData data)
    {
        Characters = data.Characters;
        Phrases = data.Phrases;

        foreach (var entry in data.Characters.Values)
        {
            foreach (string pinyin in entry.Pinyins)
            {
                string normalized = PinyinNormalizer.Normalize(pinyin);
                Add(_byPinyin, normalized, entry);
            }

            foreach (string code in entry.WubiCodes)
                Add(_byWubi, code.ToLowerInvariant(), entry);
        }
    }

    public IReadOnlyList<CharacterEntry> FindByPinyin(string query, int limit = 30)
    {
        string normalized = PinyinNormalizer.Normalize(query);

        if (_byPinyin.TryGetValue(normalized, out var exact))
            return exact.Take(limit).ToArray();

        return _byPinyin
            .Where(pair => pair.Key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value)
            .DistinctBy(entry => entry.Character)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<CharacterEntry> FindByWubi(string query, int limit = 30)
    {
        string normalized = query.Trim().ToLowerInvariant();

        if (_byWubi.TryGetValue(normalized, out var exact))
            return exact.Take(limit).ToArray();

        return _byWubi
            .Where(pair => pair.Key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value)
            .DistinctBy(entry => entry.Character)
            .Take(limit)
            .ToArray();
    }

    private static void Add(
        Dictionary<string, List<CharacterEntry>> map,
        string key,
        CharacterEntry entry)
    {
        if (key.Length == 0)
            return;

        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(entry);
    }
}
