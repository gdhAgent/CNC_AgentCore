// Application/Retrieval/Rerank.cs —— Rerank 精排
using System.Diagnostics;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;

namespace CNC_AgentCore.Application.Retrieval;

public sealed class RerankConfig
{
    public int TopN { get; init; } = 5;
    public double Threshold { get; init; } = 0.30;
    public int MaxDocChars { get; init; } = 1500;
    public bool KeepBelowThreshold { get; init; } = false;
}

public static class Rerank
{
    public static async Task<(List<Hit> Hits, double MaxScore, int Ms)> ApplyAsync(
        string query,
        IReadOnlyList<Hit> hits,
        IRerankClient reranker,
        RerankConfig? cfg = null,
        CancellationToken ct = default)
    {
        cfg ??= new RerankConfig();
        var sw = Stopwatch.StartNew();
        if (hits.Count == 0) return (new List<Hit>(), 0.0, 0);

        var docs = hits.Select(h => DocText(h, cfg.MaxDocChars)).ToList();
        var pairs = await reranker.RerankAsync(query, docs, hits.Count, ct);

        var reranked = new List<Hit>();
        double maxScore = 0.0;
        foreach (var (origIdx, score) in pairs)
        {
            if (origIdx < 0 || origIdx >= hits.Count) continue;
            var src = hits[origIdx];
            var newHit = Hit.Clone(src, newScore: score, newChannel: HitChannel.Rerank);
            newHit.Extra["rerank_score"] = score;
            reranked.Add(newHit);
            if (score > maxScore) maxScore = score;
        }

        var kept = cfg.KeepBelowThreshold ? reranked : reranked.Take(cfg.TopN).ToList();
        for (var i = 0; i < kept.Count; i++) kept[i].Rank = i;
        sw.Stop();
        return (kept, maxScore, (int)sw.ElapsedMilliseconds);
    }

    public static bool ThresholdGate(double maxScore, double threshold) => maxScore >= threshold;

    private static string DocText(Hit hit, int maxChars)
    {
        var text = (hit.Title ?? "").Trim();
        if (!string.IsNullOrEmpty(hit.Content))
            text = (text + "\n" + hit.Content).Trim();
        if (text.Length > maxChars) text = text.Substring(0, maxChars);
        return text.Length > 0 ? text : (hit.Title ?? "").Substring(0, Math.Min(maxChars, hit.Title?.Length ?? 0));
    }
}
