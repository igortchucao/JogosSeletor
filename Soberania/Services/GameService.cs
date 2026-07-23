using Soberania.Hubs;
using Soberania.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace Soberania.Services;

public class GameService
{
    private readonly IHubContext<GameHub> _hub;
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Random _rng = new();

    // mínimo de jogadores para iniciar (o host conta como jogador)
    private const int MinPlayers = 2;

    public GameService(IHubContext<GameHub> hub) => _hub = hub;

    // ----------------------------------------------------------------- catálogo de países
    // Recursos iniciais assimétricos só por sabor — a lógica econômica vem depois.
    private static readonly List<Country> Catalog = new()
    {
        new() { Id = "brasil",   Name = "Brasil",          Emoji = "🇧🇷", Inicial = new() { Dinheiro = 800, Terra = 90, Petroleo = 40, Alimento = 90, Militares = 40, Divida = 60 },  StatsInicial = new() { Populacao = 215, Credibilidade = 50, Aprovacao = 60 } },
        new() { Id = "eua",      Name = "Estados Unidos",  Emoji = "🇺🇸", Inicial = new() { Dinheiro = 1500, Terra = 70, Petroleo = 50, Alimento = 70, Militares = 90, Divida = 120 }, StatsInicial = new() { Populacao = 335, Credibilidade = 65, Aprovacao = 55 } },
        new() { Id = "russia",   Name = "Rússia",          Emoji = "🇷🇺", Inicial = new() { Dinheiro = 700, Terra = 100, Petroleo = 90, Alimento = 50, Militares = 85, Divida = 40 },  StatsInicial = new() { Populacao = 145, Credibilidade = 40, Aprovacao = 65 } },
        new() { Id = "china",    Name = "China",           Emoji = "🇨🇳", Inicial = new() { Dinheiro = 1200, Terra = 80, Petroleo = 40, Alimento = 60, Militares = 80, Divida = 70 },  StatsInicial = new() { Populacao = 1410, Credibilidade = 55, Aprovacao = 60 } },
        new() { Id = "arabia",   Name = "Arábia Saudita",  Emoji = "🇸🇦", Inicial = new() { Dinheiro = 1000, Terra = 50, Petroleo = 100, Alimento = 20, Militares = 55, Divida = 20 }, StatsInicial = new() { Populacao = 36, Credibilidade = 50, Aprovacao = 55 } },
        new() { Id = "alemanha", Name = "Alemanha",        Emoji = "🇩🇪", Inicial = new() { Dinheiro = 1100, Terra = 55, Petroleo = 20, Alimento = 55, Militares = 60, Divida = 50 },  StatsInicial = new() { Populacao = 84, Credibilidade = 70, Aprovacao = 58 } },
        new() { Id = "india",    Name = "Índia",           Emoji = "🇮🇳", Inicial = new() { Dinheiro = 600, Terra = 85, Petroleo = 30, Alimento = 80, Militares = 70, Divida = 55 },   StatsInicial = new() { Populacao = 1400, Credibilidade = 48, Aprovacao = 62 } },
        new() { Id = "nigeria",  Name = "Nigéria",         Emoji = "🇳🇬", Inicial = new() { Dinheiro = 500, Terra = 75, Petroleo = 80, Alimento = 60, Militares = 45, Divida = 65 },   StatsInicial = new() { Populacao = 220, Credibilidade = 42, Aprovacao = 57 } },
    };

    // ----------------------------------------------------------------- baralho de cartas (Investimento)
    // O jogador saca 3 e compra as que puder pagar; a carta vai para a mão e o EFEITO é aplicado na fase de Ações.
    private const int OffersPerTurn = 3;
    private static readonly List<CardDef> Deck = new()
    {
        new() { Id = "bomba",       Nome = "Bomba Atômica",      Emoji = "☢️", Alvo = "Inimigo", Efeito = "destruir_recursos_metade_populacao",
                Descricao = "Destrói os recursos e metade da população do alvo.", Custo = new() { Dinheiro = 900, Militares = 60, Petroleo = 50 } },
        new() { Id = "covid",       Nome = "Pandemia (COVID)",   Emoji = "🦠", Alvo = "Todos", Efeito = "peste_global",
                Descricao = "Todos perdem 2% dos recursos por turno e 10% da população.", Custo = new() { Dinheiro = 350 } },
        new() { Id = "escandalo",   Nome = "Escândalo Midiático", Emoji = "📰", Alvo = "Inimigo", Efeito = "reduz_cred_aprov",
                Descricao = "O alvo perde credibilidade e aprovação.", Custo = new() { Dinheiro = 250 } },
        new() { Id = "golpe",       Nome = "Golpe Militar",      Emoji = "🎖️", Alvo = "Inimigo", Efeito = "derruba_aprovacao",
                Descricao = "Derruba a aprovação do alvo drasticamente.", Custo = new() { Dinheiro = 300, Militares = 40 } },
        new() { Id = "estimulo",    Nome = "Pacote de Estímulo", Emoji = "💵", Alvo = "Proprio", Efeito = "sobe_aprov_pop",
                Descricao = "Aumenta sua aprovação e sua população.", Custo = new() { Dinheiro = 250 } },
        new() { Id = "propaganda",  Nome = "Propaganda Estatal", Emoji = "📢", Alvo = "Proprio", Efeito = "sobe_credibilidade",
                Descricao = "Aumenta sua credibilidade.", Custo = new() { Dinheiro = 150 } },
        new() { Id = "petroleo",    Nome = "Descoberta de Petróleo", Emoji = "🛢️", Alvo = "Proprio", Efeito = "ganha_petroleo",
                Descricao = "Novas reservas: ganha petróleo.", Custo = new() { Dinheiro = 300, Terra = 20 } },
        new() { Id = "humanitaria", Nome = "Ajuda Humanitária",  Emoji = "🕊️", Alvo = "Inimigo", Efeito = "diplomacia",
                Descricao = "Envia ajuda: sobe a credibilidade e aprovação do alvo (diplomacia).", Custo = new() { Dinheiro = 200, Alimento = 40 } },

        // --- cartas contra países NPC (fornecedores) ---
        new() { Id = "sabotagem",   Nome = "Sabotar Fornecedor", Emoji = "💣", Alvo = "Npc", Efeito = "bloqueia_npc",
                Descricao = "Ataca um país NPC: ele para de vender por 2 rodadas.", Custo = new() { Dinheiro = 450, Militares = 25 } },
        new() { Id = "contrabando", Nome = "Golpe Diplomático",  Emoji = "🕵️", Alvo = "Npc", Efeito = "quebra_restrita",
                Descricao = "Derruba a Relação Restrita que alguém tenha com esse NPC.", Custo = new() { Dinheiro = 400 } },
    };

    // ----------------------------------------------------------------- Relação Restrita (com NPC)
    // Acordo caro e exclusivo: o dono compra o NPC "para si" e o que aquele NPC exige
    // fica MAIS CARO para todos os outros jogadores.
    private const int RestritaPrice = 1500;      // custo em dinheiro
    private const int RestritaMarkupPct = 160;   // os outros pagam 160% do preço normal (+60%)
    private const int SabotagemRounds = 2;

    private static NpcState StateOf(Room room, string npcId)
    {
        if (!room.NpcStates.TryGetValue(npcId, out var st))
            room.NpcStates[npcId] = st = new NpcState();
        return st;
    }

