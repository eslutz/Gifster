using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Gifster.Backend.Configuration;
using Gifster.Backend.Jobs;
using Gifster.Backend.Models;
using Gifster.Backend.Providers;
using Gifster.Backend.Queueing;
using Gifster.Backend.Safety;
using Gifster.Backend.Security;
using Gifster.Backend.Storage;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")))
{
  Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", "8787");
}

var app = GifsterBackendApp.Create(args);
app.Run();

public static class GifsterBackendApp
{
  public static WebApplication Create(
    string[]? args = null,
    IGenerationProvider? provider = null,
    IJobStore? jobStore = null,
    IGenerationJobDispatcher? jobDispatcher = null,
    IAppAttestVerifier? appAttestVerifier = null,
    string? publicBaseUrl = null,
    IAppAttestStateStore? appAttestStateStore = null
  )
  {
    var builder = WebApplication.CreateSlimBuilder(args ?? []);

    builder.Services.ConfigureHttpJsonOptions(ConfigureJson);
    builder.Services.AddSingleton(provider ?? CreateGenerationProvider(builder.Configuration));
    builder.Services.AddSingleton(jobStore ?? CreateJobStore(builder.Configuration));
    builder.Services.AddSingleton(jobDispatcher ?? CreateJobDispatcher(builder.Configuration));
    builder.Services.AddSingleton(CreateResultStore(builder.Configuration));
    var appAttestOptions = AppAttestOptions.FromConfiguration(builder.Configuration);
    builder.Services.AddSingleton<IAppAttestService>(
      new AppAttestService(
        appAttestOptions,
        appAttestVerifier ?? CreateAppAttestVerifier(appAttestOptions),
        appAttestStateStore ?? CreateAppAttestStateStore(builder.Configuration)
      )
    );
    ConfigureWorkerServices(builder);
    builder.Services.AddSingleton(new BackendOptions(
      publicBaseUrl ?? builder.Configuration["GIFSTER_PUBLIC_BASE_URL"]
    ));

    var app = builder.Build();
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
      ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
      ForwardLimit = 1
    };
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);

    MapRoutes(app);
    return app;
  }

  private static void ConfigureJson(JsonOptions options)
  {
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, GifsterJsonSerializerContext.Default);
  }

  private static IGenerationProvider CreateGenerationProvider(IConfiguration configuration)
  {
    var adapter = configuration["GIFSTER_PROVIDER_ADAPTER"]?.Trim().ToLowerInvariant();
    return adapter switch
    {
      null or "" or "fake" or "fake-frame-sequence" => new FakeFrameSequenceProvider(),
      "external-http" => new ExternalHttpGenerationProvider(
        ExternalHttpProviderOptions.FromConfiguration(configuration),
        new HttpClient()
      ),
      _ => throw new InvalidOperationException($"Unsupported generation provider adapter '{adapter}'.")
    };
  }

  private static IJobStore CreateJobStore(IConfiguration configuration)
  {
    var storage = BackendStorageOptions.FromConfiguration(configuration);
    if (!storage.IsConfigured)
    {
      return new MemoryJobStore();
    }

    PreserveAzureTableEntityConstructors();

    var credentialOptions = new DefaultAzureCredentialOptions
    {
      ManagedIdentityClientId = storage.ManagedIdentityClientId
    };
    var credential = new DefaultAzureCredential(credentialOptions);
    var tableEndpoint = new Uri($"https://{storage.StorageAccountName}.table.core.windows.net");
    var tableClient = new TableClient(tableEndpoint, storage.JobsTableName, credential);

    return new TableGenerationJobStore(new AzureGenerationJobTable(tableClient));
  }

  [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(TableEntity))]
  private static void PreserveAzureTableEntityConstructors()
  {
  }

  private static IGenerationResultStore CreateResultStore(IConfiguration configuration)
  {
    var storage = BackendStorageOptions.FromConfiguration(configuration);
    if (!storage.IsConfigured)
    {
      return new MemoryGenerationResultStore();
    }

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
      ManagedIdentityClientId = storage.ManagedIdentityClientId
    });
    var blobEndpoint = new Uri($"https://{storage.StorageAccountName}.blob.core.windows.net");
    var serviceClient = new BlobServiceClient(blobEndpoint, credential);
    return new AzureBlobGenerationResultStore(serviceClient.GetBlobContainerClient(storage.ResultsContainerName));
  }

  private static IGenerationJobDispatcher CreateJobDispatcher(IConfiguration configuration)
  {
    var storage = BackendStorageOptions.FromConfiguration(configuration);
    if (!storage.IsConfigured)
    {
      return new NoopGenerationJobDispatcher();
    }

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
      ManagedIdentityClientId = storage.ManagedIdentityClientId
    });
    var queueUri = new Uri(
      $"https://{storage.StorageAccountName}.queue.core.windows.net/{storage.GenerationQueueName}"
    );
    var queueClient = new QueueClient(
      queueUri,
      credential,
      new QueueClientOptions
      {
        MessageEncoding = QueueMessageEncoding.Base64
      }
    );

    return new AzureQueueGenerationJobDispatcher(queueClient);
  }

  private static IAppAttestStateStore CreateAppAttestStateStore(IConfiguration configuration)
  {
    var storage = BackendStorageOptions.FromConfiguration(configuration);
    if (!storage.IsConfigured)
    {
      return new MemoryAppAttestStateStore();
    }

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
      ManagedIdentityClientId = storage.ManagedIdentityClientId
    });
    var tableEndpoint = new Uri($"https://{storage.StorageAccountName}.table.core.windows.net");
    var tableClient = new TableClient(tableEndpoint, storage.AppAttestStateTableName, credential);

    return new AzureTableAppAttestStateStore(tableClient);
  }

  private static IAppAttestVerifier CreateAppAttestVerifier(AppAttestOptions options)
  {
    if (string.IsNullOrWhiteSpace(options.AppIdentifier) ||
        string.IsNullOrWhiteSpace(options.RootCertificatePem))
    {
      return new UnavailableAppAttestVerifier();
    }

    try
    {
      var rootCertificate = X509Certificate2.CreateFromPem(options.RootCertificatePem);
      return new AppleAppAttestVerifier(options.AppIdentifier, rootCertificate);
    }
    catch (Exception error) when (
      error is CryptographicException or ArgumentException or InvalidOperationException
    )
    {
      return new UnavailableAppAttestVerifier();
    }
  }

  private static void ConfigureWorkerServices(WebApplicationBuilder builder)
  {
    if (!string.Equals(builder.Configuration["GIFSTER_WORKER_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    var storage = BackendStorageOptions.FromConfiguration(builder.Configuration);
    if (!storage.IsConfigured)
    {
      throw new InvalidOperationException("GIFSTER_WORKER_ENABLED requires GIFSTER_STORAGE_ACCOUNT_NAME.");
    }

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
      ManagedIdentityClientId = storage.ManagedIdentityClientId
    });
    var queueUri = new Uri(
      $"https://{storage.StorageAccountName}.queue.core.windows.net/{storage.GenerationQueueName}"
    );
    var queueClient = new QueueClient(
      queueUri,
      credential,
      new QueueClientOptions
      {
        MessageEncoding = QueueMessageEncoding.Base64
      }
    );

    builder.Services.AddSingleton<IGenerationJobQueueReader>(new AzureQueueGenerationJobReader(queueClient));
    builder.Services.AddSingleton<GenerationWorker>();
    builder.Services.AddHostedService<GenerationQueueWorkerService>();
  }

  private static void MapRoutes(WebApplication app)
  {
    app.MapGet("/health", (IGenerationProvider provider) =>
      Json(new HealthResponse(true, provider.Name, "demo"), GifsterJsonSerializerContext.Default.HealthResponse));

    app.MapPost("/v1/app-attest/challenges", async (
      IAppAttestService appAttest,
      HttpContext context
    ) =>
      Json(
        await appAttest.CreateChallengeAsync(context.RequestAborted),
        GifsterJsonSerializerContext.Default.AppAttestChallengeResponse
      ));

    app.MapPost("/v1/app-attest/attestations", async (
      AppAttestAttestationRequest request,
      IAppAttestService appAttest,
      HttpContext context
    ) =>
    {
      var session = await appAttest.CreateSessionAsync(request, context.RequestAborted);
      return session is null
        ? Error(StatusCodes.Status401Unauthorized, "App Attest challenge could not be verified.")
        : Json(session, GifsterJsonSerializerContext.Default.AppAttestSessionResponse);
    });

    app.MapPost("/v1/generations", async (
      GenerationRequest request,
      HttpContext context,
      IGenerationProvider provider,
      IJobStore jobStore,
      IGenerationJobDispatcher jobDispatcher,
      IAppAttestService appAttest,
      BackendOptions options
    ) =>
    {
      if (!await appAttest.IsAuthorizedAsync(context, context.RequestAborted))
      {
        return Error(StatusCodes.Status401Unauthorized, "App Attest authorization is required.");
      }

      var validation = ModerationPolicy.Validate(request);
      if (!validation.IsValid)
      {
        return Error(validation.StatusCode, validation.Message);
      }

      var providerJob = await provider.SubmitGenerationAsync(request, context.RequestAborted);
      var job = await jobStore.CreateAsync(request, providerJob, context.RequestAborted);
      await jobDispatcher.DispatchAsync(job, context.RequestAborted);
      var statusUrl = $"{RequestBaseUrl(context, options)}/v1/generations/{job.Id}";

      return Json(
        new JobSubmissionResponse(job.Id, "queued", statusUrl),
        GifsterJsonSerializerContext.Default.JobSubmissionResponse,
        statusCode: StatusCodes.Status202Accepted
      );
    });

    app.MapGet("/v1/generations/{jobId}", async (
      string jobId,
      HttpContext context,
      IJobStore jobStore,
      IAppAttestService appAttest,
      BackendOptions options
    ) =>
    {
      if (!await appAttest.IsAuthorizedAsync(context, context.RequestAborted))
      {
        return Error(StatusCodes.Status401Unauthorized, "App Attest authorization is required.");
      }

      var job = await jobStore.GetAsync(jobId, context.RequestAborted);
      if (job is null)
      {
        return Error(StatusCodes.Status404NotFound, "Generation job was not found.");
      }

      var status = jobStore.StatusFor(job);
      var downloadUrl = status == GenerationJobStatus.Succeeded
        ? $"{RequestBaseUrl(context, options)}/v1/generations/{job.Id}/result"
        : null;

      return Json(
        new JobStatusResponse(job.Id, status.JsonValue(), downloadUrl, status == GenerationJobStatus.Failed ? job.FailedMessage : null),
        GifsterJsonSerializerContext.Default.JobStatusResponse
      );
    });

    app.MapGet("/v1/generations/{jobId}/result", async (
      string jobId,
      IGenerationProvider provider,
      IJobStore jobStore,
      IGenerationResultStore resultStore,
      IAppAttestService appAttest,
      HttpContext context,
      CancellationToken cancellationToken
    ) =>
    {
      if (!await appAttest.IsAuthorizedAsync(context, cancellationToken))
      {
        return Error(StatusCodes.Status401Unauthorized, "App Attest authorization is required.");
      }

      var job = await jobStore.GetAsync(jobId, cancellationToken);
      if (job is null)
      {
        return Error(StatusCodes.Status404NotFound, "Generation job was not found.");
      }

      if (jobStore.StatusFor(job) != GenerationJobStatus.Succeeded)
      {
        return Error(StatusCodes.Status409Conflict, "Generation result is not ready.");
      }

      var result = string.IsNullOrWhiteSpace(job.ResultBlobName)
        ? await provider.GetResultAsync(job, cancellationToken)
        : await resultStore.ReadAsync(job, cancellationToken);

      return Results.Bytes(result.Bytes, result.ContentType);
    });
  }

  private static IResult Json<T>(T value, JsonTypeInfo<T> jsonTypeInfo, int? statusCode = null, string? contentType = null) =>
    Results.Json(value, jsonTypeInfo, contentType: contentType, statusCode: statusCode);

  private static IResult Error(int statusCode, string message) =>
    Json(
      new ErrorResponse(statusCode >= 500 ? "internal_error" : "invalid_request", message),
      GifsterJsonSerializerContext.Default.ErrorResponse,
      statusCode: statusCode
    );

  private static string RequestBaseUrl(HttpContext context, BackendOptions options)
  {
    if (!string.IsNullOrWhiteSpace(options.PublicBaseUrl))
    {
      return options.PublicBaseUrl.TrimEnd('/');
    }

    return $"{context.Request.Scheme}://{context.Request.Host}";
  }
}

public sealed record BackendOptions(string? PublicBaseUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(GenerationRequest))]
[JsonSerializable(typeof(JobSubmissionResponse))]
[JsonSerializable(typeof(JobStatusResponse))]
[JsonSerializable(typeof(FrameSequenceAsset))]
[JsonSerializable(typeof(FrameSpec))]
[JsonSerializable(typeof(GenerationQueueMessage))]
[JsonSerializable(typeof(AppAttestChallengeResponse))]
[JsonSerializable(typeof(AppAttestAttestationRequest))]
[JsonSerializable(typeof(AppAttestSessionResponse))]
internal partial class GifsterJsonSerializerContext : JsonSerializerContext;

public sealed record HealthResponse(bool Ok, string Provider, string Mode);
public sealed record ErrorResponse(string Error, string Message);

public sealed record JobSubmissionResponse(string JobId, string Status, string StatusUrl);
public sealed record JobStatusResponse(string JobId, string Status, string? DownloadUrl, string? Message);
