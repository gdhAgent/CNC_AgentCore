// Application/Agent/Router.cs —— Agent 受限工具路由状态机。
//
// 显式状态机（不用 LangChain）；超轮次/取消/超时/异常一律降级纯 RAG 直答（route=rag_fallback）。
// 拒答判定先于引用校验。
//
// 流式实现：C# 迭代器不允许在含 catch 的 try 中 yield，故用 Channel ——
// 后台任务 StreamCoreAsync 向 channel 写事件，外层迭代器只转发 channel 事件。

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using CNC_AgentCore.Application.Agent.Tools;
using CNC_AgentCore.Application.Retrieval;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Domain.Enums;
using CNC_AgentCore.Domain.ValueObjects;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CNC_AgentCore.Application.Agent;

public sealed class Router : IAgentRouter
{
    private readonly IChatClient _chat;
    private readonly IToolExecutor _tools;
    private readonly ILogger<Router> _log;
    private readonly ToolRegistry _registry;
    private readonly IRetrievalService? _retrieval;          // 降级路径用
    private readonly AgentConfig _config;

    public Router(
        IChatClient chat,
        IToolExecutor tools,
        ILogger<Router> log,
        ToolRegistry registry,
        IRetrievalService? retrieval = null,
        AgentConfig? config = null)
    {
        _chat = chat;
        _tools = tools;
        _log = log;
        _registry = registry;
        _retrieval = retrieval;
        _config = config ?? new AgentConfig();
    }

    public async Task<AgentResult> RunAsync(string query, CancellationToken ct = default)
    {
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(query))
        {
            return new AgentResult
            {
                Answer = "查询内容为空。",
                Route = RouteKind.Refused,
                Refused = true,
                RefusedReason = RefusalReason.EmptyQuery,
            };
        }

