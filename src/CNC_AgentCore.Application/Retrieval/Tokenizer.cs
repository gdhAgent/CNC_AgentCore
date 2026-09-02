// Application/Retrieval/Tokenizer.cs —— 中文分词（双字切分 + user_dict 整体保留）。
// 入库/查询用同一实现对称分词，输出空格连接串喂 PG to_tsvector('simple', ...)。

using System.Collections.Concurrent;

namespace CNC_AgentCore.Application.Retrieval;

public interface ITokenizer
{
    string Tokenize(string text);

    void LoadUserDict(IEnumerable<(string Canonical, string[]? Synonyms)> entries);
}

public sealed class SimpleTokenizer : ITokenizer
{
    // 缓存：同一文本只分词一次
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly HashSet<string> _userDict = new(StringComparer.Ordinal);

    public string Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return _cache.GetOrAdd(text, t =>
        {
            // 简化分词：按非字母数字汉字字符切分，对每个片段做二次切分
            // 工业术语由 user_dict 提供整体保留
            var tokens = new List<string>();
            var buf = new System.Text.StringBuilder();
            foreach (var ch in t)
            {
                if (char.IsLetterOrDigit(ch) || IsChinese(ch))
                {
                    buf.Append(ch);
                }
                else if (buf.Length > 0)
                {
                    FlushToken(buf, tokens);
                    buf.Clear();
                }
            }
            if (buf.Length > 0) FlushToken(buf, tokens);
            return string.Join(" ", tokens);
        });
    }

    public void LoadUserDict(IEnumerable<(string Canonical, string[]? Synonyms)> entries)
    {
        foreach (var (canonical, _) in entries)
        {
            if (!string.IsNullOrWhiteSpace(canonical))
                _userDict.Add(canonical.Trim());
        }
    }

    private void FlushToken(System.Text.StringBuilder buf, List<string> tokens)
    {
        var word = buf.ToString();
        // 优先匹配 user dict
        if (_userDict.Contains(word)) { tokens.Add(word); return; }
        // 中文按双字切（最简实现）；英文数字保留整体
        if (word.Any(IsChinese) && word.Length > 2)
        {
            for (var i = 0; i < word.Length - 1; i++)
            {
                var bigram = word.Substring(i, 2);
                if (_userDict.Contains(bigram)) { tokens.Add(bigram); i++; continue; }
                tokens.Add(bigram);
            }
            // 单字尾巴
            if (word.Length % 2 == 1) tokens.Add(word.Substring(word.Length - 1, 1));
        }
        else
        {
            tokens.Add(word);
        }
    }

    private static bool IsChinese(char ch) => ch >= 0x4E00 && ch <= 0x9FFF;
}
