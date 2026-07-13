using System.Globalization;
using System.Text;

namespace Contato.Models;

public enum GamePhase
{
    Lobby,        // esperando jogadores
    AwaitingWord, // interceptador está digitando a palavra secreta
    Playing,      // rodada em andamento, jogadores dão dicas
    ContactWindow,// alguém apertou CONTATO, janela aberta
    RoundOver     // rodada terminou (jogadores venceram)
}

public class Player
{
    public string Id { get; set; } = "";   // connectionId do SignalR
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public bool Connected { get; set; } = true;
}

public class Room
{
    public string Code { get; set; } = "";
    public string? HostConnectionId { get; set; }
    public List<Player> Players { get; } = new();
    public GamePhase Phase { get; set; } = GamePhase.Lobby;

    // --- rodada atual ---
    public string? InterceptadorId { get; set; }
    public string SecretWord { get; set; } = "";   // já em MAIÚSCULAS, com acentos, só para exibir letras
    public int RevealedCount { get; set; }
    public int RotationIndex { get; set; }         // controla o rodízio do interceptador

    // --- janela de contato ---
    public Guid ContactToken { get; set; }
    public DateTime? ContactDeadlineUtc { get; set; }
    public Dictionary<string, string> Submissions { get; } = new(); // playerId -> palpite cru

    // chutes do interceptador na fase livre ("palavras queimadas"), em MAIÚSCULAS
    public List<string> InterceptGuesses { get; } = new();
    public DateTime? LastInterceptGuessUtc { get; set; }

    // --- último resultado (para animação/feedback) ---
    public string? LastResult { get; set; }      // "success" | "blocked" | "failed"
    public string? LastResultWord { get; set; }  // a palavra que fez o contato

    public object Sync { get; } = new();

    public int WordLength => SecretWord.Length;

    /// <summary>Normaliza um palpite: MAIÚSCULO, sem acentos, só letras/números.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var formD = s.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
