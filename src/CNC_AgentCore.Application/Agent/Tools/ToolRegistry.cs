// Application/Agent/Tools/ToolRegistry.cs —— 工具统一注册 + 分发
using System.Text.Json;
using CNC_AgentCore.Domain.Abstractions;
using Microsoft.Extensions.AI;

namespace CNC_AgentCore.Application.Agent.Tools;

public sealed class ToolRegistry : IToolExecutor
{
    private readonly Dictionary<string, IToolHandler> _handlers;
    private readonly IReadOnlyList<ToolSpec> _specs;

    public ToolRegistry(IEnumerable<IToolHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Spec.Name);
        _specs = _handlers.Values.Select(h => h.Spec).ToList();
    }

    public IReadOnlyList<ToolSpec> GetAllToolSpecs() => _specs;

    public IReadOnlyList<Dictionary<string, object?>> GetAllToolSchemas()
        => _specs.Select(s => s.ToOpenAISchema()).ToList();

    /// <summary>把工具 schema 转成 MEAI AITool 供 ChatOptions.Tools。
    /// 用不可调用的 AIFunctionDeclaration：Router 自行解析 tool_calls 后调 ExecuteAsync。</summary>
    public IReadOnlyList<AITool> GetAllFunctions()
    {
        return _specs.Select(spec => (AITool)AIFunctionFactory.CreateDeclaration(
            spec.Name,
            spec.Description,
            GetParametersJson(spec),
            null)).ToList();
    }

    private static JsonElement GetParametersJson(ToolSpec spec)
    {
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = spec.Parameters,
            ["required"] = spec.Required,
        };
        return JsonSerializer.SerializeToElement(schema);
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
            throw new UnknownToolException($"unknown tool: '{toolName}'; known: [{string.Join(", ", _handlers.Keys)}]");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var (output, structured) = await handler.ExecuteAsync(args, ct);
            sw.Stop();
            return new ToolResult
            {
                Name = toolName,
                Args = args.ToDictionary(kv => kv.Key, kv => kv.Value),
                Output = output,
                Ok = true,
                Ms = (int)sw.ElapsedMilliseconds,
                Structured = structured,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolResult
            {
                Name = toolName,
                Args = args.ToDictionary(kv => kv.Key, kv => kv.Value),
                Output = $"[工具执行失败] {ex.Message}",
                Ok = false,
                Ms = (int)sw.ElapsedMilliseconds,
                Structured = null,
            };
        }
    }
}

public sealed class UnknownToolException : Exception
{
    public UnknownToolException(string message) : base(message) { }
}
