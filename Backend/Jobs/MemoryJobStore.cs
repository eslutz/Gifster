using System.Collections.Concurrent;
using Gifster.Backend.Models;
using Gifster.Backend.Providers;

namespace Gifster.Backend.Jobs;

public sealed class MemoryJobStore : IJobStore
{
  private readonly ConcurrentDictionary<string, GenerationJob> jobs = new();
  private readonly TimeSpan queuedDuration = TimeSpan.FromMilliseconds(250);
  private readonly TimeSpan completeDuration = TimeSpan.FromMilliseconds(800);

  public Task<GenerationJob> CreateAsync(
    GenerationRequest request,
    ProviderJob providerJob,
    CancellationToken cancellationToken
  )
  {
    var job = GenerationJob.Create(request, providerJob);

    jobs[job.Id] = job;
    return Task.FromResult(job);
  }

  public Task<GenerationJob?> GetAsync(string id, CancellationToken cancellationToken)
  {
    jobs.TryGetValue(id, out var job);
    return Task.FromResult(job);
  }

  public Task SaveAsync(GenerationJob job, CancellationToken cancellationToken)
  {
    jobs[job.Id] = job;
    return Task.CompletedTask;
  }

  public GenerationJobStatus StatusFor(GenerationJob job)
  {
    if (!string.IsNullOrWhiteSpace(job.FailedMessage))
    {
      return GenerationJobStatus.Failed;
    }

    if (job.Status is GenerationJobStatus.Succeeded or GenerationJobStatus.Failed)
    {
      return job.Status;
    }

    var age = DateTimeOffset.UtcNow - job.CreatedAt;
    if (age < queuedDuration)
    {
      return GenerationJobStatus.Queued;
    }

    return age < completeDuration
      ? GenerationJobStatus.Running
      : GenerationJobStatus.Succeeded;
  }
}
