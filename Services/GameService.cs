using Contato.Hubs;
using Contato.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Contato.Services;

public class GameService
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Random _rng = new();

    // segundos que a janela de CONTATO fica aberta (todo mundo digita às cegas)
    private const int ContactWindowSeconds = 3;

    // intervalo mínimo entre chutes do interceptador (anti-spam)
    private const int InterceptCooldownMs = 2000;

    public GameService(IHubContext<GameHub> hub) => _hub = hub;

    // ----------------------------------------------------------------- criar sala
    public async Task<string> CreateRoomAsync(string connectionId)
    {
        string code;
        do { code = GenerateCode(); } while (_rooms.ContainsKey(code));

        var room = new Room { Code = code, HostConnectionId = connectionId };
        _rooms[code] = room;

        await _hub.Groups.AddToGroupAsync(connectionId, code);
        await BroadcastStateAsync(room);
        return code;
    }

    // ----------------------------------------------------------------- entrar
    public async Task<object> JoinRoomAsync(string connectionId, string code, string name)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(code, out var room))
            return new { ok = false, error = "Sala não encontrada." };

        name = (name ?? "").Trim();
        if (name.Length == 0) return new { ok = false, error = "Digite um nome." };
        if (name.Length > 20) name = name[..20];

        lock (room.Sync)
        {
            // reconexão pelo mesmo nome reaproveita o slot
            var existing = room.Players.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Id = connectionId;
                existing.Connected = true;
            }
            else
            {
                if (room.Phase != GamePhase.Lobby)
                    return new { ok = false, error = "A partida já começou." };
                room.Players.Add(new Player { Id = connectionId, Name = name });
            }
        }

        await _hub.Groups.AddToGroupAsync(connectionId, code);
        await BroadcastStateAsync(room);
        return new { ok = true, playerId = connectionId, code };
    }

    // ----------------------------------------------------------------- iniciar rodada
    public async Task StartRoundAsync(string connectionId, string code)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.HostConnectionId != connectionId) return;   // só o telão inicia
            var actives = room.Players.Where(p => p.Connected).ToList();
            if (actives.Count < 3) return;                       // 1 interceptador + 2 para fazer contato

            // rodízio do interceptador
            var interceptor = actives[room.RotationIndex % actives.Count];
            room.RotationIndex++;

            room.InterceptadorId = interceptor.Id;
            room.SecretWord = "";
            room.RevealedCount = 0;
            room.Submissions.Clear();
            room.InterceptGuesses.Clear();
            room.LastInterceptGuessUtc = null;
            room.LastResult = null;
            room.LastResultWord = null;
            room.ContactDeadlineUtc = null;
            room.Phase = GamePhase.AwaitingWord;
        }

        await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- definir palavra secreta
    public async Task SetSecretWordAsync(string connectionId, string code, string word)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.AwaitingWord) return;
            if (room.InterceptadorId != connectionId) return;

            var clean = (word ?? "").Trim().ToUpperInvariant();
            var letters = new string(clean.Where(c => char.IsLetter(c)).ToArray());
            if (letters.Length < 2) return; // palavra muito curta

            room.SecretWord = letters;
            room.RevealedCount = 1;        // jogadores já recebem a 1ª letra
            room.Phase = GamePhase.Playing;
        }

        await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- jogador aperta CONTATO
    public async Task SubmitContactAsync(string connectionId, string code, string word)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        bool openedWindow = false;
        Guid token = Guid.Empty;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Playing && room.Phase != GamePhase.ContactWindow) return;
            if (room.InterceptadorId == connectionId) return; // interceptador não faz contato

            // a palavra precisa começar com o prefixo já revelado (F, FA, FAR...)
            if (!Room.Normalize(word).StartsWith(RevealedPrefix(room), StringComparison.Ordinal)) return;

            room.Submissions[connectionId] = word;

            if (room.Phase == GamePhase.Playing)
            {
                // primeiro a apertar abre a janela
                room.Phase = GamePhase.ContactWindow;   // interceptador congela aqui
                room.ContactToken = token = Guid.NewGuid();
                room.ContactDeadlineUtc = DateTime.UtcNow.AddSeconds(ContactWindowSeconds);
                room.LastResult = null;
                room.LastResultWord = null;
                openedWindow = true;
            }
        }

        await BroadcastStateAsync(room);

        if (openedWindow)
            _ = ResolveAfterDelayAsync(room, token);
    }

    // ----------------------------------------------------------------- interceptador chuta (fase livre)
    public async Task SubmitInterceptAsync(string connectionId, string code, string word)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        bool added = false;

        lock (room.Sync)
        {
            // só na fase livre — depois que alguém aperta CONTATO ele congela
            if (room.Phase != GamePhase.Playing) return;
            if (room.InterceptadorId != connectionId) return;

            // cooldown anti-spam
            var now = DateTime.UtcNow;
            if (room.LastInterceptGuessUtc.HasValue &&
                (now - room.LastInterceptGuessUtc.Value).TotalMilliseconds < InterceptCooldownMs)
                return;

            var clean = (word ?? "").Trim().ToUpperInvariant();
            if (Room.Normalize(clean).Length == 0) return;
            // o chute também precisa começar com o prefixo revelado
            if (!Room.Normalize(clean).StartsWith(RevealedPrefix(room), StringComparison.Ordinal)) return;
            // ignora repetidos
            if (room.InterceptGuesses.Any(g => Room.Normalize(g) == Room.Normalize(clean))) return;

            room.InterceptGuesses.Add(clean);
            room.LastInterceptGuessUtc = now;
            added = true;
        }

        if (added) await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- interceptador limpa os chutes
    public async Task ClearInterceptGuessesAsync(string connectionId, string code)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.InterceptadorId != connectionId) return;
            if (room.InterceptGuesses.Count == 0) return;
            room.InterceptGuesses.Clear();
        }

        await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- resolução da janela
    private async Task ResolveAfterDelayAsync(Room room, Guid token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(ContactWindowSeconds));
        }
        catch { /* ignore */ }

        bool changed = false;
        lock (room.Sync)
        {
            if (room.Phase != GamePhase.ContactWindow || room.ContactToken != token) return;

            // agrupa palpites dos jogadores (exceto interceptador) por palavra normalizada,
            // considerando só os que começam com o prefixo revelado
            var prefix = RevealedPrefix(room);
            var groups = room.Submissions
                .Where(kv => kv.Key != room.InterceptadorId)
                .Select(kv => Room.Normalize(kv.Value))
                .Where(w => w.Length > 0 && w.StartsWith(prefix, StringComparison.Ordinal))
                .GroupBy(w => w)
                .Select(g => new { Word = g.Key, Count = g.Count() })
                .Where(g => g.Count >= 2)
                .OrderByDescending(g => g.Count)
                .ToList();

            var contactWord = groups.FirstOrDefault()?.Word;

            // o interceptador bloqueia se já tinha "queimado" essa palavra
            var burned = room.InterceptGuesses.Select(Room.Normalize).ToHashSet();

            if (contactWord == null)
            {
                room.LastResult = "failed";
                room.LastResultWord = null;
            }
            else if (burned.Contains(contactWord))
            {
                // interceptador bloqueou
                room.LastResult = "blocked";
                room.LastResultWord = contactWord;
                var interceptor = room.Players.FirstOrDefault(p => p.Id == room.InterceptadorId);
                if (interceptor != null) interceptor.Score += 1;
            }
            else
            {
                // contato bem-sucedido: revela mais uma letra
                room.LastResult = "success";
                room.LastResultWord = contactWord;
                room.RevealedCount = Math.Min(room.RevealedCount + 1, room.WordLength);
            }

            // nova dica começa do zero
            room.Submissions.Clear();
            room.InterceptGuesses.Clear();
            room.LastInterceptGuessUtc = null;

            if (room.LastResult == "success" && room.RevealedCount >= room.WordLength)
            {
                // jogadores venceram a rodada
                room.Phase = GamePhase.RoundOver;
                foreach (var p in room.Players.Where(p => p.Id != room.InterceptadorId))
                    p.Score += 2;
            }
            else
            {
                room.Phase = GamePhase.Playing;
            }
            changed = true;
        }

        if (changed) await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- desconexão
    public async Task HandleDisconnectAsync(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            bool touched = false;
            lock (room.Sync)
            {
                var p = room.Players.FirstOrDefault(x => x.Id == connectionId);
                if (p != null) { p.Connected = false; touched = true; }
            }
            if (touched) await BroadcastStateAsync(room);
        }
    }

    // ----------------------------------------------------------------- broadcast do estado
    private async Task BroadcastStateAsync(Room room)
    {
        object dto;
        string? interceptadorId;
        string secretForInterceptor;

        lock (room.Sync)
        {
            interceptadorId = room.InterceptadorId;
            secretForInterceptor = room.SecretWord;
            dto = new
            {
                code = room.Code,
                phase = room.Phase.ToString(),
                hostId = room.HostConnectionId,
                interceptadorId = room.InterceptadorId,
                players = room.Players.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    score = p.Score,
                    connected = p.Connected,
                    isInterceptador = p.Id == room.InterceptadorId
                }).ToList(),
                revealed = room.SecretWord.Length > 0
                    ? room.SecretWord[..Math.Min(room.RevealedCount, room.SecretWord.Length)]
                    : "",
                wordLength = room.WordLength,
                revealedCount = room.RevealedCount,
                contactActive = room.Phase == GamePhase.ContactWindow,
                contactDeadline = room.ContactDeadlineUtc.HasValue
                    ? new DateTimeOffset(room.ContactDeadlineUtc.Value).ToUnixTimeMilliseconds()
                    : (long?)null,
                submittedPlayerIds = room.Submissions.Keys.ToList(),
                interceptGuesses = room.InterceptGuesses.ToList(),
                lastResult = room.LastResult,
                lastResultWord = room.LastResultWord,
            };
        }

        await _hub.Clients.Group(room.Code).SendAsync("state", dto);

        // o interceptador recebe a palavra secreta só para ele
        if (!string.IsNullOrEmpty(interceptadorId))
            await _hub.Clients.Client(interceptadorId).SendAsync("secret", secretForInterceptor);
    }

    // prefixo já revelado da palavra secreta, normalizado (F, FA, FAR...)
    private static string RevealedPrefix(Room room)
    {
        var n = Math.Min(room.RevealedCount, room.SecretWord.Length);
        return n <= 0 ? "" : Room.Normalize(room.SecretWord.Substring(0, n));
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem I,O,0,1
        return new string(Enumerable.Range(0, 4).Select(_ => chars[_rng.Next(chars.Length)]).ToArray());
    }
}
