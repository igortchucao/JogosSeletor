using Contato.Services;
using Microsoft.AspNetCore.SignalR;

namespace Contato.Hubs;

public class GameHub : Hub
{
    private readonly GameService _game;
    public GameHub(GameService game) => _game = game;

    public Task<string> CreateRoom(string token) => _game.CreateRoomAsync(Context.ConnectionId, token);

    public Task<object> JoinRoom(string code, string name, string token) =>
        _game.JoinRoomAsync(Context.ConnectionId, code, name, token);

    // telão reassume a sala depois de reconectar (connectionId novo, mesmo token)
    public Task<bool> ReclaimHost(string code, string token) =>
        _game.ReclaimHostAsync(Context.ConnectionId, code, token);

    public Task StartRound(string code) => _game.StartRoundAsync(Context.ConnectionId, code);

    public Task SetSecretWord(string code, string word) =>
        _game.SetSecretWordAsync(Context.ConnectionId, code, word);

    public Task SubmitContact(string code, string word) =>
        _game.SubmitContactAsync(Context.ConnectionId, code, word);

    public Task SubmitIntercept(string code, string word) =>
        _game.SubmitInterceptAsync(Context.ConnectionId, code, word);

    public Task ClearInterceptGuesses(string code) =>
        _game.ClearInterceptGuessesAsync(Context.ConnectionId, code);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _ = _game.HandleDisconnectAsync(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
