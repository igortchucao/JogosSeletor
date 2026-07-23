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
    public string Id { get; set; } = "";      // connectionId do SignalR (MUDA a cada reconexão)
    public string Token { get; set; } = "";   // identidade estável do cliente (não muda ao reconectar)
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public bool Connected { get; set; } = true;
}

public class Room
{
    public string Code { get; set; } = "";
    public string? HostConnectionId { get; set; }   // telão (muda ao reconectar)
    public string HostToken { get; set; } = "";      // identidade estável do telão
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
    public bool InterceptWindowGuessUsed { get; set; } // já usou o chute único da janela de contato

    // --- último resultado (para animação/feedback) ---
    public string? LastResult { get; set; }      // "success" | "blocked" | "failed"
    public string? LastResultWord { get; set; }  // a palavra que fez o contato

    public object Sync { get; } = new();

    /// <summary>Última vez que algo aconteceu na sala — usado para limpar salas abandonadas.</summary>
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

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
