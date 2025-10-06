using EquipmentHubDemo.Client;
using EquipmentHubDemo.Client.Services;
using EquipmentHubDemo.Domain.Live;
using EquipmentHubDemo.Domain.Status;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<EquipmentHubDemo.Client.Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

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
