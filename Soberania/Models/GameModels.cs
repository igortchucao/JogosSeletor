namespace Soberania.Models;

/// <summary>
/// Fases de uma rodada. A ordem do enum é a ordem em que o host avança.
/// (As cartas de Ação/Represália em si ficam pra depois — aqui só existe o palco.)
/// </summary>
public enum GamePhase
{
    Lobby,        // esperando jogadores e escolha de países
    Negociacao,   // enviar propostas de troca para jogadores/NPCs
    Investimento, // sacar 3 cartas e comprar as que puder pagar (vão para a mão)
    Acoes,        // usar as cartas compradas (efeitos definidos depois)
    Represarias,  // reação a quem te afetou na fase de Ações
    Resultados    // soma tudo e mostra os cofres de cada país
}

public enum LeaderTitle
{
    Presidente,
    Ditador,
    Rei
}

/// <summary>
/// O cofre de um país: os 6 recursos que circulam no jogo.
/// Por enquanto os valores só são exibidos — a lógica de trocas/mercado/resultados vem depois.
/// </summary>
public class Cofre
{
    public int Dinheiro { get; set; }
    public int Terra { get; set; }
    public int Petroleo { get; set; }
    public int Alimento { get; set; }
    public int Militares { get; set; }
    public int Divida { get; set; }

    public Cofre Clone() => new()
    {
        Dinheiro = Dinheiro,
        Terra = Terra,
        Petroleo = Petroleo,
        Alimento = Alimento,
        Militares = Militares,
        Divida = Divida
    };

    /// <summary>Aplica os recursos de <paramref name="delta"/> a este cofre (sign +1 soma, -1 subtrai). Permite negativo.</summary>
    public void Apply(Cofre delta, int sign)
    {
        Dinheiro  += sign * delta.Dinheiro;
        Terra     += sign * delta.Terra;
        Petroleo  += sign * delta.Petroleo;
        Alimento  += sign * delta.Alimento;
        Militares += sign * delta.Militares;
        Divida    += sign * delta.Divida;
    }

    public bool IsEmpty =>
        Dinheiro == 0 && Terra == 0 && Petroleo == 0 && Alimento == 0 && Militares == 0 && Divida == 0;

    /// <summary>Devolve uma cópia com todos os recursos escalados por uma porcentagem (ex.: 160 = +60%).</summary>
    public Cofre Scale(int pct) => new()
    {
        Dinheiro  = Dinheiro  * pct / 100,
        Terra     = Terra     * pct / 100,
        Petroleo  = Petroleo  * pct / 100,
        Alimento  = Alimento  * pct / 100,
        Militares = Militares * pct / 100,
        Divida    = Divida    * pct / 100,
    };

    /// <summary>
    /// Tabela de equivalência: quanto vale 1 unidade de cada recurso, em "g" (a moeda do jogo).
    /// Base do Igor: 1 soldado = 1g e 1 petróleo = 2g (o dobro do soldado). Os demais seguem a
    /// mesma proporção, escalados para o tamanho da economia. Dívida vale NEGATIVO: passar dívida
    /// adiante tira valor de quem recebe.
    /// </summary>
    public const int ValorDinheiro  = 1;
    public const int ValorMilitares = 10;
    public const int ValorPetroleo  = 20;   // = 2 soldados
    public const int ValorAlimento  = 8;
    public const int ValorTerra     = 25;
    public const int ValorDivida    = -5;

    /// <summary>Converte o pacote inteiro para um valor único em g — é o que permite comparar maçã com laranja.</summary>
    public int Valor =>
        Dinheiro  * ValorDinheiro  + Militares * ValorMilitares +
        Petroleo  * ValorPetroleo  + Alimento  * ValorAlimento  +
        Terra     * ValorTerra     + Divida    * ValorDivida;

    /// <summary>Todo recurso de A é &gt;= ao de B (usado nas regras do NPC).</summary>
    public bool CobreOuIgual(Cofre b) =>
        Dinheiro >= b.Dinheiro && Terra >= b.Terra && Petroleo >= b.Petroleo &&
        Alimento >= b.Alimento && Militares >= b.Militares && Divida >= b.Divida;
}

/// <summary>
/// Itens internos do país que NÃO se compram nem se trocam. Regidos pelo jogo:
/// população (renda por impostos), credibilidade (destrava acordos, cresce a população)
/// e satisfação (baixa demais = risco de golpe).
/// </summary>
public class Stats
{
    public int Populacao { get; set; }        // em milhões
    public int Credibilidade { get; set; }    // 0..100 — seu nome lá fora
    public int Satisfacao { get; set; }        // -100..+100 — o humor do povo (0 = indiferente)
    public int EvasaoPct { get; set; }         // % da população que fugiu na última rodada

