// Infrastructure/Providers/DeepSeekChatClient.cs —— DeepSeek IChatClient 适配
// DeepSeek 为 OpenAI 兼容 API：OpenAIClient.GetChatClient(model).AsIChatClient() 即得 IChatClient。
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace CNC_AgentCore.Infrastructure.Providers;

public static class DeepSeekChatClient
{
    public static IChatClient Create(DeepSeekOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new ArgumentException("DeepSeek api_key 未配置", nameof(opts));

        var client = new OpenAIClient(
            new ApiKeyCredential(opts.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(opts.BaseUrl) });
        return client.GetChatClient(opts.Model).AsIChatClient();
    }
}
