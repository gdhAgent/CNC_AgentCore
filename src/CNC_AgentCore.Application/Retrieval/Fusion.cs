// Application/Retrieval/Fusion.cs —— RRF 多路融合
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;

namespace CNC_AgentCore.Application.Retrieval;

public sealed class FusionConfig
{
    public int K { get; init; } = 60;
    public int TopN { get; init; } = 20;
    public double MinScore { get; init; } = 0.0;
}

public static class Fusion
{
    public static List<Hit> Rrf(IReadOnlyList<IReadOnlyList<Hit>> channels, FusionConfig? cfg = null)
    {
        cfg ??= new FusionConfig();
        var merged = new Dictionary<(string, long), (double Rrf, Hit Template)>();

        foreach (var hits in channels)
        {
            var chName = hits.Count > 0 ? hits[0].Channel : "ch0";
            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                var rank = hit.Rank > 0 ? hit.Rank : i;
                var contrib = 1.0 / (cfg.K + rank);

                if (merged.TryGetValue((hit.Type, hit.Id), out var existing))
                {
                    existing.Template.Extra.TryAdd("rrf_breakdown", new Dictionary<string, double>());
                    if (existing.Template.Extra["rrf_breakdown"] is Dictionary<string, double> bd)
                        bd[chName] = Math.Round(contrib, 6);
                    existing.Template.Extra.TryAdd("ranks_by_channel", new Dictionary<string, int>());
                    if (existing.Template.Extra["ranks_by_channel"] is Dictionary<string, int> rbc)
                        rbc[chName] = rank;
                    // origin_channels：跨多 channel 命中同一 (type,id) 时累加渠道标签
                    if (existing.Template.Extra.TryGetValue("origin_channels", out var originsObj)
                        && originsObj is List<string> origins && !origins.Contains(chName))
                    {
                        origins.Add(chName);
                    }
                    merged[(hit.Type, hit.Id)] = (existing.Rrf + contrib, existing.Template);
                }
                else
                {
                    var cloned = Hit.Clone(hit, newScore: 0, newChannel: HitChannel.Rrf);
                    cloned.Extra["rrf_breakdown"] = new Dictionary<string, double> { [chName] = Math.Round(contrib, 6) };
                    cloned.Extra["ranks_by_channel"] = new Dictionary<string, int> { [chName] = rank };
                    cloned.Extra["origin_channels"] = new List<string> { chName };
                    merged[(hit.Type, hit.Id)] = (contrib, cloned);
                }
            }
        }

        var list = merged.Values.Select(v =>
        {
            v.Template.Score = Math.Round(v.Rrf, 6);
            return v.Template;
        }).ToList();

        list.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (cfg.MinScore > 0)
            list = list.Where(h => h.Score >= cfg.MinScore).ToList();

        var top = list.Take(cfg.TopN).ToList();
        for (var i = 0; i < top.Count; i++) top[i].Rank = i;
        return top;
    }
}