        var trace = new TraceRecorder();
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, Prompts.SystemPrompt),
            new ChatMessage(ChatRole.User, query),
        };
        var toolTrace = new List<Dictionary<string, object?>>();
        var sw = Stopwatch.StartNew();

        try
        {
            using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TotalTimeoutSec));
            using var ctsLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);

            int rounds = 0;
            int maxRef = 0;
            for (rounds = 1; rounds <= _config.MaxRounds; rounds++)
            {
                var opts = new ChatOptions
                {
                    Temperature = (float?)_config.Temperature,
                    MaxOutputTokens = _config.MaxTokens,
                    Tools = _registry.GetAllFunctions().ToList(),
                };

                var t0 = Stopwatch.GetTimestamp();
                var resp = await _chat.GetResponseAsync(messages, opts, ctsLinked.Token);
                var t1 = Stopwatch.GetTimestamp();
                var ms = (int)((t1 - t0) * 1000 / (double)Stopwatch.Frequency);

                var toolCalls = ExtractToolCalls(resp);
                trace.Add("llm_generate", ms: ms,
                    input: new() { ["round"] = rounds, ["tools"] = toolCalls.Select(tc => tc.Name).ToList() },
                    output: new() { ["decision"] = toolCalls.Count > 0 ? "tool_calls" : "final" });

                if (toolCalls.Count == 0)
                {
                    var content = ExtractContent(resp);
                    return Finish(content, rounds, toolTrace, sw, maxRef, grounded: HasGrounding(toolTrace));
                }

                // 执行工具（Task.WhenAll + 8s 单工具超时）
                var executed = await ExecuteToolCallsAsync(toolCalls, ctsLinked.Token);
                toolTrace.AddRange(executed);
                maxRef = Math.Max(maxRef, ComputeMaxRef(executed));
                // 每条工具调用记一条 tool_call trace：input.name/args + status(ok/timeout/failed)
                foreach (var ex in executed)
                {
                    var status = ex.TryGetValue("timed_out", out var t) && t is true ? "timeout"
                                : !(ex.TryGetValue("ok", out var o) && o is true) ? "failed"
                                : "ok";
                    trace.Add("tool_call", status: status, ms: GetInt(ex, "ms"),
                        input: new() { ["name"] = GetStrOrNull(ex, "name") ?? "", ["args"] = ex.GetValueOrDefault("args") },
                        output: new() { ["ok"] = status == "ok", ["output"] = TruncateOutput(GetStr(ex, "output"), 120) });
                }
                // 合并 retrieval 的 trace_steps 进 recorder
                foreach (var ex in executed)
                {
                    if (ex.TryGetValue("structured", out var st) && st is Dictionary<string, object?> std
                        && std.TryGetValue("trace_steps", out var tsObj) && tsObj is IEnumerable<object?> stepsList)
                    {
                        var toMerge = new List<Dictionary<string, object?>>();
                        foreach (var s in stepsList)
                            if (s is Dictionary<string, object?> sd) toMerge.Add(sd);
                        trace.Merge(toMerge);
                    }
                }

                // 回填消息
                messages.Add(new ChatMessage(ChatRole.Assistant, [.. toolCalls]));
                foreach (var ex in executed)
                {
                    // OpenAI 要求 tool 消息带对应 tool_call_id：MEAI 用 FunctionResultContent.CallId 承载
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(GetStrOrNull(ex, "call_id") ?? string.Empty, GetStr(ex, "output"))]));
                }
            }

            // 轮次用尽 → 最后 json_mode 强制结构化
            var finalOpts = new ChatOptions
            {
                Temperature = (float?)_config.Temperature,
                MaxOutputTokens = _config.MaxTokens,
                ResponseFormat = ChatResponseFormat.Json,
            };
            var ft0 = Stopwatch.GetTimestamp();
            var final = await _chat.GetResponseAsync(messages, finalOpts, ctsLinked.Token);
            var ft1 = Stopwatch.GetTimestamp();
            trace.Add("llm_generate", ms: (int)((ft1 - ft0) * 1000 / (double)Stopwatch.Frequency),
                input: new() { ["round"] = _config.MaxRounds },
                output: new() { ["decision"] = "final" });
            var content2 = ExtractContent(final);
            if (string.IsNullOrEmpty(content2))
            {
                return await DegradeToRagAsync(query, toolTrace, "max_rounds_exhausted_empty_final", trace.Steps, ct);
            }
            return Finish(content2, _config.MaxRounds, toolTrace, sw, maxRef, grounded: HasGrounding(toolTrace), degraded: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 客户端断流/cancel 单独记日志，与总超时区分
            _log.LogInformation("[agent] 客户端取消");
            return await DegradeToRagAsync(query, toolTrace, "cancelled", trace.Steps, ct);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("[agent] 总超时 {Sec}s，降级纯 RAG", _config.TotalTimeoutSec);
            return await DegradeToRagAsync(query, toolTrace, "total_timeout", trace.Steps, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[agent] 异常，降级纯 RAG");
            return await DegradeToRagAsync(query, toolTrace, ex.Message, trace.Steps, ct);
        }
    }

    public IAsyncEnumerable<AgentEvent> RunStreamAsync(string query, CancellationToken ct = default)
    {
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(query))
            return EmptyStream();

        // Channel 生产者/消费者（见文件头注释）
        var channel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions { SingleReader = true });
        _ = StreamCoreAsync(query, channel.Writer, ct);
        return channel.Reader.ReadAllAsync(ct);
    }

    private static async IAsyncEnumerable<AgentEvent> EmptyStream()
    {
        var r = new AgentResult { Answer = "查询内容为空。", Route = RouteKind.Refused, Refused = true, RefusedReason = RefusalReason.EmptyQuery };
        yield return new AgentEvent { Kind = "done", Data = DoneData(r), Result = r };
    }

    private async Task StreamCoreAsync(string query, ChannelWriter<AgentEvent> writer, CancellationToken ct)
    {
        var trace = new TraceRecorder();
        var toolTrace = new List<Dictionary<string, object?>>();
        var sw = Stopwatch.StartNew();
        try
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, Prompts.SystemPrompt),
                new ChatMessage(ChatRole.User, query),
            };

            using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(_config.TotalTimeoutSec));
            using var ctsLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);

            int rounds = 0;
            int maxRef = 0;
            for (rounds = 1; rounds <= _config.MaxRounds; rounds++)
            {
                var opts = new ChatOptions
                {
                    Temperature = (float?)_config.Temperature,
                    MaxOutputTokens = _config.MaxTokens,
                    Tools = _registry.GetAllFunctions().ToList(),
                };

                var t0 = Stopwatch.GetTimestamp();
                var textAccum = new System.Text.StringBuilder();
                List<FunctionCallContent>? toolCalls = null;

                await foreach (var update in _chat.GetStreamingResponseAsync(messages, opts, ctsLinked.Token))
                {
                    if (update.Contents is not null)
                    {
                        foreach (var c in update.Contents)
                        {
                            if (c is TextContent tc)
                            {
                                textAccum.Append(tc.Text);
                                await writer.WriteAsync(new AgentEvent { Kind = "delta", Data = new() { ["text"] = tc.Text } }, ct);
                            }
                            else if (c is FunctionCallContent fcc)
                            {
                                toolCalls ??= new();
                                toolCalls.Add(fcc);
                            }
                        }
                    }
                }
                var t1 = Stopwatch.GetTimestamp();
                var ms = (int)((t1 - t0) * 1000 / (double)Stopwatch.Frequency);
                trace.Add("llm_generate", ms: ms,
                    input: new() { ["round"] = rounds },
                    output: new() { ["decision"] = toolCalls?.Count > 0 ? "tool_calls" : "final" });

                if (toolCalls is null || toolCalls.Count == 0)
                {
                    var content = textAccum.ToString();
                    var r0 = Finish(content, rounds, toolTrace, sw, maxRef, grounded: HasGrounding(toolTrace));
                    r0.TraceSteps = trace.Steps;
                    trace.Add("post_check", output: PostCheckSummary(r0));
                    await writer.WriteAsync(new AgentEvent { Kind = "done", Data = DoneData(r0), Result = r0 }, ct);
                    return;
                }

                // 工具执行
                var executed = await ExecuteToolCallsAsync(toolCalls, ctsLinked.Token);
                toolTrace.AddRange(executed);
                maxRef = Math.Max(maxRef, ComputeMaxRef(executed));
                trace.Add("tool_call", ms: executed.Sum(e => GetInt(e, "ms")),
                    output: new() { ["count"] = executed.Count });

                foreach (var ex in executed)
                {
                    await writer.WriteAsync(new AgentEvent { Kind = "tool", Data = ex }, ct);
                    if (ex.TryGetValue("structured", out var s) && s is Dictionary<string, object?> sd)
                    {
                        if (GetStrOrNull(ex, "name") == "retrieve_knowledge")
                            await writer.WriteAsync(new AgentEvent { Kind = "retrieval", Data = sd }, ct);
                    }
                    // 每条工具调用记一条 tool_call trace（status + name/args）
                    var status = ex.TryGetValue("timed_out", out var t) && t is true ? "timeout"
                                : !(ex.TryGetValue("ok", out var o) && o is true) ? "failed"
                                : "ok";
                    trace.Add("tool_call", status: status, ms: GetInt(ex, "ms"),
                        input: new() { ["name"] = GetStrOrNull(ex, "name") ?? "", ["args"] = ex.GetValueOrDefault("args") },
                        output: new() { ["ok"] = status == "ok", ["output"] = TruncateOutput(GetStr(ex, "output"), 120) });
                    // 合并 retrieval 的 trace_steps 进 recorder
                    if (ex.TryGetValue("structured", out var std2) && std2 is Dictionary<string, object?> stdDict
                        && stdDict.TryGetValue("trace_steps", out var tsObj2) && tsObj2 is IEnumerable<object?> stepsList2)
                    {
                        var toMerge = new List<Dictionary<string, object?>>();
                        foreach (var step in stepsList2)
                            if (step is Dictionary<string, object?> sd2) toMerge.Add(sd2);
                        trace.Merge(toMerge);
                    }
                }

                messages.Add(new ChatMessage(ChatRole.Assistant, [.. toolCalls]));
                foreach (var ex in executed)
                {
                    // OpenAI 要求 tool 消息带对应 tool_call_id：MEAI 用 FunctionResultContent.CallId 承载
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(GetStrOrNull(ex, "call_id") ?? string.Empty, GetStr(ex, "output"))]));
                }
            }

            // 轮次用尽 + json_mode
            var finalOpts = new ChatOptions
            {
                Temperature = (float?)_config.Temperature,
                MaxOutputTokens = _config.MaxTokens,
                ResponseFormat = ChatResponseFormat.Json,
            };
            var ft0 = Stopwatch.GetTimestamp();
            var finalText = new System.Text.StringBuilder();
            await foreach (var update in _chat.GetStreamingResponseAsync(messages, finalOpts, ctsLinked.Token))
            {
                if (update.Contents is not null)
                    foreach (var c in update.Contents)
                        if (c is TextContent tc)
                        {
                            finalText.Append(tc.Text);
                            await writer.WriteAsync(new AgentEvent { Kind = "delta", Data = new() { ["text"] = tc.Text } }, ct);
                        }
            }
            trace.Add("llm_generate", ms: (int)((Stopwatch.GetTimestamp() - ft0) * 1000 / (double)Stopwatch.Frequency));
            if (string.IsNullOrEmpty(finalText.ToString().Trim()))
            {
                var r = await DegradeToRagAsync(query, toolTrace, "max_rounds_exhausted_empty_final", trace.Steps, ct);
                await writer.WriteAsync(new AgentEvent { Kind = "done", Data = DoneData(r), Result = r }, CancellationToken.None);
                return;
            }
            var result = Finish(finalText.ToString(), _config.MaxRounds, toolTrace, sw, maxRef,
                grounded: HasGrounding(toolTrace), degraded: true);
            result.TraceSteps = trace.Steps;
            trace.Add("post_check", output: PostCheckSummary(result));
            await writer.WriteAsync(new AgentEvent { Kind = "done", Data = DoneData(result), Result = result }, CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 客户端断流/cancel 单独记日志，与总超时区分
            _log.LogInformation("[agent] 流式客户端取消");
            // 客户端断开：静默收尾（不写降级事件）
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("[agent] 流式总超时 {Sec}s，降级纯 RAG", _config.TotalTimeoutSec);
            try
            {
                var r = await DegradeToRagAsync(query, toolTrace, "total_timeout", trace.Steps, ct);
                await writer.WriteAsync(new AgentEvent { Kind = "done", Data = DoneData(r), Result = r }, CancellationToken.None);
            }
            catch
            {
                // 降级也失败时静默收尾
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[agent] 流式异常，降级纯 RAG");
            try
            {
                var r = await DegradeToRagAsync(query, toolTrace, ex.Message, trace.Steps, ct);
                await writer.WriteAsync(new AgentEvent { Kind = "done", Data = DoneData(r), Result = r }, CancellationToken.None);
            }
            catch
            {
                // 降级也失败时静默收尾
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    // ---- 私有辅助 ----

    private static List<FunctionCallContent> ExtractToolCalls(ChatResponse resp)
    {
        var result = new List<FunctionCallContent>();
        if (resp.Messages is null) return result;
        foreach (var msg in resp.Messages)
        {
            if (msg.Contents is null) continue;
            foreach (var c in msg.Contents)
            {
                if (c is FunctionCallContent fcc) result.Add(fcc);
            }
        }
        return result;
    }

    private static string ExtractContent(ChatResponse resp)
    {
        if (resp.Messages is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var msg in resp.Messages)
        {
            if (msg.Contents is null) continue;
            foreach (var c in msg.Contents)
            {
                if (c is TextContent tc) sb.Append(tc.Text);
            }
        }
        return sb.ToString();
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteToolCallsAsync(
        List<FunctionCallContent> toolCalls, CancellationToken ct)
    {
        // Task.WhenAll 并行；每工具 8s 超时
        using var perToolCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        perToolCts.CancelAfter(TimeSpan.FromSeconds(_config.ToolTimeoutSec));

        var tasks = toolCalls.Select(async tc =>
        {
            var args = new Dictionary<string, object?>();
            if (tc.Arguments is not null)
            {
                foreach (var kv in tc.Arguments) args[kv.Key] = kv.Value?.ToString();
            }
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await _tools.ExecuteAsync(tc.Name, args, perToolCts.Token);
                sw.Stop();
                return new Dictionary<string, object?>
                {
                    ["call_id"] = tc.CallId ?? Guid.NewGuid().ToString("N"),
                    ["name"] = result.Name,
                    ["args"] = result.Args,
                    ["output"] = result.Output,
                    ["ok"] = result.Ok,
                    ["ms"] = result.Ms > 0 ? result.Ms : (int)sw.ElapsedMilliseconds,
                    ["timed_out"] = false,
                    ["structured"] = result.Structured,
                };
            }
            catch (OperationCanceledException)
            {
                return new Dictionary<string, object?>
                {
                    ["call_id"] = tc.CallId ?? Guid.NewGuid().ToString("N"),
                    ["name"] = tc.Name,
                    ["args"] = args,
                    ["output"] = $"[工具 {tc.Name} 执行超时（>{_config.ToolTimeoutSec}s）]",
                    ["ok"] = false,
                    ["ms"] = (int)(_config.ToolTimeoutSec * 1000),
                    ["timed_out"] = true,
                    ["structured"] = null,
                };
            }
            catch (UnknownToolException ex)
            {
                return new Dictionary<string, object?>
                {
                    ["call_id"] = tc.CallId ?? Guid.NewGuid().ToString("N"),
                    ["name"] = tc.Name,
                    ["args"] = args,
                    ["output"] = $"[工具执行失败] {ex.Message}",
                    ["ok"] = false,
                    ["ms"] = (int)sw.ElapsedMilliseconds,
                    ["timed_out"] = false,
                    ["structured"] = null,
                };
            }
        }).ToList();

        return (await Task.WhenAll(tasks)).ToList();
    }

    private static bool HasGrounding(List<Dictionary<string, object?>> executed)
    {
        foreach (var ex in executed)
        {
            var name = GetStrOrNull(ex, "name");
            if (ex.TryGetValue("structured", out var s) && s is Dictionary<string, object?> sd)
            {
                if (name == "retrieve_knowledge" &&
                    sd.TryGetValue("topk", out var topk) && topk is IEnumerable<object?> list && list.Any() &&
                    !(sd.TryGetValue("refused", out var r) && r is true))
                    return true;
                if (name == "query_alarm_code" && sd.TryGetValue("exact", out var e) && e is not null)
                    return true;
                if (name == "query_device_history" &&
                    sd.TryGetValue("total", out var t) && t is long total && total > 0)
                    return true;
            }
        }
        return false;
    }

    private static int ComputeMaxRef(List<Dictionary<string, object?>> executed)
    {
        // maxRef 取各 topk 项 ref 字段最大值（不能用条数：稀疏编号如 [1,3,5] 会剥掉 5）
        var mx = 0;
        foreach (var ex in executed)
        {
            if (ex.TryGetValue("structured", out var s) && s is Dictionary<string, object?> sd &&
                sd.TryGetValue("topk", out var topk) && topk is IEnumerable<object?> list)
            {
                foreach (var item in list)
                {
                    if (item is not Dictionary<string, object?> itemDict) continue;
                    if (!itemDict.TryGetValue("ref", out var refVal) || refVal is null) continue;
                    if (int.TryParse(refVal.ToString(), out var n) && n > mx) mx = n;
                }
            }
        }
        return mx;
    }

    private AgentResult Finish(string content, int rounds, List<Dictionary<string, object?>> toolTrace,
        Stopwatch sw, int maxRef, bool grounded, bool degraded = false)
    {
        var analysis = OutputParser.ParseAnalysis(content);
        // 先拒答判定，后引用校验
        var (refused, reason) = OutputParser.DecideRefusal(grounded, analysis, content);
        if (refused)
        {
            return new AgentResult
            {
                Answer = Prompts.RefusalMessage,
                Route = RouteKind.Refused,
                Rounds = rounds,
                Degraded = degraded,
                ToolCalls = toolTrace,
                TotalMs = (int)sw.ElapsedMilliseconds,
                Refused = true,
                RefusedReason = reason,
                RawAnswer = content,
            };
        }
        if (analysis.HasContent)
        {
            analysis = OutputParser.ValidateCitations(analysis, maxRef);
            return new AgentResult
            {
                Answer = OutputParser.RenderAnalysis(analysis),
                Route = RouteKind.Agent,
                Rounds = rounds,
                Degraded = degraded,
                ToolCalls = toolTrace,
                TotalMs = (int)sw.ElapsedMilliseconds,
                Analysis = analysis,
                RawAnswer = content,
            };
        }
        return new AgentResult
        {
            Answer = content ?? string.Empty,
            Route = RouteKind.Agent,
            Rounds = rounds,
            Degraded = degraded,
            ToolCalls = toolTrace,
            TotalMs = (int)sw.ElapsedMilliseconds,
            RawAnswer = content,
        };
    }

    private async Task<AgentResult> DegradeToRagAsync(string query, List<Dictionary<string, object?>> toolTrace,
        string error, List<Dictionary<string, object?>>? existingSteps, CancellationToken ct)
    {
        if (_retrieval is null)
        {
            return new AgentResult
            {
                Answer = "抱歉，服务暂时不可用（检索与生成均失败），请稍后重试或联系设备工程师。",
                Route = RouteKind.RagFallback,
                Degraded = true,
                ToolCalls = toolTrace,
                Error = error,
            };
        }
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.ToolTimeoutSec));
            var result = await _retrieval.RunQueryAsync(query, null, cts.Token);
            if (result.Refused || result.Topk.Count == 0)
            {
                return new AgentResult
                {
                    Answer = Prompts.RefusalMessage,
                    Route = RouteKind.RagFallback,
                    Degraded = true,
                    ToolCalls = toolTrace,
                    Refused = true,
                    RefusedReason = $"rag_fallback:{result.RefusedReason ?? "no_candidates"}",
                    Error = error,
                    TraceSteps = result.TraceSteps,
                    TotalMs = (int)sw.ElapsedMilliseconds,
                };
            }
            var answer = RenderRetrievalAsAnswer(result);
            return new AgentResult
            {
                Answer = answer,
                Route = RouteKind.RagFallback,
                Degraded = true,
                ToolCalls = toolTrace,
                Error = error,
                TraceSteps = result.TraceSteps,
                TotalMs = (int)sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[agent] RAG 降级也失败");
            return new AgentResult
            {
                Answer = "抱歉，检索超时，请稍后重试或联系设备工程师。",
                Route = RouteKind.RagFallback,
                Degraded = true,
                ToolCalls = toolTrace,
                Error = error,
            };
        }
    }

    private static string RenderRetrievalAsAnswer(QueryResult result)
    {
        var lines = new List<string>();
        if (result.Topk.Count == 0) return "未找到相关知识。";
        lines.Add("📚 知识库检索结果（降级路径，无 LLM 结构化分析）：");
        for (var i = 0; i < result.Topk.Count; i++)
        {
            var h = result.Topk[i];
            lines.Add($"[{i + 1}] {h.Title}（{h.Source}）");
            lines.Add($"    {h.Content}");
        }
        return string.Join("\n", lines);
    }

    private static Dictionary<string, object?> PostCheckSummary(AgentResult r) => new()
    {
        ["refused"] = r.Refused,
        ["refused_reason"] = r.RefusedReason,
        ["causes"] = r.Analysis?.PossibleCauses.Count ?? 0,
        ["steps"] = r.Analysis?.TroubleshootingSteps.Count ?? 0,
        ["need_expert"] = r.Analysis?.NeedExpert ?? false,
    };

    private static Dictionary<string, object?> DoneData(AgentResult r) => new()
    {
        ["trace_id"] = r.TraceId.ToString(),
        ["route"] = r.Route,
        ["refused"] = r.Refused,
        ["refused_reason"] = r.RefusedReason,
        ["answer"] = r.Answer,
        ["rounds"] = r.Rounds,
        ["degraded"] = r.Degraded,
        ["total_ms"] = r.TotalMs,
        ["analysis"] = r.Analysis?.ToDict(),
        ["tool_calls"] = r.ToolCalls,
        ["trace_steps"] = r.TraceSteps,
    };

    // ---- 字典取值辅助 ----

    private static string GetStr(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static string? GetStrOrNull(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int GetInt(Dictionary<string, object?> d, string key)
        => d.TryGetValue(key, out var v) && v is int i ? i : 0;

    private static string TruncateOutput(string s, int max)
        => s.Length > max ? s[..max] : s;
}
