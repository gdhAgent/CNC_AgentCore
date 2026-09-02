// Application/Retrieval/RetrievalService.cs —— 混合检索多步骤编排。
using System.Diagnostics;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;
using Microsoft.Extensions.AI;
using Pgvector;

namespace CNC_AgentCore.Application.Retrieval;

public sealed class RetrievalService : IRetrievalService
{
    private readonly Npgsql.NpgsqlDataSource _dataSource;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly IRerankClient _reranker;
    private readonly CodeExtractor _codeExtractor;
    private readonly VectorSearch _vectorSearch;
    private readonly FulltextSearch _fulltextSearch;
    private readonly ITokenizer _tokenizer;

    public RetrievalService(
        Npgsql.NpgsqlDataSource dataSource,
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        IRerankClient reranker,
        CodeExtractor codeExtractor,
        VectorSearch vectorSearch,
        FulltextSearch fulltextSearch,
        ITokenizer tokenizer)
    {
        _dataSource = dataSource;
        _embedder = embedder;
        _reranker = reranker;
        _codeExtractor = codeExtractor;
        _vectorSearch = vectorSearch;
        _fulltextSearch = fulltextSearch;
        _tokenizer = tokenizer;
    }

    public async Task<QueryResult> RunQueryAsync(string queryText, RetrievalServiceConfig? cfg = null, CancellationToken ct = default)
    {
        cfg ??= new RetrievalServiceConfig();
        var trace = new TraceRecorder();
        var timing = new QueryTiming();
        var totalSw = Stopwatch.StartNew();
        var traceId = Guid.NewGuid();

        // normalize
        trace.Add("normalize", input: new() { ["query"] = queryText },
            output: new() { ["tokens"] = _tokenizer.Tokenize(queryText) });

        // 1) 报警码抽取 + 精确短路
        var ceSw = Stopwatch.StartNew();
        var ceResult = await _codeExtractor.ExtractAndMatchAsync(queryText, previewChars: 240, ct: ct);
        ceSw.Stop();
        timing.CodeExtract = timing.ExactMatch = (int)ceSw.ElapsedMilliseconds;

        var detectedCodes = ceResult.DetectedCodes.ToList();
        var exactHits = ceResult.ExactHits.ToList();
        var suggestHits = ceResult.SuggestHits.ToList();

        trace.Add("code_extract", ms: timing.CodeExtract,
            input: new() { ["query"] = queryText },
            output: new() { ["detected_codes"] = detectedCodes, ["exact_count"] = exactHits.Count, ["suggest_count"] = suggestHits.Count });
        trace.Add("exact_match", ms: timing.ExactMatch,
            input: new() { ["detected_codes"] = detectedCodes },
            output: new() { ["hits"] = exactHits.Select(h => new Dictionary<string, object?>
            {
                ["type"] = h.Type, ["id"] = h.Id, ["code_norm"] = h.Extra.GetValueOrDefault("code_norm"), ["score"] = h.Score,
            }).ToList() });

        // 2) 向量化
        var embedSw = Stopwatch.StartNew();
        var embeddings = await _embedder.GenerateAsync(new[] { queryText }, cancellationToken: ct);
        embedSw.Stop();
        timing.Embed = (int)embedSw.ElapsedMilliseconds;
        var queryVec = new Vector(embeddings[0].Vector);

        // 3) 向量召回 + 全文召回（并行）
        var vectorTask = _vectorSearch.RecallAsync(queryVec, cfg.VectorTopN, cfg.Brand, previewChars: 240, ct: ct);
        var ftsTask = _fulltextSearch.RecallAsync(queryText, cfg.FulltextTopN, cfg.Brand, previewChars: 240, ct: ct);
        await Task.WhenAll(vectorTask, ftsTask);

        var (vecHits, vecMs) = await vectorTask;
        var (ftsHits, ftsMs) = await ftsTask;
        timing.VectorRecall = vecMs;
        timing.FulltextRecall = ftsMs;

        trace.Add("vector_recall", ms: vecMs,
            input: new() { ["top_n"] = cfg.VectorTopN },
            output: new() { ["count"] = vecHits.Count, ["candidates"] = vecHits.Take(10).Select((h, i) => new Dictionary<string, object?>
            {
                ["type"] = h.Type, ["id"] = h.Id, ["score"] = Math.Round(h.Score, 4), ["rank"] = i + 1,
                ["title"] = h.Title,    // 候选标题：trace UI 排名表展示
            }).ToList() });
        trace.Add("fulltext_recall", ms: ftsMs,
            input: new() { ["top_n"] = cfg.FulltextTopN },
            output: new() { ["count"] = ftsHits.Count, ["candidates"] = ftsHits.Take(10).Select((h, i) => new Dictionary<string, object?>
            {
                ["type"] = h.Type, ["id"] = h.Id, ["score"] = Math.Round(h.Score, 4), ["rank"] = i + 1,
                ["title"] = h.Title,
            }).ToList() });

        // 4) RRF 融合
        var fusedSw = Stopwatch.StartNew();
        var fused = Fusion.Rrf(new[] { (IReadOnlyList<Hit>)vecHits, ftsHits }, new FusionConfig { TopN = cfg.RrfTopN });
        fusedSw.Stop();
        timing.RrfFusion = (int)fusedSw.ElapsedMilliseconds;
        trace.Add("rrf_fusion", ms: timing.RrfFusion,
            input: new() { ["channels"] = new[] { "vector", "fulltext" }, ["k"] = 60 },
            output: new() { ["count"] = fused.Count, ["candidates"] = fused.Take(10).Select((h, i) => new Dictionary<string, object?>
            {
                ["type"] = h.Type, ["id"] = h.Id, ["score"] = Math.Round(h.Score, 4), ["rank"] = i + 1,
                ["ranks_by_channel"] = h.Extra.GetValueOrDefault("ranks_by_channel"),
                ["title"] = h.Title,
            }).ToList() });

        // 5) Rerank
        var (reranked, maxScore, rerankMs) = await Rerank.ApplyAsync(queryText, fused, _reranker,
            new RerankConfig { TopN = cfg.RerankTopN, Threshold = cfg.RerankThreshold }, ct);
        timing.Rerank = rerankMs;
        trace.Add("rerank", ms: rerankMs,
            input: new() { ["top_n"] = cfg.RerankTopN, ["threshold"] = cfg.RerankThreshold },
            output: new() { ["count"] = reranked.Count, ["max_score"] = Math.Round(maxScore, 4),
                ["candidates"] = reranked.Take(5).Select((h, i) => new Dictionary<string, object?>
                {
                    ["type"] = h.Type, ["id"] = h.Id, ["score"] = Math.Round(h.Score, 4), ["rank"] = i + 1,
                    ["title"] = h.Title,
                }).ToList() });

        // 6) 阈值闸门
        var gateSw = Stopwatch.StartNew();
        bool refused;
        string? refusedReason = null;
        if (exactHits.Count > 0)
        {
            refused = false; refusedReason = null;
        }
        else
        {
            refused = !Rerank.ThresholdGate(maxScore, cfg.RerankThreshold);
            if (fused.Count == 0) { refused = true; refusedReason = "no_candidates"; }
            else if (refused) refusedReason = $"max_rerank_score={maxScore:F3} < threshold={cfg.RerankThreshold}";
        }
        gateSw.Stop();
        timing.ThresholdGate = (int)gateSw.ElapsedMilliseconds;
        trace.Add("threshold_gate", ms: timing.ThresholdGate,
            output: new() { ["passed"] = !refused, ["max_score"] = Math.Round(maxScore, 4),
                ["threshold"] = cfg.RerankThreshold, ["reason"] = refusedReason });

        // 7) route 决策 + topk 合并
        string route;
        List<Hit> topk;
        if (exactHits.Count > 0 && !refused)
        {
            route = RouteKind.ExactCode;
            var exactIds = new HashSet<long>(exactHits.Select(h => h.Id));
            var rest = reranked.Where(h => !(h.Type == "alarm" && exactIds.Contains(h.Id))).ToList();
            var budget = Math.Max(0, cfg.RerankTopN - exactHits.Count);
            topk = exactHits.Concat(rest.Take(budget)).ToList();
        }
        else
        {
            route = refused ? RouteKind.Refused : RouteKind.Hybrid;
            topk = reranked;
        }

        // 8) 总耗时
        totalSw.Stop();
        timing.Total = (int)totalSw.ElapsedMilliseconds;

        // 9) 快照
        var snapshot = new List<Dictionary<string, object?>>();
        var seenKeys = new HashSet<(string, long)>();
        void Add(Hit h)
        {
            if (!seenKeys.Add((h.Type, h.Id))) return;
            snapshot.Add(new()
            {
                ["type"] = h.Type, ["id"] = h.Id, ["score"] = Math.Round(h.Score, 4),
                ["channel"] = h.Channel, ["rank"] = h.Rank,
            });
        }
        foreach (var h in exactHits) Add(h);
        foreach (var h in suggestHits) Add(h);
        foreach (var h in vecHits.Take(10)) Add(h);
        foreach (var h in ftsHits.Take(10)) Add(h);
        foreach (var h in fused.Take(10)) Add(h);
        foreach (var h in reranked) Add(h);

        return new QueryResult
        {
            TraceId = traceId,
            DetectedCodes = detectedCodes,
            Route = route,
            Refused = refused,
            RefusedReason = refusedReason,
            Topk = topk,
            SuggestHits = suggestHits,
            Timing = timing,
            RetrievedSnapshot = snapshot,
            TraceSteps = trace.Steps,
        };
    }
}
