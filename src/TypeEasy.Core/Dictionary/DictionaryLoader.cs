using System.Text;
using Wubi86.Models;

namespace Wubi86.Core;

public sealed class DictionaryData
{
    public Dictionary<string, CharacterEntry> Characters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, PhraseEntry> Phrases { get; } = new(StringComparer.Ordinal);
}

public static class DictionaryLoader
{
    public static async Task<DictionaryData> LoadAsync(string dataDirectory)
    {
        var data = new DictionaryData();

        string wubiPath = Path.Combine(dataDirectory, "编码.txt");
        string pinyinPath = Path.Combine(dataDirectory, "拼音.txt");
        string phrasePath = Path.Combine(dataDirectory, "词组.txt");

        var wubi = await ReadKeywordFileAsync(wubiPath);
        var pinyin = await ReadKeywordFileAsync(pinyinPath);
        var phrases = await ReadKeywordFileAsync(phrasePath);

        foreach (var (character, value) in wubi)
        {
            if (string.IsNullOrWhiteSpace(character))
                continue;

            var codes = value.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            pinyin.TryGetValue(character, out string? pinyinValue);

            var pinyins = string.IsNullOrWhiteSpace(pinyinValue)
                ? []
                : pinyinValue.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            data.Characters[character] = new CharacterEntry(character, codes, pinyins);
        }

        // 拼音文件可能包含编码文件没有的字符，也保留。
        foreach (var (character, value) in pinyin)
        {
            if (data.Characters.ContainsKey(character))
                continue;

            var pinyins = value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            data.Characters[character] = new CharacterEntry(character, [], pinyins);
        }

        foreach (var (phrase, code) in phrases)
        {
            if (!string.IsNullOrWhiteSpace(phrase) && !string.IsNullOrWhiteSpace(code))
                data.Phrases[phrase] = new PhraseEntry(phrase, code.Trim());
        }

        return data;
    }

    private static async Task<Dictionary<string, string>> ReadKeywordFileAsync(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        bool inKeyword = false;

        while (await reader.ReadLineAsync() is { } rawLine)
        {
            string line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inKeyword = string.Equals(line, "[keyword]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inKeyword)
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();

            if (key.Length > 0)
                result[key] = value;
        }

        return result;
    }
}
