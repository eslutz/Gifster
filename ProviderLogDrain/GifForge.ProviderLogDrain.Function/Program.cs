using Azure.Identity;
using GifForge.ProviderLogDrain.Function;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
  .ConfigureFunctionsWorkerDefaults()
  .ConfigureServices((context, services) =>
  {
    services.AddSingleton(ProviderDrainOptions.FromConfiguration(context.Configuration));
    services.AddSingleton<ProviderDrainHandler>();
    services.AddSingleton<IProviderLogIngestionSink>(serviceProvider =>
    {
      var options = serviceProvider.GetRequiredService<ProviderDrainOptions>();
      return new AzureMonitorProviderLogIngestionSink(options, new DefaultAzureCredential());
    });
  })
  .Build();

host.Run();
