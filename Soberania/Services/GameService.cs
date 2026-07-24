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
        new() { Id = "brasil",   Name = "Brasil",          Emoji = "🇧🇷", Inicial = new() { Dinheiro = 800, Terra = 90, Petroleo = 40, Alimento = 90, Militares = 40, Divida = 60 },  StatsInicial = new() { Populacao = 215, Credibilidade = 50, Satisfacao = 60 } },
        new() { Id = "eua",      Name = "Estados Unidos",  Emoji = "🇺🇸", Inicial = new() { Dinheiro = 1500, Terra = 70, Petroleo = 50, Alimento = 70, Militares = 90, Divida = 120 }, StatsInicial = new() { Populacao = 335, Credibilidade = 65, Satisfacao = 55 } },
        new() { Id = "russia",   Name = "Rússia",          Emoji = "🇷🇺", Inicial = new() { Dinheiro = 700, Terra = 100, Petroleo = 90, Alimento = 50, Militares = 85, Divida = 40 },  StatsInicial = new() { Populacao = 145, Credibilidade = 40, Satisfacao = 65 } },
        new() { Id = "china",    Name = "China",           Emoji = "🇨🇳", Inicial = new() { Dinheiro = 1200, Terra = 80, Petroleo = 40, Alimento = 60, Militares = 80, Divida = 70 },  StatsInicial = new() { Populacao = 1410, Credibilidade = 55, Satisfacao = 60 } },
        new() { Id = "arabia",   Name = "Arábia Saudita",  Emoji = "🇸🇦", Inicial = new() { Dinheiro = 1000, Terra = 50, Petroleo = 100, Alimento = 20, Militares = 55, Divida = 20 }, StatsInicial = new() { Populacao = 36, Credibilidade = 50, Satisfacao = 55 } },
        new() { Id = "alemanha", Name = "Alemanha",        Emoji = "🇩🇪", Inicial = new() { Dinheiro = 1100, Terra = 55, Petroleo = 20, Alimento = 55, Militares = 60, Divida = 50 },  StatsInicial = new() { Populacao = 84, Credibilidade = 70, Satisfacao = 58 } },
        new() { Id = "india",    Name = "Índia",           Emoji = "🇮🇳", Inicial = new() { Dinheiro = 600, Terra = 85, Petroleo = 30, Alimento = 80, Militares = 70, Divida = 55 },   StatsInicial = new() { Populacao = 1400, Credibilidade = 48, Satisfacao = 62 } },
        new() { Id = "nigeria",  Name = "Nigéria",         Emoji = "🇳🇬", Inicial = new() { Dinheiro = 500, Terra = 75, Petroleo = 80, Alimento = 60, Militares = 45, Divida = 65 },   StatsInicial = new() { Populacao = 220, Credibilidade = 42, Satisfacao = 57 } },
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
                Descricao = "O alvo perde credibilidade e satisfação.", Custo = new() { Dinheiro = 250 } },
        new() { Id = "golpe",       Nome = "Golpe Militar",      Emoji = "🎖️", Alvo = "Inimigo", Efeito = "derruba_satisfacao",
                Descricao = "Derruba a satisfação do alvo drasticamente.", Custo = new() { Dinheiro = 300, Militares = 40 } },
        new() { Id = "estimulo",    Nome = "Pacote de Estímulo", Emoji = "💵", Alvo = "Proprio", Efeito = "sobe_aprov_pop",
                Descricao = "Aumenta sua satisfação e sua população.", Custo = new() { Dinheiro = 250 } },
        new() { Id = "propaganda",  Nome = "Propaganda Estatal", Emoji = "📢", Alvo = "Proprio", Efeito = "sobe_credibilidade",
                Descricao = "Aumenta sua credibilidade.", Custo = new() { Dinheiro = 150 } },
        new() { Id = "petroleo",    Nome = "Descoberta de Petróleo", Emoji = "🛢️", Alvo = "Proprio", Efeito = "ganha_petroleo",
                Descricao = "Novas reservas: ganha petróleo.", Custo = new() { Dinheiro = 300, Terra = 20 } },
        new() { Id = "humanitaria", Nome = "Ajuda Humanitária",  Emoji = "🕊️", Alvo = "Inimigo", Efeito = "diplomacia",
                Descricao = "Envia ajuda: sobe a credibilidade e satisfação do alvo (diplomacia).", Custo = new() { Dinheiro = 200, Alimento = 40 } },

        // --- cartas contra países NPC (fornecedores) ---
        new() { Id = "sabotagem",   Nome = "Sabotar Fornecedor", Emoji = "💣", Alvo = "Npc", Efeito = "bloqueia_npc",
                Descricao = "Ataca um país NPC: ele para de vender por 2 rodadas.", Custo = new() { Dinheiro = 450, Militares = 25 } },
        new() { Id = "contrabando", Nome = "Golpe Diplomático",  Emoji = "🕵️", Alvo = "Npc", Efeito = "quebra_restrita",
                Descricao = "Derruba a Relação Restrita que alguém tenha com esse NPC.", Custo = new() { Dinheiro = 400 } },
    };

    // ----------------------------------------------------------------- missões (condição de vitória)
    // Cada jogador recebe UMA missão secreta ao iniciar. Cumprir = vencer a partida.
    private static readonly List<MissionDef> Missoes = new()
    {
        new() { Id = "ouro",       Titulo = "Cofre Imperial",     Emoji = "💰", Tipo = "dinheiro",     Meta = 4000,
                Dica = "Acumule dinheiro vendendo recursos aos NPCs e aplicando no mercado." },
        new() { Id = "patrimonio", Titulo = "Potência Econômica", Emoji = "🏆", Tipo = "patrimonio",   Meta = 9000,
                Dica = "Vale o patrimônio TOTAL em g (todos os recursos somados pela tabela de equivalência)." },
        new() { Id = "amado",      Titulo = "Amado pelo Povo",    Emoji = "😀", Tipo = "satisfacao",   Meta = 95,
                Dica = "Baixe os impostos e evite guerras. Satisfação sobe devagar." },
        new() { Id = "respeitado", Titulo = "Nome Impecável",     Emoji = "🎖️", Tipo = "credibilidade", Meta = 90,
                Dica = "Propaganda, ajuda humanitária e nunca difamar ninguém." },
        new() { Id = "populoso",   Titulo = "Berço do Mundo",     Emoji = "👥", Tipo = "populacao",    Meta = 400,
                Dica = "Credibilidade e satisfação altas atraem imigrantes todo turno." },
        new() { Id = "exercito",   Titulo = "Máquina de Guerra",  Emoji = "🪖", Tipo = "militares",    Meta = 200,
                Dica = "Compre militares nas trocas — eles valem 10g cada." },
        new() { Id = "petroestado",Titulo = "Petroestado",        Emoji = "🛢️", Tipo = "petroleo",     Meta = 250,
                Dica = "Compre petróleo dos NPCs e use a carta Descoberta de Petróleo." },
        new() { Id = "difamar",    Titulo = "Assassinato de Reputação", Emoji = "📰", Tipo = "destruir_imagem", Meta = 10, PrecisaAlvo = true,
                Dica = "Derrube a CREDIBILIDADE do alvo até esse patamar. Difamação e escândalo são seus amigos." },
        new() { Id = "depor",      Titulo = "Golpe de Estado",    Emoji = "🚨", Tipo = "depor",        Meta = 1, PrecisaAlvo = true,
                Dica = "Faça o alvo ser deposto: derrube a satisfação dele a -50% ou zere a população." },
        new() { Id = "semdivida",  Titulo = "Nação Soberana",     Emoji = "📈", Tipo = "semdivida",    Meta = 0,
                Dica = "Zere sua dívida (passe-a adiante numa negociação) e mantenha 2000 de caixa." },
    };

    private static void SortearMissoes(Room room)
    {
        var jogadores = room.Players.Where(p => p.ChoseCountry).ToList();
        var baralho = Missoes.OrderBy(_ => _rng.Next()).ToList();

        for (int i = 0; i < jogadores.Count; i++)
        {
            var def = baralho[i % baralho.Count];
            var m = new Mission { DefId = def.Id, Meta = def.Meta };

            if (def.PrecisaAlvo)
            {
                var possiveis = jogadores.Where(x => x.Id != jogadores[i].Id).ToList();
                // sem adversário não dá para ter missão contra alguém: cai numa de acúmulo
                if (possiveis.Count == 0)
                {
                    var alt = Missoes.First(d => !d.PrecisaAlvo);
                    m = new Mission { DefId = alt.Id, Meta = alt.Meta };
                }
                else m.TargetId = possiveis[_rng.Next(possiveis.Count)].Id;
            }
            jogadores[i].Missao = m;
        }
    }

    /// <summary>Progresso atual da missão: (quanto está, quanto precisa, cumpriu?).</summary>
    private static (int atual, int meta, bool ok) ProgressoMissao(Room room, Player p)
    {
        var m = p.Missao;
        if (m == null) return (0, 0, false);
        var def = Missoes.FirstOrDefault(d => d.Id == m.DefId);
        if (def == null) return (0, 0, false);

        var alvo = m.TargetId != null ? room.Players.FirstOrDefault(x => x.Id == m.TargetId) : null;
        int atual = def.Tipo switch
        {
            "dinheiro"        => p.Cofre.Dinheiro,
            "patrimonio"      => p.Cofre.Valor,
            "satisfacao"      => p.Stats.Satisfacao,
            "credibilidade"   => p.Stats.Credibilidade,
            "populacao"       => p.Stats.Populacao,
            "militares"       => p.Cofre.Militares,
            "petroleo"        => p.Cofre.Petroleo,
            "destruir_imagem" => alvo?.Stats.Credibilidade ?? 100,
            "depor"           => (alvo?.Deposto ?? false) ? 1 : 0,
            "semdivida"       => p.Cofre.Divida <= 0 && p.Cofre.Dinheiro >= 2000 ? 1 : 0,
            _ => 0
        };

        // nas missões de destruir imagem, cumprir é ficar ABAIXO da meta
        bool ok = def.Tipo == "destruir_imagem" ? atual <= m.Meta
                : def.Tipo == "semdivida" ? atual == 1
                : atual >= m.Meta;
        return (atual, m.Meta, ok);
    }

    /// <summary>Checa as missões no fim da rodada. A primeira cumprida encerra a partida.</summary>
    private static void AvaliaMissoes(Room room)
    {
        if (room.Acabou) return;

        foreach (var p in room.Players.Where(x => x.ChoseCountry && !x.Deposto))
        {
            var (_, _, ok) = ProgressoMissao(room, p);
            if (!ok) continue;

            p.Missao!.Concluida = true;
            p.Missao.RoundConcluida = room.Round;
            room.WinnerId = p.Id;

            var def = Missoes.First(d => d.Id == p.Missao.DefId);
            room.Events.Add(new GameEvent
            {
                Round = room.Round, Phase = room.Phase, Kind = "vitoria", Public = true,
                ActorId = p.Id,
                Text = $"🏆 {LabelFor(room, p)} cumpriu a missão \"{def.Emoji} {def.Titulo}\" e VENCEU a partida!"
            });
            p.LastResults.Add($"🏆 MISSÃO CUMPRIDA — você venceu!");
            break;   // primeira missão cumprida encerra o jogo
        }
    }

    // ----------------------------------------------------------------- aplicações financeiras
    // Risco x retorno: quanto mais rende, maior a chance de quebrar. A credibilidade e a
    // satisfação do país ajustam a taxa — quem tem nome bom capta melhor.
    private static readonly List<InvestmentDef> Investimentos = new()
    {
        new() { Id = "suica", Nome = "Títulos Suíços", Emoji = "🏦", Origem = "🇨🇭 Suíça",
                RendimentoPct = 4, RiscoPct = 3, PerdaSeQuebrarPct = 20, PrazoMin = 2, PrazoMax = 6,
                Descricao = "Porto seguro: rende pouco, quase nunca quebra." },
        new() { Id = "catar", Nome = "Fundo Soberano", Emoji = "🏗️", Origem = "🇶🇦 Catar",
                RendimentoPct = 9, RiscoPct = 15, PerdaSeQuebrarPct = 40, PrazoMin = 2, PrazoMax = 5,
                Descricao = "Petrodólares aplicados em infraestrutura." },
        new() { Id = "ucrania", Nome = "Agronegócio", Emoji = "🌻", Origem = "🇺🇦 Ucrânia",
                RendimentoPct = 13, RiscoPct = 28, PerdaSeQuebrarPct = 50, PrazoMin = 1, PrazoMax = 4,
                Descricao = "Safra farta paga bem, mas depende de estabilidade." },
        new() { Id = "venezuela", Nome = "Petro-bônus", Emoji = "🛢️", Origem = "🇻🇪 Venezuela",
                RendimentoPct = 20, RiscoPct = 45, PerdaSeQuebrarPct = 70, PrazoMin = 1, PrazoMax = 3,
                Descricao = "Rendimento altíssimo. Pode virar pó." },
        new() { Id = "interno", Nome = "Tesouro Nacional", Emoji = "🏛️", Origem = "seu próprio país",
                RendimentoPct = 7, RiscoPct = 10, PerdaSeQuebrarPct = 35, PrazoMin = 1, PrazoMax = 6,
                Descricao = "Aplica em casa: sua credibilidade e satisfação pesam MAIS aqui." },
    };

    private const int PrazoMaximoRodadas = 6;

    /// <summary>Arrecadação de uma rodada. Taxa 50 = arrecadação "normal"; 100 dobra, 0 zera.</summary>
    private static int ImpostoDe(Player p) =>
        p.Stats.Populacao * Math.Max(0, p.Stats.Satisfacao) / 100 * p.TaxaImposto / TaxaNeutra;

    public async Task SetTaxRateAsync(string connectionId, string code, int taxa)
    {
        if (!_rooms.TryGetValue(code, out var room)) return;

        lock (room.Sync)
        {
            // ajustável na fase de Ações (é uma decisão de governo, como as outras)
            if (room.Phase != GamePhase.Acoes) return;
            var p = room.Players.FirstOrDefault(x => x.Id == connectionId);
            if (p == null || !p.ChoseCountry || p.Deposto) return;
            p.TaxaImposto = Math.Clamp(taxa, 0, 100);
        }

        await BroadcastStateAsync(room);
    }

    /// <summary>Taxa efetiva: credibilidade e satisfação melhoram (ou estragam) o rendimento.</summary>
    private static int RendimentoEfetivo(InvestmentDef def, Player p)
    {
        // credibilidade acima de 50 soma; abaixo, subtrai. Satisfação pesa menos.
        int bonusCred = (p.Stats.Credibilidade - CredibilidadeNeutra) / 10;
        int bonusSatisf = p.Stats.Satisfacao / 40;
        if (def.Id == "interno") { bonusCred *= 2; bonusSatisf *= 2; }   // no tesouro interno pesa dobrado
        return Math.Max(0, def.RendimentoPct + bonusCred + bonusSatisf);
    }

    public async Task<object> InvestAsync(string connectionId, string code, string defId, int valor, int rodadas)
    {
        if (!_rooms.TryGetValue(code, out var room)) return Fail("Sala não encontrada.");
        int taxaAplicada, prazoAplicado;

        lock (room.Sync)
        {
            if (room.Phase != GamePhase.Investimento) return Fail("Só dá para aplicar na fase de Investimento.");

            var p = room.Players.FirstOrDefault(x => x.Id == connectionId);
            if (p == null || !p.ChoseCountry || p.Deposto) return Fail("Você não pode aplicar.");

            var def = Investimentos.FirstOrDefault(d => d.Id == defId);
            if (def == null) return Fail("Aplicação inválida.");

            if (valor <= 0) return Fail("Informe um valor.");
            if (p.Cofre.Dinheiro < valor) return Fail($"Você só tem {p.Cofre.Dinheiro} 💰.");
            rodadas = Math.Clamp(rodadas, def.PrazoMin, Math.Min(def.PrazoMax, PrazoMaximoRodadas));

            var taxa = RendimentoEfetivo(def, p);
            p.Cofre.Dinheiro -= valor;                       // dinheiro fica travado
            p.Aplicacoes.Add(new Investment
            {
                DefId = def.Id, Valor = valor,
                RodadasRestantes = rodadas, PrazoTotal = rodadas, RendimentoTravadoPct = taxa
            });
            taxaAplicada = taxa; prazoAplicado = rodadas;
        }

        await BroadcastStateAsync(room);
        return new { ok = true, taxa = taxaAplicada, rodadas = prazoAplicado };
    }

    /// <summary>Roda no Resultados: conta o prazo, paga o que venceu e sorteia o risco.</summary>
    private static void LiquidaAplicacoes(Room room, Player p)
    {
        foreach (var ap in p.Aplicacoes.ToList())
        {
            ap.RodadasRestantes--;
            if (ap.RodadasRestantes > 0) continue;

            var def = Investimentos.FirstOrDefault(d => d.Id == ap.DefId);
            if (def == null) { p.Aplicacoes.Remove(ap); continue; }

            // juros compostos simplificados: principal * (1 + taxa)^prazo
            double montante = ap.Valor;
            for (int i = 0; i < ap.PrazoTotal; i++) montante *= 1 + ap.RendimentoTravadoPct / 100.0;
            int total = (int)Math.Round(montante);

            if (_rng.Next(100) < def.RiscoPct)
            {
                int devolvido = ap.Valor * (100 - def.PerdaSeQuebrarPct) / 100;
                p.Cofre.Dinheiro += devolvido;
                p.LastResults.Add($"📉 {def.Emoji} {def.Nome} QUEBROU! Você aplicou {ap.Valor} e recuperou só {devolvido} 💰.");
            }
            else
            {
                p.Cofre.Dinheiro += total;
                p.LastResults.Add($"📈 {def.Emoji} {def.Nome} venceu: {ap.Valor} 💰 viraram {total} (+{total - ap.Valor}) a {ap.RendimentoTravadoPct}%/rodada.");
            }
            p.Aplicacoes.Remove(ap);
        }
    }

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

    /// <summary>Preço em g que ESTE jogador paga (inflado se outro dono detém a Relação Restrita).</summary>
    private static int PrecoDe(Room room, Npc npc, string playerId)
    {
        var st = StateOf(room, npc.Id);
        return (st.RestritaOwnerId != null && st.RestritaOwnerId != playerId)
            ? npc.Preco * RestritaMarkupPct / 100
            : npc.Preco;
    }

    // ----------------------------------------------------------------- NPCs (oferta FIXA, igual em todo jogo)
    // Cada NPC entrega "Da" e exige "Quer". Aceita automaticamente se a proposta do jogador
    // oferecer pelo menos o que ele Quer e pedir no máximo o que ele Da.
    // Valores na mesma escala dos cofres — ajuste à vontade.
    // Cada NPC só expõe o que VENDE; o preço sai da tabela de equivalência + o markup dele.
    private static readonly List<Npc> Npcs = new()
    {
        new() { Id = "venezuela", Name = "Venezuela", Emoji = "🇻🇪", Da = new() { Petroleo = 40 }, MarkupPct = 120 },
        new() { Id = "catar",     Name = "Catar",     Emoji = "🇶🇦", Da = new() { Dinheiro = 600 }, MarkupPct = 115 },
        new() { Id = "ucrania",   Name = "Ucrânia",   Emoji = "🇺🇦", Da = new() { Alimento = 80 }, MarkupPct = 120 },
        new() { Id = "suica",     Name = "Suíça",     Emoji = "🇨🇭", Da = new() { Dinheiro = 900 }, MarkupPct = 110 },
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
            SortearMissoes(room);          // cada um recebe seu objetivo secreto
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
        foreach (var p in room.Players) { p.Ready = false; p.AtaquesNaFase = 0; }
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
                st.Populacao -= Perda(st.Populacao, 8); st.Satisfacao -= 5;
                break;
            case "terremoto":
                TiraRecurso(ref dinheiro, 15, "💰"); TiraRecurso(ref terra, 10, "🗺️");
                TiraRecurso(ref militares, 15, "🪖");
                st.Populacao -= Perda(st.Populacao, 10); st.Satisfacao -= 6;
                break;
            case "enchente":
                TiraRecurso(ref alimento, 30, "🌾"); TiraRecurso(ref terra, 12, "🗺️");
                st.Populacao -= Perda(st.Populacao, 5); st.Satisfacao -= 4;
                break;
            case "furacao":
                TiraRecurso(ref dinheiro, 12, "💰"); TiraRecurso(ref alimento, 20, "🌾");
                TiraRecurso(ref militares, 10, "🪖");
                st.Populacao -= Perda(st.Populacao, 6); st.Satisfacao -= 5;
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
        if (room.Acabou) return;   // partida encerrada: ninguém avança mais

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
                if (room.Acabou) return;                               // alguém venceu: cronômetro para
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
                target.Stats.Populacao /= 2; target.Stats.Satisfacao -= 20; target.Stats.Clamp();
                AjustaCredibilidade(room, target, -10);
                return "Recursos arrasados e metade da população perdida.";
            case "reduz_cred_aprov":
                target!.Stats.Satisfacao -= 12; target.Stats.Clamp();
                AjustaCredibilidade(room, target, -15);
                return "O alvo perdeu credibilidade e satisfação.";
            case "derruba_satisfacao":
                target!.Stats.Satisfacao -= 30; target.Stats.Clamp();
                return "A satisfação do alvo despencou.";
            case "diplomacia":
                target!.Stats.Credibilidade += 10; target.Stats.Satisfacao += 8; target.Stats.Clamp();
                return "Ajuda enviada: credibilidade e satisfação do alvo subiram.";
            case "sobe_aprov_pop":
                actor.Stats.Satisfacao += 10; actor.Stats.Populacao += 5; actor.Stats.Clamp();
                return "Sua satisfação e população subiram.";
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
            // um ataque por fase: sem isso dá para invadir em Ações e de novo em Represálias
            if (a.AtaquesNaFase >= 1) return Fail("Você já atacou nesta fase.");
            a.AtaquesNaFase++;

            int pa = a.Cofre.Militares, pd = t.Cofre.Militares;
            int baixasT = Math.Min(pd, (int)Math.Ceiling(pa * AtkCasualtyRate));
            int baixasA = Math.Min(pa, (int)Math.Ceiling(pd * DefCasualtyRate));
            t.Cofre.Militares -= baixasT;
            a.Cofre.Militares -= baixasA;

            int vantagem = pa - pd;
            string extra;
            if (vantagem > 0)
            {
                // percentual (com teto), não absoluto: um país grande sofre mais em número,
                // mas ninguém é apagado do mapa por uma invasão só.
                int pctPop = Math.Clamp(vantagem / 2, 1, InvasaoPopTetoPct);
                int perdaPop = Perda(t.Stats.Populacao, pctPop);
                t.Stats.Populacao = Math.Max(0, t.Stats.Populacao - perdaPop);
                t.Stats.Satisfacao -= 5; t.Stats.Clamp();
                int saque = vantagem / 2;
                int petro = Math.Min(saque, Math.Max(0, t.Cofre.Petroleo));
                int alim = Math.Min(saque, Math.Max(0, t.Cofre.Alimento));
                t.Cofre.Petroleo -= petro; a.Cofre.Petroleo += petro;
                t.Cofre.Alimento -= alim; a.Cofre.Alimento += alim;
                extra = $" Invasão! Alvo perdeu {perdaPop}mi de população ({pctPop}%); saque de {petro}🛢️ e {alim}🌾.";
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
            t.Stats.Satisfacao -= 8; t.Stats.Clamp();
            AjustaCredibilidade(room, t, -10);
            // sujar o vizinho respinga em você: difamar custa credibilidade própria
            AjustaCredibilidade(room, a, -DefameSelfCredCost);
            RecordEvent(room, a.Id, t.Id, "difamar",
                $"📢 {Label(room, a.Id)} difamou {Label(room, t.Id)}: -10 credibilidade e -8 satisfação no alvo, -{DefameSelfCredCost} de credibilidade em quem difamou.");
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

    // ================================================================= EFEITOS EM CASCATA
    // Valores concentrados aqui de propósito — é o que vamos afinar depois.
    private const int DefameSelfCredCost = 4;        // difamar suja também quem difama (alvo perde 10)
    private const int ContagioCredibilidadePct = 50; // parceiro comercial absorve 50% da queda alheia

    // saldo migratório: positivo = gente chegando, negativo = evasão
    private const int CredibilidadeNeutra = 50;      // acima disso o país atrai; abaixo, empurra
    private const int CredPorPctMigracao = 10;       // cada 10 pontos de credibilidade = 1% de migração
    private const int SatisfacaoPorPctMigracao = 20; // cada 20 pontos de satisfação = 1% de migração
    private const int FomeDeficitPorPctPop = 2;      // cada 2 de déficit de comida = 1% de fuga
    private const int EvasaoTetoPct = 30;            // teto de fuga por rodada
    private const int ImigracaoTetoPct = 10;         // teto de entrada por rodada
    private const int FomeDeficitPorCred = 5;        // cada 5 de déficit = -1 credibilidade
    private const int FomeCredTeto = 20;

    private const int SatisfacaoGolpe = -50;         // satisfação nesse patamar derruba o líder

    // impostos: a barra que o jogador arrasta (0..100)
    private const int TaxaNeutra = 50;               // no meio, não mexe na satisfação
    private const int TaxaSatisfacaoDivisor = 5;     // cada 5 pontos de taxa acima/abaixo = 1 de satisfação

    // guerra civil
    private const int CredRiscoGuerraCivil = 30;     // abaixo disso começa o risco; em 0 é certeza
    private const int MilitarSeguro = 30;            // militares abaixo disso agravam o risco
    private const int MilitarAgravanteMax = 40;      // quanto o militar fraco soma na chance
    private const int GuerraCivilSatisf = 12;        // quanto de satisfação ela custa (era 25: virava poço sem fundo)
    private const int GuerraCivilCred = 5;           // quanto de credibilidade ela custa (era 10)
    private const int GuerraCivilCooldown = 2;       // rodadas de paz forçada depois de uma guerra civil

    private const int InvasaoPopTetoPct = 20;        // teto de população perdida numa invasão

    /// <summary>Quem tem relação comercial ATIVA com este jogador.</summary>
    private static IEnumerable<Player> ParceirosComerciais(Room room, string playerId) =>
        room.Relations
            .Where(r => r.Status == RelationStatus.Ativa && (r.FromId == playerId || r.ToId == playerId))
            .Select(r => r.FromId == playerId ? r.ToId : r.FromId)
            .Distinct()
            .Select(id => room.Players.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null && p.ChoseCountry && !p.Deposto)!;

    /// <summary>
    /// Único caminho para mexer em credibilidade. PERDAS contaminam os parceiros comerciais
    /// (fazer negócio com país desmoralizado te queima junto). Um salto só — sem recursão.
    /// </summary>
    private static void AjustaCredibilidade(Room room, Player alvo, int delta)
    {
        alvo.Stats.Credibilidade += delta;
        alvo.Stats.Clamp();
        if (delta >= 0) return;

        int contagio = (-delta) * ContagioCredibilidadePct / 100;
        if (contagio <= 0) return;

        foreach (var parceiro in ParceirosComerciais(room, alvo.Id).ToList())
        {
            parceiro.Stats.Credibilidade -= contagio;
            parceiro.Stats.Clamp();
            room.Events.Add(new GameEvent
            {
                Round = room.Round, Phase = room.Phase, Kind = "relacao",
                ActorId = "", TargetId = parceiro.Id,
                Text = $"🔗 {LabelFor(room, parceiro)} perdeu {contagio} de credibilidade por ter acordo comercial com {LabelFor(room, alvo)}."
            });
        }
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
            // arrecadação = população × satisfação × a taxa que o líder escolheu.
            // Satisfação negativa não gera imposto negativo: o povo simplesmente para de pagar.
            int imposto = ImpostoDe(p);
            p.Cofre.Dinheiro += imposto;
            p.LastResults.Add(s.Satisfacao > 0
                ? $"💰 Impostos: +{imposto} (população {s.Populacao}mi × satisfação {s.Satisfacao}% × taxa {p.TaxaImposto}%)."
                : $"💰 Impostos: +0 — com satisfação em {s.Satisfacao}% o povo não paga imposto.");

            // apertar o povo rende agora e cobra depois; aliviar acalma
            int deltaSatisf = (TaxaNeutra - p.TaxaImposto) / TaxaSatisfacaoDivisor;
            if (deltaSatisf != 0)
            {
                s.Satisfacao += deltaSatisf;
                s.Clamp();
                p.LastResults.Add(deltaSatisf < 0
                    ? $"😠 Carga tributária em {p.TaxaImposto}%: {deltaSatisf} de satisfação."
                    : $"😀 Alívio tributário em {p.TaxaImposto}%: +{deltaSatisf} de satisfação.");
            }

            // aplicações que venceram voltam com rendimento (ou quebram)
            LiquidaAplicacoes(room, p);
            // (o crescimento populacional agora está no saldo migratório, mais abaixo)
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

        // 3b) SALDO MIGRATÓRIO: credibilidade e satisfação atraem ou empurram; fome só empurra.
        // Positivo = gente chegando. Negativo = evasão.
        foreach (var p in ativos)
        {
            var s = p.Stats;
            int deficit = p.Cofre.Alimento < 0 ? -p.Cofre.Alimento : 0;

            if (deficit > 0)
            {
                int perdaCred = Math.Min(FomeCredTeto, deficit / FomeDeficitPorCred);
                if (perdaCred > 0)
                {
                    AjustaCredibilidade(room, p, -perdaCred);
                    p.LastResults.Add($"🍽️ A fome (déficit de {deficit} 🌾) derrubou {perdaCred} de credibilidade.");
                }
            }

            // credibilidade alta atrai imigrante; baixa faz o povo procurar outro lugar
            int pctCred = (s.Credibilidade - CredibilidadeNeutra) / CredPorPctMigracao;
            int pctSatisf = s.Satisfacao / SatisfacaoPorPctMigracao;      // negativo empurra sozinho
            int pctFome = -(deficit / FomeDeficitPorPctPop);

            int saldo = Math.Clamp(pctCred + pctSatisf + pctFome, -EvasaoTetoPct, ImigracaoTetoPct);
            s.EvasaoPct = saldo;
            if (saldo == 0) { p.LastResults.Add("🧳 Migração estável: ninguém entrou nem saiu."); continue; }

            int pessoas = Perda(s.Populacao, Math.Abs(saldo));
            if (saldo > 0)
            {
                s.Populacao += pessoas;
                p.LastResults.Add($"🧳 Imigração de +{saldo}% (credibilidade {s.Credibilidade}, satisfação {s.Satisfacao}%): +{pessoas}mi de pessoas.");
            }
            else
            {
                s.Populacao -= pessoas;
                var causa = deficit > 0 ? "fome, " : "";
                p.LastResults.Add($"🧳 Evasão de {saldo}% ({causa}credibilidade {s.Credibilidade}, satisfação {s.Satisfacao}%): {pessoas}mi deixaram o país.");
            }
        }

        // 3c) GUERRA CIVIL: credibilidade baixa é o gatilho; militar fraco agrava.
        // Credibilidade 0 = 100% de chance. Depois de uma, o país ganha rodadas de paz forçada
        // para poder se reerguer — sem isso vira poço sem fundo (a guerra civil alimenta a próxima).
        foreach (var p in ativos.ToList())
        {
            var s = p.Stats;
            if (p.PazInternaRounds > 0)
            {
                p.PazInternaRounds--;
                p.LastResults.Add($"🕊️ Trégua interna: o país se recompõe da guerra civil ({p.PazInternaRounds} rodada(s) de paz).");
                continue;
            }
            if (s.Credibilidade >= CredRiscoGuerraCivil) continue;

            int chance = (CredRiscoGuerraCivil - s.Credibilidade) * 100 / CredRiscoGuerraCivil;
            int mil = Math.Max(0, p.Cofre.Militares);
            if (mil < MilitarSeguro)
                chance += (MilitarSeguro - mil) * MilitarAgravanteMax / MilitarSeguro;
            chance = Math.Clamp(chance, 0, 100);

            if (_rng.Next(100) >= chance)
            {
                p.LastResults.Add($"⚠️ Tensão interna: {chance}% de risco de guerra civil (credibilidade {s.Credibilidade}, militares {mil}).");
                continue;
            }

            int perdaMil = Perda(p.Cofre.Militares, 40);
            int perdaPop = Perda(s.Populacao, 10);
            p.Cofre.Militares -= perdaMil;
            s.Populacao -= perdaPop;
            s.Satisfacao -= GuerraCivilSatisf;
            AjustaCredibilidade(room, p, -GuerraCivilCred);
            p.PazInternaRounds = GuerraCivilCooldown;
            s.Clamp();

            p.LastResults.Add($"🔥 GUERRA CIVIL! (chance era {chance}%) -{perdaMil}🪖, -{perdaPop}mi de população, -{GuerraCivilSatisf} satisfação. (Trégua pelas próximas {GuerraCivilCooldown} rodadas.)");
            room.Events.Add(new GameEvent
            {
                Round = room.Round, Phase = room.Phase, Kind = "desastre", Public = true,
                ActorId = "", TargetId = p.Id,
                Text = $"🔥 Guerra civil em {LabelFor(room, p)}! O país se volta contra o próprio líder."
            });
        }

        // 4) golpe por satisfação baixa (depois de todos os efeitos)
        foreach (var p in ativos)
        {
            p.Stats.Clamp();
            // foto do fim da rodada, para os gráficos de evolução
            p.History.Add(new Snapshot
            {
                Round = room.Round,
                Dinheiro = p.Cofre.Dinheiro,
                Populacao = p.Stats.Populacao,
                Satisfacao = p.Stats.Satisfacao
            });
            // país sem povo deixa de existir (antes virava um zumbi imortal sem arrecadação)
            if (p.Stats.Populacao <= 0)
            {
                p.Deposto = true;
                p.LastResults.Add("💀 COLAPSO! O país ficou sem população e deixou de existir.");
                room.Events.Add(new GameEvent
                {
                    Round = room.Round, Phase = room.Phase, Kind = "desastre", Public = true,
                    ActorId = "", TargetId = p.Id,
                    Text = $"💀 {LabelFor(room, p)} entrou em colapso: sem povo, o país saiu do mapa."
                });
            }
            else if (p.Stats.Satisfacao <= SatisfacaoGolpe)
            {
                p.Deposto = true;
                p.LastResults.Add($"🚨 GOLPE! Satisfação em {p.Stats.Satisfacao}% — a população derrubou o líder.");
                room.Events.Add(new GameEvent
                {
                    Round = room.Round, Phase = room.Phase, Kind = "desastre", Public = true,
                    ActorId = "", TargetId = p.Id,
                    Text = $"🚨 Golpe popular em {LabelFor(room, p)}! O líder foi deposto."
                });
            }
            else if (p.Stats.Satisfacao < 0)
            {
                p.LastResults.Add($"⚠️ Povo revoltado ({p.Stats.Satisfacao}%): abaixo de {SatisfacaoGolpe}% o líder cai.");
            }
        }

        // 5) missões: a primeira cumprida encerra a partida
        AvaliaMissoes(room);
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
                int preco = PrecoDe(room, npc, connectionId);     // já com ágio da Relação Restrita, se houver

                // Agora vale QUALQUER mistura de recursos: o que conta é o valor total em g.
                bool pagouOPreco = off.Valor >= preco;
                bool pedidoCabe = npc.Da.CobreOuIgual(req);

                if (estado.Bloqueado)
                {
                    prop.Status = ProposalStatus.Recusada;
                    prop.Note = $"{npc.Name} está sabotada e não vende nada por {estado.BloqueioRoundsLeft} rodada(s).";
                }
                else if (pagouOPreco && pedidoCabe)
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
                    prop.Note = !pedidoCabe
                        ? $"{npc.Name} só vende {Descrever(npc.Da)}."
                        : $"{npc.Name} pede {preco}g e você ofereceu só {off.Valor}g{agio}.";
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
                    // tabela de equivalência: quanto cada recurso vale em g (o front soma o "valor total")
                    tabelaValores = new
                    {
                        dinheiro = Cofre.ValorDinheiro, militares = Cofre.ValorMilitares,
                        petroleo = Cofre.ValorPetroleo, alimento = Cofre.ValorAlimento,
                        terra = Cofre.ValorTerra, divida = Cofre.ValorDivida
                    },
                    // cronômetro da fase (epoch ms) e prontidão
                    phaseDeadline = room.PhaseDeadlineUtc.HasValue
                        ? new DateTimeOffset(room.PhaseDeadlineUtc.Value).ToUnixTimeMilliseconds()
                        : (long?)null,
                    meReady = viewer.Ready,
                    // fim de jogo
                    acabou = room.Acabou,
                    vencedorId = room.WinnerId,
                    vencedorLabel = room.WinnerId != null ? Label(room, room.WinnerId) : null,
                    euVenci = room.WinnerId == viewer.Id,
                    // missão: SÓ o dono vê a dele
                    missao = MissaoDto(room, viewer),
                    // barra de impostos + o que o servidor calcularia com a taxa atual
                    taxaImposto = viewer.TaxaImposto,
                    impostoPrevisto = ImpostoDe(viewer),
                    satisfacaoPrevista = (TaxaNeutra - viewer.TaxaImposto) / TaxaSatisfacaoDivisor,
                    taxaNeutra = TaxaNeutra,
                    taxaSatisfacaoDivisor = TaxaSatisfacaoDivisor,
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
                                round = h.Round, dinheiro = h.Dinheiro, populacao = h.Populacao, satisfacao = h.Satisfacao
                            }).ToList(),
                            cofre = mostraPrivado ? CofreDto(p.Cofre) : null,
                            stats = mostraPrivado ? StatsDto(p.Stats) : null,
                            maoCount = p.Mao.Count,
                            // só o próprio jogador vê as cartas da mão; os demais só a contagem
                            mao = eu ? p.Mao.Select(h => CardDto(h)).ToList() : null,
                            // ofertas do Investimento só para o próprio dono
                            ofertas = eu ? p.Ofertas.Select(h => CardDto(h, p.Cofre)).ToList() : null,
                            // aplicações: o dono vê os detalhes; os outros só o total travado
                            aplicacoesCount = p.Aplicacoes.Count,
                            aplicacoes = eu ? p.Aplicacoes.Select(a =>
                            {
                                var d = Investimentos.FirstOrDefault(x => x.Id == a.DefId);
                                return new
                                {
                                    id = a.Id, nome = d?.Nome ?? a.DefId, emoji = d?.Emoji, origem = d?.Origem,
                                    valor = a.Valor, taxa = a.RendimentoTravadoPct,
                                    rodadasRestantes = a.RodadasRestantes, prazoTotal = a.PrazoTotal,
                                    risco = d?.RiscoPct ?? 0
                                };
                            }).ToList() : null,
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
                    // catálogo de aplicações, com a taxa JÁ ajustada para este jogador
                    investimentos = Investimentos.Select(d => new
                    {
                        id = d.Id, nome = d.Nome, emoji = d.Emoji, origem = d.Origem,
                        descricao = d.Descricao, rendimentoBase = d.RendimentoPct,
                        rendimentoEfetivo = RendimentoEfetivo(d, viewer),
                        risco = d.RiscoPct, perdaSeQuebrar = d.PerdaSeQuebrarPct,
                        prazoMin = d.PrazoMin, prazoMax = d.PrazoMax
                    }).ToList(),
                    npcs = Npcs.Select(n =>
                    {
                        var st = StateOf(room, n.Id);
                        return new
                        {
                            id = "npc:" + n.Id,
                            name = n.Name,
                            emoji = n.Emoji,
                            da = CofreDto(n.Da),               // só o que ele VENDE fica exposto
                            preco = PrecoDe(room, n, viewer.Id), // preço em g (com ágio, se houver)
                            precoBase = n.Preco,
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

    private static object? MissaoDto(Room room, Player p)
    {
        if (p.Missao == null) return null;
        var def = Missoes.FirstOrDefault(d => d.Id == p.Missao.DefId);
        if (def == null) return null;
        var (atual, meta, ok) = ProgressoMissao(room, p);
        var alvo = p.Missao.TargetId != null ? room.Players.FirstOrDefault(x => x.Id == p.Missao.TargetId) : null;

        return new
        {
            titulo = def.Titulo,
            emoji = def.Emoji,
            tipo = def.Tipo,
            dica = def.Dica,
            meta,
            atual,
            concluida = p.Missao.Concluida || ok,
            alvoLabel = alvo != null ? LabelFor(room, alvo) : null,
            // barra de progresso (nas de destruir imagem o progresso é inverso)
            pct = def.Tipo == "destruir_imagem"
                ? (atual <= meta ? 100 : Math.Clamp((100 - atual) * 100 / Math.Max(1, 100 - meta), 0, 99))
                : Math.Clamp(meta <= 0 ? (ok ? 100 : 0) : atual * 100 / meta, 0, 100)
        };
    }

    private static object StatsDto(Stats s) => new
    {
        populacao = s.Populacao, credibilidade = s.Credibilidade,
        satisfacao = s.Satisfacao, evasao = s.EvasaoPct
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
