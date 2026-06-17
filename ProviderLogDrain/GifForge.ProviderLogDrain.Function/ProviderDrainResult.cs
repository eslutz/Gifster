using System.Net;

namespace GifForge.ProviderLogDrain.Function;

public sealed record ProviderDrainResult(HttpStatusCode StatusCode, string Body);
