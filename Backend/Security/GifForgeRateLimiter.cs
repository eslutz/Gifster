using System.Collections.Concurrent;

namespace GifForge.Backend.Security;

public sealed class GifForgeRateLimiter
{
  private readonly AccountSecurityOptions options;
  private readonly ConcurrentDictionary<string, Bucket> buckets = new(StringComparer.Ordinal);

  public GifForgeRateLimiter(AccountSecurityOptions options)
  {
    this.options = options;
  }

  public RateLimitDecision CheckAuth(HttpContext context) =>
    Check(context, "auth", options.AuthRateLimitMax, null);

  public RateLimitDecision CheckAppAttest(HttpContext context) =>
    Check(context, "app-attest", options.AppAttestRateLimitMax, null);

  public RateLimitDecision CheckPurchase(HttpContext context, AuthenticatedUser user) =>
    Check(context, "purchase", options.PurchaseRateLimitMax, user.UserId.ToString("D"));

  public RateLimitDecision CheckGeneration(HttpContext context, AuthenticatedUser? user) =>
    Check(context, "generation", options.GenerationRateLimitMax, user?.UserId.ToString("D"));

  public RateLimitDecision CheckGenerationStatus(HttpContext context, AuthenticatedUser? user) =>
    Check(context, "generation-status", options.GenerationStatusRateLimitMax, user?.UserId.ToString("D"));

  private RateLimitDecision Check(
    HttpContext context,
    string area,
    int maxRequests,
    string? userId
  )
  {
    var now = DateTimeOffset.UtcNow;
    var retryAfter = TimeSpan.Zero;

    foreach (var key in Keys(context, area, userId))
    {
      var decision = Consume(key, maxRequests, options.RateLimitWindow, now);
      if (!decision.Allowed && decision.RetryAfter > retryAfter)
      {
        retryAfter = decision.RetryAfter;
      }
    }

    return retryAfter == TimeSpan.Zero
      ? RateLimitDecision.Pass
      : new RateLimitDecision(false, retryAfter);
  }

  private RateLimitDecision Consume(
    string key,
    int maxRequests,
    TimeSpan window,
    DateTimeOffset now
  )
  {
    while (true)
    {
      var bucket = buckets.GetOrAdd(key, _ => new Bucket(now.Add(window), 0));
      if (bucket.ResetAt <= now)
      {
        var reset = new Bucket(now.Add(window), 1);
        if (buckets.TryUpdate(key, reset, bucket))
        {
          return RateLimitDecision.Pass;
        }

        continue;
      }

      if (bucket.Count >= maxRequests)
      {
        return new RateLimitDecision(false, bucket.ResetAt - now);
      }

      var updated = bucket with { Count = bucket.Count + 1 };
      if (buckets.TryUpdate(key, updated, bucket))
      {
        return RateLimitDecision.Pass;
      }
    }
  }

  private static IEnumerable<string> Keys(HttpContext context, string area, string? userId)
  {
    var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    yield return $"{area}:ip:{remoteAddress}";

    if (!string.IsNullOrWhiteSpace(userId))
    {
      yield return $"{area}:user:{userId}";
    }

    var appAttestSession = context.Request.Headers["X-GifForge-App-Attest-Session"].ToString();
    if (!string.IsNullOrWhiteSpace(appAttestSession))
    {
      yield return $"{area}:app-attest:{SecurityTokenHelpers.Sha256Base64Url(appAttestSession)}";
    }
  }

  private sealed record Bucket(DateTimeOffset ResetAt, int Count);
}

public sealed record RateLimitDecision(bool Allowed, TimeSpan RetryAfter)
{
  public static RateLimitDecision Pass { get; } = new(true, TimeSpan.Zero);
}
