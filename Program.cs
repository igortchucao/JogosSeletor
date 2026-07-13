using Contato.Hubs;
using Contato.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<GameService>();

// no Render (e outros hosts) a porta vem na variável de ambiente PORT
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.UseDefaultFiles();   // serve wwwroot/index.html em "/"
app.UseStaticFiles();

app.MapHub<GameHub>("/gamehub");

app.Run();