    public Stats Clone() => new()
    {
        Populacao = Populacao, Credibilidade = Credibilidade,
        Satisfacao = Satisfacao, EvasaoPct = EvasaoPct
    };

    // credibilidade 0..100; satisfação vai a negativo (povo revoltado); população nunca negativa
    public void Clamp()
    {
        Credibilidade = Math.Clamp(Credibilidade, 0, 100);
        Satisfacao = Math.Clamp(Satisfacao, -100, 100);
        if (Populacao < 0) Populacao = 0;
    }
}

/// <summary>Definição fixa de uma carta (baralho do Investimento). Efeitos são aplicados na fase de Ações.</summary>
public class CardDef
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Emoji { get; set; } = "";
    public Cofre Custo { get; set; } = new();
    public string Alvo { get; set; } = "Nenhum";  // "Inimigo" | "Todos" | "Proprio" | "Nenhum"
    public string Efeito { get; set; } = "";       // chave interpretada na fase de Ações (depois)
}

/// <summary>
/// Opção de aplicação financeira (catálogo fixo). Cada uma tem rendimento, risco e país de origem.
/// O rendimento efetivo ainda é ajustado pela credibilidade e satisfação de quem aplica.
/// </summary>
public class InvestmentDef
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Emoji { get; set; } = "";
    public string Origem { get; set; } = "";        // país de origem (sabor + tema)
    public int RendimentoPct { get; set; }           // % por rodada sobre o valor aplicado
    public int RiscoPct { get; set; }                // chance de dar errado no vencimento
    public int PerdaSeQuebrarPct { get; set; }       // quanto do principal se perde se der errado
    public int PrazoMin { get; set; } = 1;
    public int PrazoMax { get; set; } = 5;
    public string Descricao { get; set; } = "";
}

/// <summary>Aplicação ativa de um jogador: dinheiro travado até vencer.</summary>
public class Investment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DefId { get; set; } = "";
    public int Valor { get; set; }                   // principal aplicado
    public int RodadasRestantes { get; set; }
    public int PrazoTotal { get; set; }
    public int RendimentoTravadoPct { get; set; }    // taxa efetiva no momento da aplicação
}

/// <summary>Instância de carta que um jogador possui (oferta no Investimento ou carta na mão).</summary>
public class HeldCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CardId { get; set; } = "";
}

/// <summary>DTO recebido do cliente com as quantidades de cada recurso numa oferta/pedido.</summary>
public class ResourceDto
{
    public int Dinheiro { get; set; }
    public int Terra { get; set; }
    public int Petroleo { get; set; }
    public int Alimento { get; set; }
    public int Militares { get; set; }
    public int Divida { get; set; }

    /// <summary>Converte para Cofre já saneado (sem quantidades negativas na entrada).</summary>
    public Cofre ToCofre() => new()
    {
        Dinheiro  = Math.Max(0, Dinheiro),
        Terra     = Math.Max(0, Terra),
        Petroleo  = Math.Max(0, Petroleo),
        Alimento  = Math.Max(0, Alimento),
        Militares = Math.Max(0, Militares),
        Divida    = Math.Max(0, Divida),
    };
}

/// <summary>País-NPC com oferta fixa (o mesmo em todo jogo). Aceita se o jogador oferecer o que ele pede.</summary>
public class Npc
{
    public string Id { get; set; } = "";     // referenciado como "npc:<id>"
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
    public Cofre Da { get; set; } = new();     // o que o NPC vende (a única coisa que ele expõe)
    public int MarkupPct { get; set; } = 125;  // ele cobra acima do valor de tabela — é o lucro dele

    /// <summary>Preço em g. O jogador paga com QUALQUER mistura de recursos que some esse valor.</summary>
    public int Preco => Da.Valor * MarkupPct / 100;
}

public enum ProposalStatus { Pendente, Aceita, Recusada }

public class Proposal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";     // connectionId de jogador OU "npc:<id>"
    public bool ToIsNpc { get; set; }
    public Cofre Offer { get; set; } = new();   // o que o remetente dá
    public Cofre Request { get; set; } = new(); // o que o remetente quer receber
    public ProposalStatus Status { get; set; } = ProposalStatus.Pendente;
    public string? Note { get; set; }           // motivo de recusa etc.
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Estado MUTÁVEL de um NPC dentro de UMA sala (o catálogo `Npcs` é fixo e compartilhado,
/// então nada que muda durante a partida pode morar lá).
/// </summary>
public class NpcState
{
    public int BloqueioRoundsLeft { get; set; }        // sabotado: para de vender por N rodadas
    public bool Bloqueado => BloqueioRoundsLeft > 0;
    public string? RestritaOwnerId { get; set; }        // dono da Relação Restrita (encarece p/ os outros)
}

public enum RelationStatus { Pendente, Ativa }

/// <summary>
/// Relação comercial recorrente entre dois jogadores. Uma vez Ativa, a troca acontece sozinha
/// TODO turno no Resultados (From dá FromGives e recebe ToGives) até alguém cortar.
/// </summary>
public class Relation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromId { get; set; } = "";      // quem propôs
    public string ToId { get; set; } = "";        // quem precisa aceitar
    public Cofre FromGives { get; set; } = new();  // o que From entrega por rodada
    public Cofre ToGives { get; set; } = new();    // o que To entrega por rodada
    public RelationStatus Status { get; set; } = RelationStatus.Pendente;
}