    /// <summary>O que o NPC exige DESTE jogador (com ágio se outro dono detém a Relação Restrita).</summary>
    private static Cofre QuerDe(Room room, Npc npc, string playerId)
    {
        var st = StateOf(room, npc.Id);
        return (st.RestritaOwnerId != null && st.RestritaOwnerId != playerId)
            ? npc.Quer.Scale(RestritaMarkupPct)
            : npc.Quer;
    }

    // ----------------------------------------------------------------- NPCs (oferta FIXA, igual em todo jogo)
    // Cada NPC entrega "Da" e exige "Quer". Aceita automaticamente se a proposta do jogador
    // oferecer pelo menos o que ele Quer e pedir no máximo o que ele Da.
    // Valores na mesma escala dos cofres — ajuste à vontade.
    private static readonly List<Npc> Npcs = new()
    {
        new() { Id = "venezuela", Name = "Venezuela", Emoji = "🇻🇪", Da = new() { Petroleo = 40 }, Quer = new() { Dinheiro = 300 } },
        new() { Id = "catar",     Name = "Catar",     Emoji = "🇶🇦", Da = new() { Dinheiro = 400 }, Quer = new() { Petroleo = 30 } },
        new() { Id = "ucrania",   Name = "Ucrânia",   Emoji = "🇺🇦", Da = new() { Alimento = 60 }, Quer = new() { Dinheiro = 250 } },
        new() { Id = "suica",     Name = "Suíça",     Emoji = "🇨🇭", Da = new() { Dinheiro = 600 }, Quer = new() { Terra = 40 } },
    };

    // ----------------------------------------------------------------- criar sala
    // --- limites de recurso (evitam estourar a memória do container) ---
    private const int MaxRooms = 200;
    private static readonly TimeSpan EmptyRoomTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleRoomTtl = TimeSpan.FromHours(3);

