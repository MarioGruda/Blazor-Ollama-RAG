﻿using Codeblaze.SemanticKernel.Connectors.Ollama;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Plugins.Memory;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0020
#pragma warning disable SKEXP0050

namespace Gruda.RagBot.Kernel;

public static class KernelExtensions
{
	public static void AddBrainKernel(this IHostApplicationBuilder builder)
	{
		builder.Services.AddOptions<KernelOptions>()
			.BindConfiguration(nameof(KernelOptions))
			.ValidateDataAnnotations()
			.ValidateOnStart();

		var options = builder.Configuration.GetSection(nameof(KernelOptions)).Get<KernelOptions>()!;

		var kernelBuilder = Microsoft.SemanticKernel.Kernel.CreateBuilder();
		kernelBuilder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Information));
		kernelBuilder.Services.ConfigureHttpClientDefaults(c =>
		{
			c.AddStandardResilienceHandler(x =>
			{
				x.CircuitBreaker = new HttpCircuitBreakerStrategyOptions()
				{
					SamplingDuration = TimeSpan.FromMinutes(4),
				};
				x.AttemptTimeout = new HttpTimeoutStrategyOptions() { Timeout = TimeSpan.FromMinutes(2) };
				x.TotalRequestTimeout = new HttpTimeoutStrategyOptions() { Timeout = TimeSpan.FromMinutes(5) };
			});
		});

		string ollamaEndpoint = builder.Configuration.GetConnectionString(Constants.OllamaConnectionString)!;
		kernelBuilder
			.AddOllamaChatCompletion(options.ChatModelId, ollamaEndpoint)
			.AddOllamaTextEmbeddingGeneration(options.TextEmbeddingModelId, ollamaEndpoint);

		string[] qdrantEndpoint = builder.Configuration.GetConnectionString(Constants.QdrantHttpConnectionString)!.Split(";");
		string qdrantUrl = qdrantEndpoint[0]["Endpoint=".Length..];
		string qdrantApiKey = qdrantEndpoint[1]["Key=".Length..];

		HttpClient qdrantClient = new()
		{
			BaseAddress = new Uri(qdrantUrl), DefaultRequestHeaders = { { "api-key", qdrantApiKey } }
		};

		var memory = new MemoryBuilder()
			.WithMemoryStore(new QdrantMemoryStore(qdrantClient, options.TextEmbeddingVectorSize))
			.WithOllamaTextEmbeddingGeneration(options.TextEmbeddingModelId, ollamaEndpoint)
			.WithHttpClient(qdrantClient)
			.Build();

		kernelBuilder.Services.AddSingleton(memory);

		var kernel = kernelBuilder.Build();
		kernel.ImportPluginFromObject(new TextMemoryPlugin(memory));

		builder.Services.AddSingleton(kernel);
		builder.Services.AddSingleton<KernelService>();
	}
}