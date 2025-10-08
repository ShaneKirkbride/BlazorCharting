using EquipmentHubDemo.Client;
using EquipmentHubDemo.Client.Services;
using EquipmentHubDemo.Domain.Live;
using EquipmentHubDemo.Domain.Status;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices.JavaScript;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var domHelpersModuleUri = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "js/domHelpers.js");
await JSHost.ImportAsync("domHelpers", domHelpersModuleUri.AbsoluteUri);
var spaHostExists = DomHelpers.HasSelector("#app");

if (spaHostExists)
{
    builder.RootComponents.Add<Routes>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");
}

builder.Services.Configure<ApiClientOptions>(options =>
    builder.Configuration.GetSection(ApiClientOptions.SectionName).Bind(options));

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddScoped<IApiBaseUriProvider, ApiBaseUriProvider>();
builder.Services.AddScoped<ILiveMeasurementClient, HttpLiveMeasurementClient>();
builder.Services.AddScoped<ISystemStatusClient, HttpSystemStatusClient>();

await builder.Build().RunAsync();