    /// <summary>Descarta salas abandonadas (sem ninguém conectado) ou paradas há muito tempo.</summary>
    private void PurgeStaleRooms()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _rooms)
        {
            var room = kv.Value;
            bool stale;
            lock (room.Sync)
            {
                var idle = now - room.LastActivityUtc;
                var ninguem = !room.Players.Any(p => p.Connected);
                stale = (ninguem && idle > EmptyRoomTtl) || idle > IdleRoomTtl;
            }
            if (stale) _rooms.TryRemove(kv.Key, out _);
        }
    }

    public async Task<object> CreateRoomAsync(string connectionId, string name, string token)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) return new { ok = false, error = "Digite um nome." };
        if (name.Length > 20) name = name[..20];
        token = SanitizeToken(token);

        PurgeStaleRooms();
        if (_rooms.Count >= MaxRooms)
            return new { ok = false, error = "O servidor está cheio de salas. Tente de novo em alguns minutos." };

        string code;
        do { code = GenerateCode(); } while (_rooms.ContainsKey(code));

        var room = new Room { Code = code, HostConnectionId = connectionId, HostToken = token };
        room.Players.Add(new Player { Id = connectionId, Token = token, Name = name });
        _rooms[code] = room;

        await _hub.Groups.AddToGroupAsync(connectionId, code);
        await BroadcastStateAsync(room);
        return new { ok = true, code, playerId = connectionId };
    }

    // ----------------------------------------------------------------- entrar
    // A identidade do jogador é o TOKEN (estável no cliente), nunca o nome nem o connectionId
    // (que muda a cada reconexão). Isso evita: dois jogadores com o mesmo connectionId,
    // e alguém sequestrar o slot alheio só usando o mesmo nome.
    public async Task<object> JoinRoomAsync(string connectionId, string code, string name, string token)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(code, out var room))
            return new { ok = false, error = "Sala não encontrada." };

        name = (name ?? "").Trim();
        if (name.Length == 0) return new { ok = false, error = "Digite um nome." };
        if (name.Length > 20) name = name[..20];
        token = SanitizeToken(token);

        lock (room.Sync)
        {
            // 1) já conheço este token? é reconexão/re-entrada: reamarra ao novo connectionId
            var mine = token.Length > 0
                ? room.Players.FirstOrDefault(p => p.Token == token)
                : null;
            if (mine != null)
            {
                mine.Id = connectionId;
                mine.Connected = true;
                if (room.HostToken == token) room.HostConnectionId = connectionId;
            }
            else
            {
                // 2) esta conexão já ocupa outro slot? (era o bug de "2 jogadores, 1 ID")
                if (room.Players.Any(p => p.Id == connectionId))
                    return new { ok = false, error = "Esta conexão já está na sala." };

                var sameName = room.Players.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

                if (sameName != null)
                {
                    // 3) nome ocupado por alguém ONLINE: barra (antes isso sequestrava o slot)
                    if (sameName.Connected)
                        return new { ok = false, error = "Já tem alguém com esse nome na sala." };

                    // 4) jogador offline: deixa reassumir o próprio slot (perdeu o token, trocou de aba)
                    var oldToken = sameName.Token;
                    sameName.Id = connectionId;
                    sameName.Token = token;
                    sameName.Connected = true;
                    if (room.HostToken == oldToken) { room.HostToken = token; room.HostConnectionId = connectionId; }
                }
                else
                {
                    if (room.Phase != GamePhase.Lobby)
                        return new { ok = false, error = "A partida já começou." };
                    room.Players.Add(new Player { Id = connectionId, Token = token, Name = name });
                }
            }
        }

        await _hub.Groups.AddToGroupAsync(connectionId, code);
        await BroadcastStateAsync(room);
        return new { ok = true, code, playerId = connectionId };
    }

    private static string SanitizeToken(string? t)
    {
        t = (t ?? "").Trim();
        return t.Length > 64 ? t[..64] : t;
    }

    // ----------------------------------------------------------------- escolher país
    public async Task ChooseCountryAsync(string connectionId, string code, string countryId, string title)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        var country = Catalog.FirstOrDefault(c => c.Id == countryId);
        if (country == null) return;
        if (!Enum.TryParse<LeaderTitle>(title, ignoreCase: true, out var leaderTitle))
            leaderTitle = LeaderTitle.Presidente;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Lobby) return;

            var me = room.Players.FirstOrDefault(p => p.Id == connectionId);
            if (me == null) return;

            // país já tomado por outro jogador?
            if (room.Players.Any(p => p.Id != connectionId && p.CountryId == countryId)) return;

            me.CountryId = countryId;
            me.Title = leaderTitle;
            me.Cofre = country.Inicial.Clone();       // cofre nasce dos recursos iniciais do país
            me.Stats = country.StatsInicial.Clone();  // e os itens internos também
        }

        await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- iniciar jogo
    public async Task StartGameAsync(string connectionId, string code)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        Guid startedToken;

        lock (room.Sync)
        {
            if (room.HostConnectionId != connectionId) return;   // só o host inicia
            if (room.Phase != GamePhase.Lobby) return;

            var actives = room.Players.Where(p => p.Connected).ToList();
            if (actives.Count < MinPlayers) return;
            if (actives.Any(p => !p.ChoseCountry)) return;       // todos precisam ter país

            room.Round = 1;
            room.Proposals.Clear();
            room.Events.Clear();
            EnterPhase(room, GamePhase.Negociacao);
            startedToken = room.PhaseToken;
        }

        await BroadcastStateAsync(room);
        _ = PhaseTimerAsync(room, startedToken);
    }

    // ----------------------------------------------------------------- avançar fase / rodada
    // ----------------------------------------------------------------- motor de fases (tempo + prontidão)
    // A fase NÃO depende mais do host: troca quando todos os jogadores ativos ficam prontos
    // ou quando o cronômetro da fase zera. Segundos por fase (tunáveis):
    private static readonly Dictionary<GamePhase, int> PhaseSeconds = new()
    {
        [GamePhase.Negociacao]   = 120,
        [GamePhase.Investimento] = 90,
        [GamePhase.Acoes]        = 90,
        [GamePhase.Represarias]  = 60,
        [GamePhase.Resultados]   = 45,
    };

    /// <summary>Entra numa fase: zera a prontidão, arma o cronômetro e roda o gatilho da fase. Chamar sob lock.</summary>
    private static void EnterPhase(Room room, GamePhase phase)
    {
        room.Phase = phase;
        foreach (var p in room.Players) p.Ready = false;
        room.PhaseToken = Guid.NewGuid();
        room.PhaseDeadlineUtc = DateTime.UtcNow.AddSeconds(PhaseSeconds.TryGetValue(phase, out var s) ? s : 60);

        if (phase == GamePhase.Investimento) DealCards(room);
        else if (phase == GamePhase.Acoes) MaybeTriggerDisaster(room);
        else if (phase == GamePhase.Resultados) ComputeResults(room);
    }

    // ----------------------------------------------------------------- desastres naturais
    // O próprio jogo age: raro, sem culpado, para quebrar a rotina. Sorteado ao entrar em Ações.
    private const int DisasterChancePct = 12;   // chance por rodada (tunável)

    private record DisasterDef(string Id, string Nome, string Emoji, string Manchete);

    private static readonly List<DisasterDef> Disasters = new()
    {
        new("tsunami",   "Tsunami",   "🌊", "Ondas gigantes devastam o litoral"),
        new("terremoto", "Terremoto", "🏚️", "A terra treme e derruba cidades inteiras"),
        new("enchente",  "Enchente",  "💧", "As águas sobem e afogam as lavouras"),
        new("furacao",   "Furacão",   "🌀", "Ventos arrasam a infraestrutura"),
    };

    /// <summary>Perda proporcional: quem é grande perde mais, mas nunca some do mapa.</summary>
    private static int Perda(int valor, int pct) => valor > 0 ? Math.Max(1, valor * pct / 100) : 0;

    private static void MaybeTriggerDisaster(Room room)
    {
        if (_rng.Next(100) >= DisasterChancePct) return;

        var candidatos = room.Players.Where(p => p.ChoseCountry && !p.Deposto).ToList();
        if (candidatos.Count == 0) return;

        var vitima = candidatos[_rng.Next(candidatos.Count)];
        var d = Disasters[_rng.Next(Disasters.Count)];
        var c = vitima.Cofre;
        var st = vitima.Stats;
        var perdas = new List<string>();

        void TiraRecurso(ref int campo, int pct, string emoji)
        {
            var perda = Perda(campo, pct);
            if (perda > 0) { campo -= perda; perdas.Add($"-{perda}{emoji}"); }
        }

        int dinheiro = c.Dinheiro, terra = c.Terra, alimento = c.Alimento, militares = c.Militares;

        switch (d.Id)
        {
            case "tsunami":
                TiraRecurso(ref terra, 15, "🗺️"); TiraRecurso(ref alimento, 25, "🌾");
                st.Populacao -= Perda(st.Populacao, 8); st.Aprovacao -= 5;
                break;
            case "terremoto":
                TiraRecurso(ref dinheiro, 15, "💰"); TiraRecurso(ref terra, 10, "🗺️");
                TiraRecurso(ref militares, 15, "🪖");
                st.Populacao -= Perda(st.Populacao, 10); st.Aprovacao -= 6;
                break;
            case "enchente":
                TiraRecurso(ref alimento, 30, "🌾"); TiraRecurso(ref terra, 12, "🗺️");
                st.Populacao -= Perda(st.Populacao, 5); st.Aprovacao -= 4;
                break;
            case "furacao":
                TiraRecurso(ref dinheiro, 12, "💰"); TiraRecurso(ref alimento, 20, "🌾");
                TiraRecurso(ref militares, 10, "🪖");
                st.Populacao -= Perda(st.Populacao, 6); st.Aprovacao -= 5;
                break;
        }

        c.Dinheiro = dinheiro; c.Terra = terra; c.Alimento = alimento; c.Militares = militares;
        st.Clamp();

        var detalhe = perdas.Count > 0 ? " " + string.Join(" ", perdas) : "";
        room.Events.Add(new GameEvent
        {
            Round = room.Round,
            Phase = GamePhase.Acoes,
            ActorId = "",                       // sem culpado: não gera direito a represália
            TargetId = vitima.Id,
            Kind = "desastre",
            Public = true,                      // é notícia: todos veem na hora
            Text = $"{d.Emoji} {d.Nome} em {LabelFor(room, vitima)}! {d.Manchete}.{detalhe} e a população caiu."
        });
    }

    /// <summary>Decide e aplica a próxima fase (fecha a rodada depois de Resultados). Chamar sob lock.</summary>
    private static void AdvanceLocked(Room room)
    {
        if (room.Phase == GamePhase.Resultados)
        {
            room.Round++;
            room.Proposals.Clear();   // negociação começa limpa a cada rodada
            room.Events.Clear();      // eventos são por rodada
            EnterPhase(room, GamePhase.Negociacao);
        }
        else
        {
            EnterPhase(room, (GamePhase)((int)room.Phase + 1));
        }
    }

    /// <summary>Só conta quem pode agir: conectado, com país e não deposto.</summary>
    private static bool AllReady(Room room)
    {
        var elegiveis = room.Players.Where(p => p.Connected && p.ChoseCountry && !p.Deposto).ToList();
        return elegiveis.Count > 0 && elegiveis.All(p => p.Ready);
    }

    public async Task SetReadyAsync(string connectionId, string code, bool ready)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;
        bool advanced = false;
        Guid token = default;

        lock (room.Sync)
        {
            if (room.Phase == GamePhase.Lobby) return;
            var p = room.Players.FirstOrDefault(x => x.Id == connectionId);
            if (p == null || !p.ChoseCountry || p.Deposto) return;

            p.Ready = ready;
            if (AllReady(room)) { AdvanceLocked(room); advanced = true; token = room.PhaseToken; }
        }

        await BroadcastStateAsync(room);
        if (advanced) _ = PhaseTimerAsync(room, token);
    }

    /// <summary>Cronômetro da fase. O token garante que um timer de fase antiga não avance nada.</summary>
    private async Task PhaseTimerAsync(Room room, Guid token)
    {
        while (true)
        {
            TimeSpan wait;
            lock (room.Sync)
            {
                if (room.PhaseToken != token) return;                  // outra via já avançou
                if (room.PhaseDeadlineUtc is not DateTime dl) return;
                wait = dl - DateTime.UtcNow;
            }

            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait); } catch { return; }
            }

            lock (room.Sync)
            {
                if (room.PhaseToken != token) return;
                if (room.PhaseDeadlineUtc is DateTime dl2 && DateTime.UtcNow < dl2.AddMilliseconds(-100))
                    continue;                                          // acordou cedo demais
                AdvanceLocked(room);
                token = room.PhaseToken;                               // segue cronometrando a próxima fase
            }

            await BroadcastStateAsync(room);
        }
    }

    // ----------------------------------------------------------------- Investimento: distribuir e comprar cartas
    private static void DealCards(Room room)
    {
        foreach (var p in room.Players.Where(p => p.ChoseCountry))
        {
            p.Ofertas.Clear();
            for (int i = 0; i < OffersPerTurn; i++)
            {
                var def = Deck[_rng.Next(Deck.Count)];   // com reposição: pode repetir
                p.Ofertas.Add(new HeldCard { CardId = def.Id });
            }
        }
    }

    public async Task BuyCardAsync(string connectionId, string code, string offerId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Investimento) return;

            var p = room.Players.FirstOrDefault(x => x.Id == connectionId);
            if (p == null || !p.ChoseCountry) return;

            var offer = p.Ofertas.FirstOrDefault(o => o.Id == offerId);
            if (offer == null) return;

            var def = Deck.FirstOrDefault(d => d.Id == offer.CardId);
            if (def == null) return;

            if (!p.Cofre.CobreOuIgual(def.Custo)) return;   // cartas exigem saldo (sem negativo aqui)

            p.Cofre.Apply(def.Custo, -1);
            p.Ofertas.Remove(offer);
            p.Mao.Add(new HeldCard { CardId = def.Id });
        }

        await BroadcastStateAsync(room);
    }

    // ================================================================= AÇÕES
    private const int DefameCost = 150;      // custo em dinheiro para difamar
    private const double AtkCasualtyRate = 0.6;  // % das militares do atacante que vira baixa no alvo
    private const double DefCasualtyRate = 0.4;  // % das militares do defensor que vira baixa no atacante

    // -------- jogar carta da mão --------
    public async Task<object> PlayCardAsync(string connectionId, string code, string handCardId, string? targetId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");

        lock (room.Sync)
        {
            bool represalia = room.Phase == GamePhase.Represarias;
            if (room.Phase != GamePhase.Acoes && !represalia)
                return Fail("Só dá para jogar cartas nas fases de Ações ou Represálias.");

            var actor = room.Players.FirstOrDefault(p => p.Id == connectionId);
            if (actor == null || !actor.ChoseCountry || actor.Deposto) return Fail("Você não pode agir.");

            var held = actor.Mao.FirstOrDefault(h => h.Id == handCardId);
            if (held == null) return Fail("Carta não está na sua mão.");
            var def = Deck.FirstOrDefault(d => d.Id == held.CardId);
            if (def == null) return Fail("Carta inválida.");

            if (represalia && def.Alvo != "Inimigo")
                return Fail("Nas Represálias só dá para usar cartas contra quem te agrediu.");

            // cartas contra países NPC (fornecedores)
            if (def.Alvo == "Npc")
            {
                var id = (targetId ?? "").StartsWith("npc:", StringComparison.OrdinalIgnoreCase)
                    ? targetId!["npc:".Length..] : targetId;
                var npcAlvo = Npcs.FirstOrDefault(n => n.Id == id);
                if (npcAlvo == null) return Fail("Escolha um país NPC válido.");

                var st = StateOf(room, npcAlvo.Id);
                string msgNpc;
                if (def.Efeito == "bloqueia_npc")
                {
                    st.BloqueioRoundsLeft = SabotagemRounds;
                    msgNpc = $"{npcAlvo.Name} para de vender por {SabotagemRounds} rodadas.";
                }
                else if (def.Efeito == "quebra_restrita")
                {
                    if (st.RestritaOwnerId == null) msgNpc = $"{npcAlvo.Name} não tinha Relação Restrita — sem efeito.";
                    else
                    {
                        var antigo = Label(room, st.RestritaOwnerId);
                        st.RestritaOwnerId = null;
                        msgNpc = $"A Relação Restrita de {antigo} com {npcAlvo.Name} foi derrubada.";
                    }
                }
                else msgNpc = "";

                actor.Mao.Remove(held);
                RecordEvent(room, actor.Id, null, "carta",
                    $"{Label(room, actor.Id)} usou {def.Emoji} {def.Nome} contra {npcAlvo.Emoji} {npcAlvo.Name}. {msgNpc}");
            }
            else
            {
                Player? target = null;
                if (def.Alvo == "Inimigo")
                {
                    target = room.Players.FirstOrDefault(p => p.Id == targetId && p.ChoseCountry);
                    if (target == null) return Fail("Escolha um alvo válido.");
                    if (target.Id == actor.Id) return Fail("Essa carta é contra um inimigo.");
                    if (represalia && !AggressorsOf(room, actor.Id).Contains(target.Id))
                        return Fail("Nas Represálias você só pode revidar quem te agrediu nesta rodada.");
                }

                var msg = ApplyCardEffect(room, actor, target, def);
                actor.Mao.Remove(held);
                RecordEvent(room, actor.Id, target?.Id, "carta", $"{Label(room, actor.Id)} usou {def.Emoji} {def.Nome}{(target != null ? $" contra {Label(room, target.Id)}" : "")}. {msg}");
            }
        }

        await BroadcastStateAsync(room);
        return new { ok = true };
    }

    private string ApplyCardEffect(Room room, Player actor, Player? target, CardDef def)
    {
        switch (def.Efeito)
        {
            case "destruir_recursos_metade_populacao":
                target!.Cofre.Petroleo = 0; target.Cofre.Alimento = 0; target.Cofre.Militares = 0;
                target.Cofre.Terra /= 2; target.Cofre.Dinheiro /= 2;
                target.Stats.Populacao /= 2; target.Stats.Aprovacao -= 20; target.Stats.Clamp();
                return "Recursos arrasados e metade da população perdida.";
            case "reduz_cred_aprov":
                target!.Stats.Credibilidade -= 15; target.Stats.Aprovacao -= 12; target.Stats.Clamp();
                return "O alvo perdeu credibilidade e aprovação.";
            case "derruba_aprovacao":
                target!.Stats.Aprovacao -= 30; target.Stats.Clamp();
                return "A aprovação do alvo despencou.";
            case "diplomacia":
                target!.Stats.Credibilidade += 10; target.Stats.Aprovacao += 8; target.Stats.Clamp();
                return "Ajuda enviada: credibilidade e aprovação do alvo subiram.";
            case "sobe_aprov_pop":
                actor.Stats.Aprovacao += 10; actor.Stats.Populacao += 5; actor.Stats.Clamp();
                return "Sua aprovação e população subiram.";
            case "sobe_credibilidade":
                actor.Stats.Credibilidade += 15; actor.Stats.Clamp();
                return "Sua credibilidade subiu.";
            case "ganha_petroleo":
                actor.Cofre.Petroleo += 40;
                return "Novas reservas: +40 petróleo.";
            case "peste_global":
                room.Ongoing.Add(new OngoingEffect { Efeito = "peste_global", RoundsLeft = 3, Nome = "Pandemia", Emoji = "🦠", SourceLabel = Label(room, actor.Id) });
                return "Pandemia global iniciada: -2% recursos e -10% população por 3 turnos.";
            default:
                return "";
        }
    }

    // -------- ataque militar (força relativa) --------
    public async Task<object> MilitaryAttackAsync(string connectionId, string code, string targetId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");

        lock (room.Sync)
        {
            var a = room.Players.FirstOrDefault(p => p.Id == connectionId);
            var t = room.Players.FirstOrDefault(p => p.Id == targetId && p.ChoseCountry);
            if (a == null || !a.ChoseCountry || a.Deposto) return Fail("Você não pode agir.");
            if (t == null) return Fail("Alvo inválido.");
            if (t.Id == a.Id) return Fail("Não dá para atacar a si mesmo.");
            var phaseErr = CheckOffensivePhase(room, a.Id, t.Id, out _);
            if (phaseErr != null) return phaseErr;
            if (a.Cofre.Militares <= 0) return Fail("Sem militares para atacar.");

            int pa = a.Cofre.Militares, pd = t.Cofre.Militares;
            int baixasT = Math.Min(pd, (int)Math.Ceiling(pa * AtkCasualtyRate));
            int baixasA = Math.Min(pa, (int)Math.Ceiling(pd * DefCasualtyRate));
            t.Cofre.Militares -= baixasT;
            a.Cofre.Militares -= baixasA;

            int vantagem = pa - pd;
            string extra;
            if (vantagem > 0)
            {
                int perdaPop = vantagem * 2;
                t.Stats.Populacao = Math.Max(0, t.Stats.Populacao - perdaPop);
                t.Stats.Aprovacao -= 5; t.Stats.Clamp();
                int saque = vantagem / 2;
                int petro = Math.Min(saque, Math.Max(0, t.Cofre.Petroleo));
                int alim = Math.Min(saque, Math.Max(0, t.Cofre.Alimento));
                t.Cofre.Petroleo -= petro; a.Cofre.Petroleo += petro;
                t.Cofre.Alimento -= alim; a.Cofre.Alimento += alim;
                extra = $" Invasão! Alvo perdeu {perdaPop}mi de população; saque de {petro}🛢️ e {alim}🌾.";
            }
            else
            {
                extra = " O alvo resistiu bem à investida.";
            }

            RecordEvent(room, a.Id, t.Id, "militar",
                $"⚔️ {Label(room, a.Id)} atacou {Label(room, t.Id)}. Baixas: atacante -{baixasA}🪖, alvo -{baixasT}🪖.{extra}");
        }

        await BroadcastStateAsync(room);
        return new { ok = true };
    }

    // -------- difamar --------
    public async Task<object> DefameAsync(string connectionId, string code, string targetId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");

        lock (room.Sync)
        {
            var a = room.Players.FirstOrDefault(p => p.Id == connectionId);
            var t = room.Players.FirstOrDefault(p => p.Id == targetId && p.ChoseCountry);
            if (a == null || !a.ChoseCountry || a.Deposto) return Fail("Você não pode agir.");
            if (t == null || t.Id == a.Id) return Fail("Alvo inválido.");
            var phaseErr = CheckOffensivePhase(room, a.Id, t.Id, out _);
            if (phaseErr != null) return phaseErr;
            if (a.Cofre.Dinheiro < DefameCost) return Fail($"Difamar custa {DefameCost} 💰.");

            a.Cofre.Dinheiro -= DefameCost;
            t.Stats.Credibilidade -= 10; t.Stats.Aprovacao -= 8; t.Stats.Clamp();
            RecordEvent(room, a.Id, t.Id, "difamar",
                $"📢 {Label(room, a.Id)} difamou {Label(room, t.Id)}: -10 credibilidade, -8 aprovação.");
        }

        await BroadcastStateAsync(room);
        return new { ok = true };
    }

    // -------- criar relação comercial (proposta recorrente) --------
    public async Task<object> ProposeRelationAsync(string connectionId, string code, string toId, ResourceDto fromGives, ResourceDto toGives)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");
        var fg = fromGives?.ToCofre() ?? new Cofre();
        var tg = toGives?.ToCofre() ?? new Cofre();
        if (fg.IsEmpty && tg.IsEmpty) return Fail("A relação está vazia.");

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Acoes) return Fail("Proponha relações na fase de Ações.");
            var a = room.Players.FirstOrDefault(p => p.Id == connectionId);
            var t = room.Players.FirstOrDefault(p => p.Id == toId && p.ChoseCountry);
            if (a == null || !a.ChoseCountry) return Fail("Você não está na partida.");
            if (t == null || t.Id == a.Id) return Fail("Alvo inválido.");

            room.Relations.Add(new Relation { FromId = a.Id, ToId = t.Id, FromGives = fg, ToGives = tg });
        }

        await BroadcastStateAsync(room);
        return new { ok = true };
    }

    // -------- aceitar/recusar relação (o alvo) --------
    public async Task RespondRelationAsync(string connectionId, string code, string relationId, bool accept)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Acoes && room.Phase != GamePhase.Represarias) return;
            var rel = room.Relations.FirstOrDefault(r => r.Id == relationId);
            if (rel == null || rel.Status != RelationStatus.Pendente) return;
            if (rel.ToId != connectionId) return;

            if (!accept) { room.Relations.Remove(rel); return; }
            rel.Status = RelationStatus.Ativa;
            RecordEvent(room, rel.FromId, rel.ToId, "relacao",
                $"🤝 Relação comercial firmada entre {Label(room, rel.FromId)} e {Label(room, rel.ToId)} (vale todo turno até ser cortada).");
        }

        await BroadcastStateAsync(room);
    }

    // -------- cortar relação (qualquer um dos dois) --------
    public async Task CutRelationAsync(string connectionId, string code, string relationId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Acoes && room.Phase != GamePhase.Represarias) return;
            var rel = room.Relations.FirstOrDefault(r => r.Id == relationId);
            if (rel == null) return;
            if (rel.FromId != connectionId && rel.ToId != connectionId) return;

            room.Relations.Remove(rel);
            if (rel.Status == RelationStatus.Ativa)
                RecordEvent(room, connectionId, rel.FromId == connectionId ? rel.ToId : rel.FromId, "relacao",
                    $"✂️ {Label(room, connectionId)} cortou a relação comercial com {Label(room, rel.FromId == connectionId ? rel.ToId : rel.FromId)}.");
        }

        await BroadcastStateAsync(room);
    }

    // -------- helpers de Ações --------
    private static object Fail(string error) => new { ok = false, error };

    // quem agrediu (militar/difamar/carta) o jogador NESTA rodada — alvos válidos de represália
    // Só agressões da fase de AÇÕES dão direito a revide — senão o revide de A vira
    // "agressão" contra B, que revida de volta, e as Represálias viram um vai-e-volta infinito.
    private static HashSet<string> AggressorsOf(Room room, string victimId) =>
        room.Events
            .Where(e => e.Round == room.Round && e.Phase == GamePhase.Acoes && e.TargetId == victimId
                        && (e.Kind == "militar" || e.Kind == "difamar" || e.Kind == "carta"))
            .Select(e => e.ActorId)
            .ToHashSet();

    // valida a fase de uma ação ofensiva; em Represálias, o alvo precisa ter agredido o ator nesta rodada
    private static object? CheckOffensivePhase(Room room, string actorId, string targetId, out bool represalia)
    {
        represalia = room.Phase == GamePhase.Represarias;
        if (room.Phase != GamePhase.Acoes && !represalia)
            return Fail("Só dá para agir nas fases de Ações ou Represálias.");
        if (represalia && !AggressorsOf(room, actorId).Contains(targetId))
            return Fail("Nas Represálias você só pode revidar quem te agrediu nesta rodada.");
        return null;
    }

    private static void RecordEvent(Room room, string actorId, string? targetId, string kind, string text) =>
        room.Events.Add(new GameEvent { Round = room.Round, Phase = room.Phase, ActorId = actorId, TargetId = targetId, Kind = kind, Text = text });

    private string Label(Room room, string playerId)
    {
        var p = room.Players.FirstOrDefault(x => x.Id == playerId);
        var c = p?.CountryId != null ? Catalog.FirstOrDefault(x => x.Id == p.CountryId) : null;
        return c != null ? $"{c.Emoji} {c.Name}" : (p?.Name ?? "?");
    }

    // ----------------------------------------------------------------- Resultados
    // Ordem: impostos + crescimento → efeitos contínuos (ex.: peste) → relações comerciais → golpe.
    private static void ComputeResults(Room room)
    {
        var ativos = room.Players.Where(p => p.ChoseCountry && !p.Deposto).ToList();

        foreach (var p in room.Players.Where(p => p.ChoseCountry)) p.LastResults.Clear();
        foreach (var p in room.Players.Where(p => p.ChoseCountry && p.Deposto))
            p.LastResults.Add("💀 País sob novo regime (líder deposto).");

        // 1) impostos + crescimento populacional
        foreach (var p in ativos)
        {
            var s = p.Stats;
            int imposto = s.Populacao * s.Aprovacao / 100;
            p.Cofre.Dinheiro += imposto;
            p.LastResults.Add($"💰 Impostos: +{imposto} (população {s.Populacao}mi × aprovação {s.Aprovacao}%).");

            int crescimento = s.Credibilidade / 25;
            if (crescimento > 0)
            {
                s.Populacao += crescimento;
                p.LastResults.Add($"👥 População cresceu +{crescimento}mi (credibilidade {s.Credibilidade}).");
            }
        }

        // 2) efeitos contínuos (peste global atinge todos os ativos)
        foreach (var eff in room.Ongoing.ToList())
        {
            if (eff.Efeito == "peste_global")
            {
                foreach (var p in ativos)
                {
                    var c = p.Cofre;
                    int Perc(int v) => v > 0 ? Math.Max(1, v * 2 / 100) : 0;   // -2%
                    c.Dinheiro -= Perc(c.Dinheiro); c.Terra -= Perc(c.Terra); c.Petroleo -= Perc(c.Petroleo);
                    c.Alimento -= Perc(c.Alimento); c.Militares -= Perc(c.Militares);
                    int perdaPop = p.Stats.Populacao / 10;                       // -10%
                    p.Stats.Populacao -= perdaPop;
                    p.LastResults.Add($"{eff.Emoji} {eff.Nome}: -2% recursos e -{perdaPop}mi de população.");
                }
            }
            eff.RoundsLeft--;
            if (eff.RoundsLeft <= 0) room.Ongoing.Remove(eff);
        }

        // 2b) sabotagem de NPC expira com o tempo
        foreach (var st in room.NpcStates.Values)
            if (st.BloqueioRoundsLeft > 0) st.BloqueioRoundsLeft--;

        // 3) relações comerciais ativas trocam recursos (permite negativo)
        foreach (var rel in room.Relations.Where(r => r.Status == RelationStatus.Ativa))
        {
            var from = ativos.FirstOrDefault(p => p.Id == rel.FromId);
            var to = ativos.FirstOrDefault(p => p.Id == rel.ToId);
            if (from == null || to == null) continue;
            from.Cofre.Apply(rel.FromGives, -1); from.Cofre.Apply(rel.ToGives, +1);
            to.Cofre.Apply(rel.FromGives, +1); to.Cofre.Apply(rel.ToGives, -1);
            from.LastResults.Add($"🤝 Relação com {LabelFor(room, to)}: você deu {Descrever(rel.FromGives)} e recebeu {Descrever(rel.ToGives)}.");
            to.LastResults.Add($"🤝 Relação com {LabelFor(room, from)}: você deu {Descrever(rel.ToGives)} e recebeu {Descrever(rel.FromGives)}.");
        }

        // 4) golpe por aprovação baixa (depois de todos os efeitos)
        foreach (var p in ativos)
        {
            p.Stats.Clamp();
            // foto do fim da rodada, para os gráficos de evolução
            p.History.Add(new Snapshot
            {
                Round = room.Round,
                Dinheiro = p.Cofre.Dinheiro,
                Populacao = p.Stats.Populacao,
                Aprovacao = p.Stats.Aprovacao
            });
            if (p.Stats.Aprovacao <= 0)
            {
                p.Deposto = true;
                p.LastResults.Add("🚨 GOLPE! Aprovação zerada — a população derrubou o líder.");
            }
            else if (p.Stats.Aprovacao < 20)
            {
                p.LastResults.Add($"⚠️ Aprovação muito baixa ({p.Stats.Aprovacao}%): risco de golpe da população!");
            }
        }
    }

    private static string LabelFor(Room room, Player p)
    {
        var c = p.CountryId != null ? Catalog.FirstOrDefault(x => x.Id == p.CountryId) : null;
        return c != null ? $"{c.Emoji} {c.Name}" : p.Name;
    }

    // ----------------------------------------------------------------- enviar proposta
    public async Task<object> SendProposalAsync(string connectionId, string code, string toId, ResourceDto offer, ResourceDto request)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");

        var off = offer?.ToCofre() ?? new Cofre();
        var req = request?.ToCofre() ?? new Cofre();
        if (off.IsEmpty && req.IsEmpty) return Fail("A proposta está vazia.");

        Proposal? npcResolved = null;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Negociacao) return Fail("Só dá para negociar na fase de Negociação.");

            var from = room.Players.FirstOrDefault(p => p.Id == connectionId);
            if (from == null || !from.ChoseCountry) return Fail("Você não está na partida.");

            var isNpc = toId.StartsWith("npc:", StringComparison.OrdinalIgnoreCase);
            if (isNpc)
            {
                var npcId = toId["npc:".Length..];
                var npc = Npcs.FirstOrDefault(n => n.Id == npcId);
                if (npc == null) return Fail("NPC inválido.");

                var prop = new Proposal
                {
                    FromId = connectionId, ToId = toId, ToIsNpc = true, Offer = off, Request = req
                };

                var estado = StateOf(room, npc.Id);
                var querDele = QuerDe(room, npc, connectionId);   // já com ágio da Relação Restrita, se houver

                // NPC aceita se recebe pelo menos o que pede E o pedido cabe no que ele dá
                bool ofereceOSuficiente = off.CobreOuIgual(querDele);
                bool pedidoCabe = npc.Da.CobreOuIgual(req);

                if (estado.Bloqueado)
                {
                    prop.Status = ProposalStatus.Recusada;
                    prop.Note = $"{npc.Name} está sabotada e não vende nada por {estado.BloqueioRoundsLeft} rodada(s).";
                }
                else if (ofereceOSuficiente && pedidoCabe)
                {
                    // acerta na hora (NPC tem estoque infinito; só mexe no cofre do jogador). Permite negativo.
                    from.Cofre.Apply(off, -1);
                    from.Cofre.Apply(req, +1);
                    prop.Status = ProposalStatus.Aceita;
                    prop.Note = $"{npc.Name} aceitou.";
                }
                else
                {
                    var agio = estado.RestritaOwnerId != null && estado.RestritaOwnerId != connectionId
                        ? " (preço inflado por uma Relação Restrita de outro país)" : "";
                    prop.Status = ProposalStatus.Recusada;
                    prop.Note = $"{npc.Name} só troca {Descrever(npc.Da)} por {Descrever(querDele)}{agio}.";
                }

                room.Proposals.Add(prop);
                npcResolved = prop;
            }
            else
            {
                var to = room.Players.FirstOrDefault(p => p.Id == toId);
                if (to == null || !to.ChoseCountry) return Fail("Destinatário inválido.");
                if (toId == connectionId) return Fail("Você não pode negociar consigo mesmo.");

                room.Proposals.Add(new Proposal
                {
                    FromId = connectionId, ToId = toId, ToIsNpc = false, Offer = off, Request = req
                });
            }
        }

        await BroadcastStateAsync(room);
        return new { ok = true, resolved = npcResolved != null, status = npcResolved?.Status.ToString(), note = npcResolved?.Note };
    }

    // ----------------------------------------------------------------- comprar Relação Restrita com um NPC
    public async Task<object> BuyRestritaAsync(string connectionId, string code, string npcId)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Negociacao) return Fail("A Relação Restrita se fecha na Negociação.");

            var me = room.Players.FirstOrDefault(p => p.Id == connectionId);
            if (me == null || !me.ChoseCountry || me.Deposto) return Fail("Você não pode negociar.");

            var id = npcId.StartsWith("npc:", StringComparison.OrdinalIgnoreCase) ? npcId["npc:".Length..] : npcId;
            var npc = Npcs.FirstOrDefault(n => n.Id == id);
            if (npc == null) return Fail("NPC inválido.");

            var st = StateOf(room, npc.Id);
            if (st.RestritaOwnerId == connectionId) return Fail($"Você já tem a Relação Restrita com {npc.Name}.");
            if (st.RestritaOwnerId != null) return Fail($"{npc.Name} já tem Relação Restrita com outro país.");
            if (me.Cofre.Dinheiro < RestritaPrice) return Fail($"A Relação Restrita custa {RestritaPrice} 💰.");

            me.Cofre.Dinheiro -= RestritaPrice;
            st.RestritaOwnerId = connectionId;
            RecordEvent(room, connectionId, null, "relacao",
                $"🔒 {Label(room, connectionId)} fechou Relação Restrita com {npc.Emoji} {npc.Name}: o preço dela sobe {RestritaMarkupPct - 100}% para os demais.");
        }

        await BroadcastStateAsync(room);
        return new { ok = true };
    }

    // ----------------------------------------------------------------- responder proposta (jogador↔jogador)
    public async Task RespondProposalAsync(string connectionId, string code, string proposalId, bool accept)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Negociacao) return;

            var prop = room.Proposals.FirstOrDefault(p => p.Id == proposalId);
            if (prop == null || prop.Status != ProposalStatus.Pendente) return;
            if (prop.ToIsNpc) return;                       // NPC já resolve sozinho
            if (prop.ToId != connectionId) return;          // só o destinatário responde

            if (!accept)
            {
                prop.Status = ProposalStatus.Recusada;
                prop.Note = "Recusada.";
                return;
            }

            var from = room.Players.FirstOrDefault(p => p.Id == prop.FromId);
            var to = room.Players.FirstOrDefault(p => p.Id == prop.ToId);
            if (from == null || to == null) return;

            // acerto: remetente dá Offer e recebe Request; destinatário o inverso. Permite negativo.
            from.Cofre.Apply(prop.Offer, -1);
            from.Cofre.Apply(prop.Request, +1);
            to.Cofre.Apply(prop.Offer, +1);
            to.Cofre.Apply(prop.Request, -1);

            prop.Status = ProposalStatus.Aceita;
            prop.Note = "Aceita.";
        }

        await BroadcastStateAsync(room);
    }

    // ----------------------------------------------------------------- desconexão
    public async Task HandleDisconnectAsync(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            bool touched = false, advanced = false;
            Guid token = default;
            lock (room.Sync)
            {
                var p = room.Players.FirstOrDefault(x => x.Id == connectionId);
                if (p != null)
                {
                    p.Connected = false;
                    touched = true;
                    // quem caiu não pode travar a fase: os demais podem já estar todos prontos
                    if (room.Phase != GamePhase.Lobby && AllReady(room))
                    {
                        AdvanceLocked(room); advanced = true; token = room.PhaseToken;
                    }
                }
            }
            if (touched) await BroadcastStateAsync(room);
            if (advanced) _ = PhaseTimerAsync(room, token);
        }
    }

    // ----------------------------------------------------------------- broadcast do estado (por conexão)
    // Cada jogador recebe um estado próprio: enxerga o próprio cofre sempre, mas o cofre alheio
    // só na fase de Resultados (blefe na negociação). As propostas também são filtradas por destinatário.
    private async Task BroadcastStateAsync(Room room)
    {
        List<(string id, object dto)> payloads = new();

        lock (room.Sync)
        {
            room.LastActivityUtc = DateTime.UtcNow;   // sala viva (usado pela limpeza)
            var taken = room.Players.Where(p => p.CountryId != null)
                                    .Select(p => p.CountryId!)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool revelaTudo = room.Phase == GamePhase.Resultados;

            foreach (var viewer in room.Players.Where(p => p.Connected))
            {
                var dto = new
                {
                    code = room.Code,
                    phase = room.Phase.ToString(),
                    round = room.Round,
                    hostId = room.HostConnectionId,
                    minPlayers = MinPlayers,
                    meId = viewer.Id,
                    // cronômetro da fase (epoch ms) e prontidão
                    phaseDeadline = room.PhaseDeadlineUtc.HasValue
                        ? new DateTimeOffset(room.PhaseDeadlineUtc.Value).ToUnixTimeMilliseconds()
                        : (long?)null,
                    meReady = viewer.Ready,
                    readyCount = room.Players.Count(p => p.Connected && p.ChoseCountry && !p.Deposto && p.Ready),
                    readyTotal = room.Players.Count(p => p.Connected && p.ChoseCountry && !p.Deposto),
                    players = room.Players.Select(p =>
                    {
                        var country = p.CountryId != null ? Catalog.FirstOrDefault(c => c.Id == p.CountryId) : null;
                        bool eu = p.Id == viewer.Id;
                        bool mostraPrivado = p.CountryId != null && (eu || revelaTudo);
                        return new
                        {
                            id = p.Id,
                            name = p.Name,
                            connected = p.Connected,
                            isHost = p.Id == room.HostConnectionId,
                            isMe = eu,
                            countryId = p.CountryId,
                            countryName = country?.Name,
                            emoji = country?.Emoji,
                            title = p.Title.ToString(),
                            chose = p.ChoseCountry,
                            deposto = p.Deposto,
                            ready = p.Ready,
                            // histórico é público: alimenta os gráficos da tela de Resultados
                            history = p.History.Select(h => new
                            {
                                round = h.Round, dinheiro = h.Dinheiro, populacao = h.Populacao, aprovacao = h.Aprovacao
                            }).ToList(),
                            cofre = mostraPrivado ? CofreDto(p.Cofre) : null,
                            stats = mostraPrivado ? StatsDto(p.Stats) : null,
                            maoCount = p.Mao.Count,
                            // só o próprio jogador vê as cartas da mão; os demais só a contagem
                            mao = eu ? p.Mao.Select(h => CardDto(h)).ToList() : null,
                            // ofertas do Investimento só para o próprio dono
                            ofertas = eu ? p.Ofertas.Select(h => CardDto(h, p.Cofre)).ToList() : null,
                            lastResults = (eu || revelaTudo) ? p.LastResults.ToList() : null
                        };
                    }).ToList(),
                    countries = Catalog.Select(c => new
                    {
                        id = c.Id,
                        name = c.Name,
                        emoji = c.Emoji,
                        taken = taken.Contains(c.Id),
                        inicial = CofreDto(c.Inicial)
                    }).ToList(),
                    npcs = Npcs.Select(n =>
                    {
                        var st = StateOf(room, n.Id);
                        var querParaMim = QuerDe(room, n, viewer.Id);
                        return new
                        {
                            id = "npc:" + n.Id,
                            name = n.Name,
                            emoji = n.Emoji,
                            da = CofreDto(n.Da),
                            quer = CofreDto(querParaMim),          // já com ágio, se for o caso
                            querBase = CofreDto(n.Quer),
                            bloqueado = st.Bloqueado,
                            bloqueioRounds = st.BloqueioRoundsLeft,
                            souDonoRestrita = st.RestritaOwnerId == viewer.Id,
                            restritaDono = st.RestritaOwnerId != null ? Label(room, st.RestritaOwnerId) : null,
                            temAgio = st.RestritaOwnerId != null && st.RestritaOwnerId != viewer.Id,
                            precoRestrita = RestritaPrice
                        };
                    }).ToList(),
                    // propostas relevantes para este jogador
                    incoming = room.Proposals
                        .Where(p => !p.ToIsNpc && p.ToId == viewer.Id && p.Status == ProposalStatus.Pendente)
                        .Select(p => ProposalDto(room, p, viewer.Id)).ToList(),
                    // "Minhas negociações": as que EU enviei (qualquer status) + as que RECEBI e já foram
                    // resolvidas — senão quem aceita a proposta perde o registro dela de vista.
                    outgoing = room.Proposals
                        .Where(p => p.FromId == viewer.Id
                                 || (!p.ToIsNpc && p.ToId == viewer.Id && p.Status != ProposalStatus.Pendente))
                        .OrderByDescending(p => p.CreatedUtc).Take(10)
                        .Select(p => ProposalDto(room, p, viewer.Id)).ToList(),
                    // relações que envolvem este jogador
                    relations = room.Relations
                        .Where(r => r.FromId == viewer.Id || r.ToId == viewer.Id)
                        .Select(r => RelationDto(room, r, viewer.Id)).ToList(),
                    // efeitos contínuos são públicos
                    ongoing = room.Ongoing.Select(e => new { nome = e.Nome, emoji = e.Emoji, roundsLeft = e.RoundsLeft, source = e.SourceLabel }).ToList(),
                    // eventos da rodada: durante o jogo só os que envolvem o jogador; em Resultados, todos
                    events = room.Events
                        .Where(e => e.Round == room.Round && (revelaTudo || e.Public || e.ActorId == viewer.Id || e.TargetId == viewer.Id))
                        .Select(e => new { id = e.Id, text = e.Text, kind = e.Kind, phase = e.Phase.ToString(), actorId = e.ActorId, actorLabel = Label(room, e.ActorId), mine = e.ActorId == viewer.Id, againstMe = e.TargetId == viewer.Id })
                        .ToList()
                };
                payloads.Add((viewer.Id, dto));
            }
        }

        foreach (var (id, dto) in payloads)
            await _hub.Clients.Client(id).SendAsync("state", dto);
    }

    // ----------------------------------------------------------------- helpers de DTO / descrição
    private static object CofreDto(Cofre c) => new
    {
        dinheiro = c.Dinheiro, terra = c.Terra, petroleo = c.Petroleo,
        alimento = c.Alimento, militares = c.Militares, divida = c.Divida
    };

    private static object StatsDto(Stats s) => new
    {
        populacao = s.Populacao, credibilidade = s.Credibilidade, aprovacao = s.Aprovacao
    };

    // DTO de uma carta possuída; se <paramref name="cofre"/> vier, indica se dá para pagar (ofertas).
    private static object CardDto(HeldCard h, Cofre? cofre = null)
    {
        var def = Deck.FirstOrDefault(d => d.Id == h.CardId);
        return new
        {
            id = h.Id,
            cardId = h.CardId,
            nome = def?.Nome ?? h.CardId,
            emoji = def?.Emoji,
            descricao = def?.Descricao,
            alvo = def?.Alvo,
            custo = def != null ? CofreDto(def.Custo) : null,
            podePagar = cofre != null && def != null && cofre.CobreOuIgual(def.Custo)
        };
    }

    private object ProposalDto(Room room, Proposal p, string viewerId)
    {
        string label;
        string? emoji;
        if (p.ToIsNpc)
        {
            var npc = Npcs.FirstOrDefault(n => "npc:" + n.Id == p.ToId);
            label = npc?.Name ?? "NPC"; emoji = npc?.Emoji;
        }
        else
        {
            var other = room.Players.FirstOrDefault(x => x.Id == (p.FromId == viewerId ? p.ToId : p.FromId));
            var country = other?.CountryId != null ? Catalog.FirstOrDefault(c => c.Id == other.CountryId) : null;
            label = country?.Name ?? other?.Name ?? "?"; emoji = country?.Emoji;
        }
        return new
        {
            id = p.Id,
            fromId = p.FromId,
            toId = p.ToId,
            toIsNpc = p.ToIsNpc,
            souRemetente = p.FromId == viewerId,   // inverte a leitura de quem recebeu a proposta
            counterpartLabel = label,
            counterpartEmoji = emoji,
            offer = CofreDto(p.Offer),
            request = CofreDto(p.Request),
            status = p.Status.ToString(),
            note = p.Note
        };
    }

    private object RelationDto(Room room, Relation r, string viewerId)
    {
        bool souProponente = r.FromId == viewerId;
        var otherId = souProponente ? r.ToId : r.FromId;
        var other = room.Players.FirstOrDefault(x => x.Id == otherId);
        var country = other?.CountryId != null ? Catalog.FirstOrDefault(c => c.Id == other.CountryId) : null;
        return new
        {
            id = r.Id,
            status = r.Status.ToString(),
            souProponente,
            counterpartLabel = country?.Name ?? other?.Name ?? "?",
            counterpartEmoji = country?.Emoji,
            // sempre na ótica do jogador: o que EU dou e o que EU recebo por rodada
            euDou = CofreDto(souProponente ? r.FromGives : r.ToGives),
            euRecebo = CofreDto(souProponente ? r.ToGives : r.FromGives),
            // só o alvo de uma proposta pendente pode aceitar/recusar
            podeResponder = r.Status == RelationStatus.Pendente && r.ToId == viewerId
        };
    }

    // descrição curta de um cofre, ex.: "40 Petróleo" (usado nas mensagens dos NPCs)
    private static string Descrever(Cofre c)
    {
        var parts = new List<string>();
        void Add(int v, string nome) { if (v != 0) parts.Add($"{v} {nome}"); }
        Add(c.Dinheiro, "Dinheiro"); Add(c.Terra, "Terra"); Add(c.Petroleo, "Petróleo");
        Add(c.Alimento, "Alimento"); Add(c.Militares, "Militares"); Add(c.Divida, "Dívida");
        return parts.Count == 0 ? "nada" : string.Join(" + ", parts);
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem I,O,0,1
        return new string(Enumerable.Range(0, 4).Select(_ => chars[_rng.Next(chars.Length)]).ToArray());
    }
}
