using Microsoft.AspNetCore.SignalR;   // necessário para options.AddFilter<T>()

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    // rate limit por conexão (vale para os dois jogos)
    options.AddFilter<Contato.Infrastructure.RateLimitFilter>();
    // teto no tamanho da mensagem: evita payload gigante estourar a memória
    options.MaximumReceiveMessageSize = 8 * 1024;   // 8 KB
});

// cada jogo tem seu próprio serviço de estado (as classes têm o mesmo nome,
// por isso ficam sempre qualificadas pelo namespace do jogo)
builder.Services.AddSingleton<Contato.Services.GameService>();
builder.Services.AddSingleton<Soberania.Services.GameService>();

// no Render (e outros hosts) a porta vem na variável de ambiente PORT
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.UseDefaultFiles();   // "/" = menu, "/contato/" e "/soberania/" = cada jogo
app.UseStaticFiles();

app.MapHub<Contato.Hubs.GameHub>("/hubs/contato");
app.MapHub<Soberania.Hubs.GameHub>("/hubs/soberania");

// health check leve p/ o keep-warm (evita o Render hibernar no plano free)
app.MapGet("/healthz", () => Results.Text("ok"));

app.Run();
