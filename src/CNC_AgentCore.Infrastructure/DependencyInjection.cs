// Infrastructure/DependencyInjection.cs —— 应用服务装配扩展方法
//
// 生命周期约定：无状态/池对象（NpgsqlDataSource、Chat/Embedding/Rerank、鉴权服务）及仓储 → Singleton；
// 检索链路 + 工具 + Router 走 Request 级连接状态 → Scoped（防 singleton 持有 scoped 依赖）。
using CNC_AgentCore.Application.Agent;
using CNC_AgentCore.Application.Agent.Tools;
using CNC_AgentCore.Application.Auth;
using CNC_AgentCore.Application.BaseItems;
using CNC_AgentCore.Application.Devices;
using CNC_AgentCore.Application.Import;
using CNC_AgentCore.Application.Knowledge;
using CNC_AgentCore.Application.Retrieval;
using CNC_AgentCore.Application.Stats;
using CNC_AgentCore.Application.Vectorizer;
using CNC_AgentCore.Application.Vectors;
using CNC_AgentCore.Application.WorkOrders;
using CNC_AgentCore.Domain.Abstractions;
using CNC_AgentCore.Infrastructure.HealthChecks;
using CNC_AgentCore.Infrastructure.Persistence;
using CNC_AgentCore.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;

namespace CNC_AgentCore.Infrastructure;

public static class DependencyInjection
{
    /// <summary>数据库：NpgsqlDataSource（含 pgvector）+ EF Core DbContext（三 schema）</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
    {
        var connStr = config["PG_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException("PG_CONNECTION_STRING 未配置（.env 或环境变量）");

        var dsBuilder = new NpgsqlDataSourceBuilder(connStr);
        dsBuilder.UseVector();
        var ds = dsBuilder.Build();
        services.AddSingleton(ds);
        services.AddDbContext<CncKbDbContext>(o => o.UseNpgsql(ds));
        return services;
    }

    /// <summary>中文分词（双字切分 + user dict 的 SimpleTokenizer）</summary>
    public static IServiceCollection AddJiebaTokenization(this IServiceCollection services)
    {
        services.AddSingleton<ITokenizer, SimpleTokenizer>();
        return services;
    }

    /// <summary>LLM/Embedding/Rerank 三 Provider（DeepSeek + 硅基流动）</summary>
    public static IServiceCollection AddLlmProviders(this IServiceCollection services, IConfiguration config)
    {
        var dsOpts = DeepSeekOptions.Load(config);
        var sfOpts = SiliconFlowOptions.Load(config);
        services.AddSingleton(dsOpts);
        services.AddSingleton(sfOpts);

        // 注：ApiKey 缺失时工厂在首次 Resolve 才抛（health check 会先判定 skipped）
        services.AddSingleton<IChatClient>(sp =>
            DeepSeekChatClient.Create(sp.GetRequiredService<DeepSeekOptions>()));
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            new SiliconFlowEmbeddingGenerator(sp.GetRequiredService<SiliconFlowOptions>()));
        services.AddSingleton<IRerankClient>(sp =>
            new SiliconFlowRerankClient(sp.GetRequiredService<SiliconFlowOptions>()));
        return services;
    }

    /// <summary>Agent、检索、鉴权及业务仓储注册</summary>
    public static IServiceCollection AddAgentServices(this IServiceCollection services, IConfiguration config)
    {
        // 检索链路（scoped：内部走 Request 级连接）
        services.AddScoped<CodeExtractor>();
        services.AddScoped<VectorSearch>();
        services.AddScoped<FulltextSearch>();
        services.AddScoped<IRetrievalService, RetrievalService>();
        services.AddSingleton(new RetrievalServiceConfig
        {
            RerankThreshold = double.TryParse(config["RERANK_THRESHOLD"], out var t) ? t : 0.30,
        });

        // 鉴权（无状态 → singleton）
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IRolePermissionRepository, RolePermissionRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IQueryLogRepository, QueryLogRepository>();
        services.AddSingleton<IFeedbackRepository, FeedbackRepository>();
        services.AddSingleton<ISuggestionRepository, SuggestionRepository>();
        services.AddSingleton<IKnowledgeEntryRepository, KnowledgeEntryRepository>();
        services.AddSingleton<IImportJobRepository, ImportJobRepository>();
        services.AddSingleton<IStatsRepository, StatsRepository>();
        services.AddSingleton<IVectorRepository, VectorRepository>();
        services.AddSingleton<IVectorizeService, VectorizeService>();
        services.AddSingleton<IWorkOrderRepository, WorkOrderRepository>();
        services.AddSingleton<IDeviceRepository, DeviceRepository>();
        services.AddSingleton<IBaseItemRepository, BaseItemRepository>();

        // 受限工具 + Router（scoped）
        services.AddScoped<IToolHandler, RetrieveKnowledgeTool>();
        services.AddScoped<IToolHandler, QueryAlarmCodeTool>();
        services.AddScoped<IToolHandler, QueryDeviceHistoryTool>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<IToolExecutor>(sp => sp.GetRequiredService<ToolRegistry>());
        services.AddScoped<IAgentRouter, Router>();
        services.AddSingleton(new AgentConfig());
        return services;
    }

    /// <summary>健康检查（db / llm / embedding / rerank 四项探测）</summary>
    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("db")
            .AddCheck<LlmHealthCheck>("llm")
            .AddCheck<EmbeddingHealthCheck>("embedding")
            .AddCheck<RerankHealthCheck>("rerank");
        return services;
    }
}