/// <summary>Efeito de carta que dura vários turnos (ex.: COVID). Aplicado no Resultados enquanto durar.</summary>
public class OngoingEffect
{
    public string Efeito { get; set; } = "";
    public int RoundsLeft { get; set; }
    public string SourceLabel { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Emoji { get; set; } = "";
}

/// <summary>Registro do que aconteceu (ataques, difamação, cartas). Alimenta Represálias e Resultados.</summary>
public class GameEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Round { get; set; }
    public GamePhase Phase { get; set; }      // fase em que ocorreu (só agressão em Ações dá direito a revide)
    public string ActorId { get; set; } = "";
    public string? TargetId { get; set; }
    public string Text { get; set; } = "";
    public string Kind { get; set; } = "";   // "militar" | "difamar" | "carta" | "relacao" | "desastre"
    public bool Public { get; set; }          // desastre natural é notícia: todos veem na hora
}

/// <summary>Catálogo de países que um jogador pode assumir. Recursos iniciais assimétricos, por sabor.</summary>
public class Country
{
    public string Id { get; set; } = "";     // slug estável (ex.: "brasil")
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";   // bandeira
    public Cofre Inicial { get; set; } = new();
    public Stats StatsInicial { get; set; } = new();
}

public class Player
{
    public string Id { get; set; } = "";      // connectionId do SignalR (MUDA a cada reconexão)
    public string Token { get; set; } = "";   // identidade estável do cliente (não muda ao reconectar)
    public string Name { get; set; } = "";
    public bool Connected { get; set; } = true;

    // escolha no lobby
    public string? CountryId { get; set; }
    public LeaderTitle Title { get; set; } = LeaderTitle.Presidente;

    // cofre e itens internos atuais (nascem do país escolhido)
    public Cofre Cofre { get; set; } = new();
    public Stats Stats { get; set; } = new();

    // Investimento: 3 cartas ofertadas no turno e a mão comprada
    public List<HeldCard> Ofertas { get; } = new();
    public List<HeldCard> Mao { get; } = new();

    // aplicações financeiras em andamento (dinheiro travado rendendo)
    public List<Investment> Aplicacoes { get; } = new();

    // Resultados: linhas do que mudou no último cálculo
    public List<string> LastResults { get; } = new();
    public bool Deposto { get; set; }   // golpe: satisfação chegou a zero
    public bool Ready { get; set; }     // marcou "pronto" na fase atual (zera a cada fase)
    public int TaxaImposto { get; set; } = 50;   // 0..100 — quanto você espreme a população
    public int PazInternaRounds { get; set; }    // rodadas de imunidade após uma guerra civil
    public int AtaquesNaFase { get; set; }       // 1 ataque militar por fase (zera a cada fase)

    // histórico por rodada, para os gráficos da tela de Resultados
    public List<Snapshot> History { get; } = new();

    public bool ChoseCountry => CountryId != null;
}

/// <summary>Foto do país ao fim de uma rodada — alimenta os gráficos de evolução.</summary>
public class Snapshot
{
    public int Round { get; set; }
    public int Dinheiro { get; set; }
    public int Populacao { get; set; }
    public int Satisfacao { get; set; }
}

public class Room
{
    public string Code { get; set; } = "";
    public string? HostConnectionId { get; set; }
    public string HostToken { get; set; } = "";   // quem é o host de verdade, sobrevive à reconexão
    public List<Player> Players { get; } = new();
    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public int Round { get; set; }             // 0 no lobby; começa em 1 ao iniciar o jogo

    // a fase troca sozinha: quando todos ficam prontos OU quando o tempo acaba
    public DateTime? PhaseDeadlineUtc { get; set; }
    public Guid PhaseToken { get; set; }       // invalida o cronômetro de uma fase já superada

    public List<Proposal> Proposals { get; } = new();
    public List<Relation> Relations { get; } = new();
    public Dictionary<string, NpcState> NpcStates { get; } = new();   // por NPC, só desta sala
    public List<OngoingEffect> Ongoing { get; } = new();
    public List<GameEvent> Events { get; } = new();

    public object Sync { get; } = new();

    /// <summary>Última vez que algo aconteceu na sala — usado para limpar salas abandonadas.</summary>
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}
