using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Contato.Infrastructure;

/// <summary>
/// Limita quantas chamadas cada conexão pode fazer nos hubs por janela de tempo.
/// Evita que um cliente malicioso metralhe chamadas e derrube o servidor (DoS).
/// Vale para todos os jogos, pois é registrado globalmente no SignalR.
/// </summary>
public class RateLimitFilter : IHubFilter
{
    // generoso pra jogo normal (ninguém aperta 30x em 10s), apertado pra spam
    private const int MaxCalls = 30;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private sealed class Counter
    {
        public int Count;
        public DateTime WindowStartUtc;
    }

    private static readonly ConcurrentDictionary<string, Counter> _counters = new();

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext ctx,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var now = DateTime.UtcNow;
        var counter = _counters.GetOrAdd(ctx.Context.ConnectionId,
            _ => new Counter { WindowStartUtc = now });

        lock (counter)
        {
            if (now - counter.WindowStartUtc > Window)
            {
                counter.WindowStartUtc = now;
                counter.Count = 0;
            }
            counter.Count++;
            if (counter.Count > MaxCalls)
                throw new HubException("Muitas ações em pouco tempo. Espere um instante.");
        }

        return await next(ctx);
    }

    public Task OnConnectedAsync(HubLifetimeContext ctx, Func<HubLifetimeContext, Task> next)
        => next(ctx);

    public async Task OnDisconnectedAsync(
        HubLifetimeContext ctx, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        _counters.TryRemove(ctx.Context.ConnectionId, out _);   // não vaza memória
        await next(ctx, exception);
    }
}
