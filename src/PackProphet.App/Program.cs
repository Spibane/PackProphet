using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PackProphet;
using PackProphet.Services;
using PackProphet.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Scoped, not Singleton. HttpClient above is registered Scoped by the template, and a singleton
// may not depend on a scoped service — DI validation rejects it at startup, taking down every page.
// In Blazor WebAssembly there is a single scope for the app's lifetime anyway, so Scoped gives the
// same one-instance behaviour these services need.
builder.Services.AddScoped<CardDataLoader>();
builder.Services.AddScoped<LocalStorageStateStore>();
builder.Services.AddScoped<IStateStore>(sp => sp.GetRequiredService<LocalStorageStateStore>());
builder.Services.AddScoped<AppSession>();
builder.Services.AddScoped<UiBusy>();

// Cloud sync. Off unless wwwroot/appsettings.json names a Supabase project, so a fork of this repo
// gets a working local-only app rather than a setting that cannot work. The services are registered
// either way: SyncService reports Unavailable and the settings page shows nothing.
builder.Services.AddScoped<BrowserStore>();
builder.Services.AddScoped(sp =>
    builder.Configuration.GetSection("Sync").Get<SyncOptions>() ?? new SyncOptions());
builder.Services.AddScoped<SyncCrypto>();
builder.Services.AddScoped<SyncTransport>();
builder.Services.AddScoped<SyncService>();

// Constructed by MainLayout rather than only where it is consumed: it works by observing
// navigation, so it has to exist from the first page load.
builder.Services.AddScoped<NavHistory>();

// The nav asks for the palette; the layout owns it.
builder.Services.AddScoped<PaletteSwitch>();

// Whichever grid is on screen registers itself here, so the palette can hand it focus from
// anywhere.
builder.Services.AddScoped<GridFocus>();

// Screenshot import. Both are lazy inside — the fingerprint table is not fetched, and the
// JavaScript module is not imported, until someone actually picks an image — so registering them
// here costs a page that never opens the feature nothing.
builder.Services.AddScoped<ArtHashSource>();
builder.Services.AddScoped<ShotScanner>();

await builder.Build().RunAsync();
