using BookRecommendationSystem.Data;
using BookRecommendationSystem.Web.Components;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CognoDbSettings>(builder.Configuration.GetSection("CognoDb"));

builder.Services.AddSingleton<IDriver>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<CognoDbSettings>>().Value;
    return Neo4jConnection.CreateDriver(settings);
});

builder.Services.AddScoped<RecommendationRepository>();
builder.Services.AddScoped<ReaderRepository>();
builder.Services.AddScoped<FollowRepository>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
//app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
