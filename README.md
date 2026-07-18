# Jogos — Contato + Soberania

Este serviço hospeda **dois jogos** no mesmo app ASP.NET Core (um só deploy, uma só URL):

| Rota | Jogo |
|---|---|
| `/` | menu — escolha o jogo |
| `/contato/` | **Contato** (telão) · `/contato/play.html` (celular) · hub `/hubs/contato` |
| `/soberania/` | **Soberania** · hub `/hubs/soberania` |

Cada jogo tem seu próprio Hub, Service e Models. As classes têm o mesmo nome nos dois
(`GameHub`, `GameService`), mas ficam em **namespaces diferentes** (`Contato.*` e
`Soberania.*`) — por isso o `Program.cs` sempre as qualifica.

> ⚠️ O código do Soberania mora **aqui** (`Soberania/` + `wwwroot/soberania/`).
> A pasta `repos/Soberania` é a versão antiga e standalone — não edite lá.

---

## Contato — jogo de palavras multiplayer

Versão digital do jogo "Contato". O **telão** (TV/computador) mostra um QR code; os
jogadores entram pelo **celular** e jogam na mesma sala em tempo real (SignalR).

## Como funciona

1. A cada rodada, um jogador vira o **interceptador** (rodízio automático) e digita
   uma **palavra secreta** no celular dele (ex: `FAROFA`).
2. Os outros jogadores recebem só a **1ª letra** (`F _ _ _ _ _`).
3. Na vida real, um jogador dá uma **dica** de uma palavra com aquela letra
   (ex: "corta as coisas" → FACA).
4. Enquanto isso, o **interceptador** fica **chutando** palavras que ele acha que são
   as dicas. Cada chute aparece na tela de todos como uma **palavra queimada 🔥**
   (limitado a 1 chute a cada ~2s). Isso pressiona os jogadores e "arma" um bloqueio.
5. Quem achar que combinou com alguém aperta **CONTATO** e digita a resposta (`FACA`).
   A resposta **precisa começar com as letras já reveladas** — com `F` revelado vale
   qualquer palavra com F; com `FAR` revelado, só `FARRA`, `FARTA`, etc. (acento e
   maiúscula são ignorados). Nesse momento abre uma janela de **3 segundos** para todos
   travarem a resposta **às cegas** — e o interceptador tem direito a **mais 1 chute**
   (a última chance de bloquear), além dos que já tinha queimado.
6. No fim da janela:
   - Se **2 ou mais jogadores** digitaram a **mesma palavra**:
     - Se essa palavra **estava queimada** pelo interceptador (nos chutes livres **ou**
       no chute final) → **BLOQUEADO**, nenhuma letra revelada e o interceptador ganha ponto.
     - Senão → **contato!** revela mais uma letra (`FA _ _ _ _`).
   - Se ninguém bateu, nada acontece.
7. Se os jogadores revelarem a **palavra inteira**, eles **vencem a rodada** (+2 pontos
   cada). Depois é só clicar em **Próxima rodada**.

Mínimo de **3 jogadores** (1 interceptador + 2 para fazer contato).

## Como rodar

**Pelo Visual Studio:** abra `Contato.csproj` e rode (F5).

**Pelo terminal:**

```powershell
dotnet run
```

Abra o telão no navegador (a porta aparece no console, ex: `http://localhost:5059`).

## Importante: QR code e celulares na mesma rede

Os celulares precisam abrir o link do QR, então **`localhost` não funciona no celular**.
Abra o telão pelo **IP da máquina na rede local** para o QR conter esse IP. Ex:

```powershell
# descubra seu IP local:
ipconfig   # procure o "Endereço IPv4", ex: 192.168.0.15

# rode escutando em todos os IPs:
dotnet run --urls "http://0.0.0.0:5059"
```

Depois abra o telão em `http://192.168.0.15:5059` — o QR já vai apontar para esse
endereço e os celulares na mesma rede Wi-Fi conseguem entrar. (Talvez seja preciso
liberar a porta no Firewall do Windows na primeira vez.)

## Ajustes rápidos

- **Tempo da janela de contato:** `ContactWindowSeconds` em
  [`Services/GameService.cs`](Services/GameService.cs) (padrão: 3s).
- **Intervalo entre chutes do interceptador:** `InterceptCooldownMs` no mesmo arquivo
  (padrão: 2000ms).
- **Mínimo de jogadores:** busque por `< 3` em `GameService.cs` e `wwwroot/js/screen.js`.

## Estrutura

```
Program.cs                 configuração (SignalR + arquivos estáticos)
Hubs/GameHub.cs            endpoints SignalR (chamadas do cliente)
Services/GameService.cs    estado do jogo em memória + toda a lógica + broadcast
Models/GameModels.cs       Room, Player, fases, normalização de palavras
wwwroot/index.html         TELÃO (cria a sala, mostra QR e a palavra)
wwwroot/play.html          CELULAR (entrar e jogar)
wwwroot/js/screen.js       lógica do telão
wwwroot/js/play.js         lógica do celular
wwwroot/css/style.css      visual (tema escuro)
```

O estado fica **em memória** (sem banco). Reiniciar o servidor zera as salas.
Os clients (SignalR e QR code) usam bibliotecas via CDN, então o telão/celular
precisam de internet além da rede local.
