using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PackProphet;
using PackProphet.Services;
using PackProphet.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddBlazorBootstrap();

// Scoped, not Singleton. HttpClient above is registered Scoped by the template, and a
// singleton may not depend on a scoped service — DI validation rejects it at startup, which
// takes down every page rather than just the one that needed it. In Blazor WebAssembly there
// is a single scope for the app's lifetime anyway, so Scoped gives the same one-instance
// behaviour these services need.
builder.Services.AddScoped<CardDataLoader>();
builder.Services.AddScoped<LocalStorageStateStore>();
builder.Services.AddScoped<IStateStore>(sp => sp.GetRequiredService<LocalStorageStateStore>());
builder.Services.AddScoped<AppSession>();
builder.Services.AddScoped<UiBusy>();

// Constructed by MainLayout rather than only where it is consumed: it works by observing
// navigation, so it has to exist from the first page load to have anything to remember.
builder.Services.AddScoped<NavHistory>();

// The nav asks for the palette; the layout owns it.
builder.Services.AddScoped<PaletteSwitch>();

await builder.Build().RunAsync();
