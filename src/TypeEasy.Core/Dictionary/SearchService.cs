using Wubi86.Models;

namespace Wubi86.Core;

public sealed class SearchService(SearchIndex index)
{
    public IReadOnlyList<SearchResult> Search(string query, int limit = 30)
    {
        query = query.Trim();

        if (query.Length == 0)
            return [];

        // 1. 单字精确查询
        if (index.Characters.TryGetValue(query, out var character))
        {
            return
            [
                new SearchResult(
                    SearchKind.Character,
                    character.Character,
                    WubiCodes: character.WubiCodes,
                    Pinyins: character.Pinyins,
                    GlyphKey: character.Character)
            ];
        }

        // 2. 词组精确查询
        if (index.Phrases.TryGetValue(query, out var phrase))
        {
            return
            [
                new SearchResult(
                    SearchKind.Phrase,
                    phrase.Phrase,
                    WubiCode: phrase.WubiCode)
            ];
        }

        // 3. 拉丁字母：同时尝试拼音和五笔。
        if (PinyinNormalizer.Normalize(query).All(ch => char.IsAsciiLetter(ch)))
        {
            var results = new List<SearchResult>();

            foreach (var entry in index.FindByPinyin(query, limit))
            {
                results.Add(new SearchResult(
                    SearchKind.Pinyin,
                    entry.Character,
                    WubiCodes: entry.WubiCodes,
                    Pinyins: entry.Pinyins,
                    GlyphKey: entry.Character));
            }

            foreach (var entry in index.FindByWubi(query, limit))
            {
                if (results.Any(r => r.DisplayText == entry.Character))
                    continue;

                results.Add(new SearchResult(
                    SearchKind.Wubi,
                    entry.Character,
                    WubiCodes: entry.WubiCodes,
                    Pinyins: entry.Pinyins,
                    GlyphKey: entry.Character));
            }

            return results.Take(limit).ToArray();
        }

        // 4. 中文词组前缀候选。
        return index.Phrases.Values
            .Where(p => p.Phrase.StartsWith(query, StringComparison.Ordinal))
            .Take(limit)
            .Select(p => new SearchResult(
                SearchKind.Phrase,
                p.Phrase,
                WubiCode: p.WubiCode))
            .ToArray();
    }
}

