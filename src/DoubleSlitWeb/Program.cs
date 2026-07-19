using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using DoubleSlitWeb;
using DoubleSlitWeb.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<ExperimentRunner>();
builder.Services.AddSingleton<WaveRenderer>();

await builder.Build().RunAsync();
