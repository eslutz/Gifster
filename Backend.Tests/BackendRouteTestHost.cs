using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Gifster.Backend.Tests;

internal static class BackendRouteTestHost
{
  public static async Task<Uri> StartAsync(WebApplication app)
  {
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();

    var addresses = app.Services
      .GetRequiredService<IServer>()
      .Features
      .Get<IServerAddressesFeature>()?
      .Addresses;

    var address = Assert.Single(addresses ?? []);
    return new Uri(address);
  }
}
