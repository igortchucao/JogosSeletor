using Soberania.Models;
using Soberania.Services;
using Microsoft.AspNetCore.SignalR;

namespace Soberania.Hubs;

public class GameHub : Hub
{
    private readonly GameService _game;
    public GameHub(GameService game) => _game = game;

    // cria a sala; quem cria já entra como jogador (e vira host)
    public Task<object> CreateRoom(string name, string token) =>
        _game.CreateRoomAsync(Context.ConnectionId, name, token);

    public Task<object> JoinRoom(string code, string name, string token) =>
        _game.JoinRoomAsync(Context.ConnectionId, code, name, token);

    public Task ChooseCountry(string code, string countryId, string title) =>
        _game.ChooseCountryAsync(Context.ConnectionId, code, countryId, title);

    public Task StartGame(string code) => _game.StartGameAsync(Context.ConnectionId, code);

    // host avança para a próxima fase (e, depois de Resultados, para a próxima rodada)
    public Task NextPhase(string code) => _game.NextPhaseAsync(Context.ConnectionId, code);

    // ------- Negociação -------
    public Task<object> SendProposal(string code, string toId, ResourceDto offer, ResourceDto request) =>
        _game.SendProposalAsync(Context.ConnectionId, code, toId, offer, request);

    public Task RespondProposal(string code, string proposalId, bool accept) =>
        _game.RespondProposalAsync(Context.ConnectionId, code, proposalId, accept);

    // ------- Investimento -------
    public Task BuyCard(string code, string offerId) =>
        _game.BuyCardAsync(Context.ConnectionId, code, offerId);

    // ------- Ações -------
    public Task<object> PlayCard(string code, string handCardId, string? targetId) =>
        _game.PlayCardAsync(Context.ConnectionId, code, handCardId, targetId);

    public Task<object> MilitaryAttack(string code, string targetId) =>
        _game.MilitaryAttackAsync(Context.ConnectionId, code, targetId);

    public Task<object> Defame(string code, string targetId) =>
        _game.DefameAsync(Context.ConnectionId, code, targetId);

    public Task<object> ProposeRelation(string code, string toId, ResourceDto fromGives, ResourceDto toGives) =>
        _game.ProposeRelationAsync(Context.ConnectionId, code, toId, fromGives, toGives);

    public Task RespondRelation(string code, string relationId, bool accept) =>
        _game.RespondRelationAsync(Context.ConnectionId, code, relationId, accept);

    public Task CutRelation(string code, string relationId) =>
        _game.CutRelationAsync(Context.ConnectionId, code, relationId);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _ = _game.HandleDisconnectAsync(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
