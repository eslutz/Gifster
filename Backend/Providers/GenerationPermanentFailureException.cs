namespace Gifster.Backend.Providers;

public sealed class GenerationPermanentFailureException : Exception
{
  public GenerationPermanentFailureException(string message)
    : base(message)
  {
  }

  public GenerationPermanentFailureException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
