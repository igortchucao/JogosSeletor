# Handoff — o Soberania agora roda dentro do repo "Contato"

> **TL;DR:** o Soberania **não é mais um projeto separado**. O código dele foi movido
> para dentro do projeto **JogosSeletor**, que hospeda **dois jogos** no mesmo
> serviço ASP.NET Core. A antiga pasta `repos\Soberania` **foi apagada**.

---

## Onde o código está agora

Tudo vive num projeto só, chamado **JogosSeletor**
(repo git: https://github.com/igortchucao/Contato).

```
JogosSeletor/
├─ Program.cs                  ← registra os DOIS jogos
├─ JogosSeletor.csproj         ← projeto único (.NET 10)
├─ Infrastructure/             ← rate limit (vale para os dois jogos)
│
├─ Contato/                    ← jogo CONTATO (namespace Contato.*)
│   ├─ Hubs/GameHub.cs
│   ├─ Models/GameModels.cs
│   └─ Services/GameService.cs
│
├─ Soberania/                  ← jogo SOBERANIA (namespace Soberania.*)
│   ├─ Hubs/GameHub.cs
│   ├─ Models/GameModels.cs
│   └─ Services/GameService.cs
│
└─ wwwroot/
    ├─ index.html              ← menu: escolhe o jogo
    ├─ contato/                ← front do Contato (index, play, js, css)
    └─ soberania/              ← front do Soberania (index, js, css)
```

## Rotas

| URL | O que é |
|---|---|
| `/` | menu com os dois jogos |
| `/soberania/` | **o Soberania** |
| `/contato/` | telão do Contato (`/contato/play.html` = celular) |
| `/healthz` | health check (responde `ok`) |

**Hubs SignalR** (mudaram — antes cada projeto usava `/gamehub`):

| Jogo | Rota do hub |
|---|---|
| Soberania | `/hubs/soberania` |
| Contato | `/hubs/contato` |

No front do Soberania isso está em `wwwroot/soberania/js/app.js`:
```js
.withUrl("/hubs/soberania")
```

## ⚠️ Convenção crítica: namespaces

Os dois jogos têm classes com **exatamente os mesmos nomes** (`GameHub`,
`GameService`, `Room`, `Player`, `GamePhase`). Eles só não colidem porque estão em
**namespaces diferentes**: `Contato.*` e `Soberania.*`.

Por isso o `Program.cs` **sempre qualifica**:

```csharp
builder.Services.AddSingleton<Contato.Services.GameService>();
builder.Services.AddSingleton<Soberania.Services.GameService>();

app.MapHub<Contato.Hubs.GameHub>("/hubs/contato");
app.MapHub<Soberania.Hubs.GameHub>("/hubs/soberania");
```

> Nunca faça `using Contato.Services;` **e** `using Soberania.Services;` no mesmo
> arquivo — vira ambiguidade de `GameService`. Qualifique pelo namespace.

Dentro dos arquivos do Soberania, os `using Soberania.*` funcionam normalmente —
cada jogo é autocontido.

## Como rodar e testar

```bash
cd C:/Users/igors/source/repos/Contato
dotnet build
dotnet run --no-launch-profile --urls "http://localhost:5059"
```

Depois abra `http://localhost:5059/soberania/`.

Já foi testado e **funciona**: cria sala, mostra o lobby com países
(🇧🇷 🇺🇸 🇷🇺 🇨🇳 🇸🇦) e títulos (Presidente/Ditador/Rei), com o hub conectando
por WebSocket.

## Deploy

- **Hospedagem:** Render (Docker, plano free) — `https://contato-acmu.onrender.com`
- **Um deploy serve os dois jogos** (era esse o motivo de juntar: 1 serviço cabe nas
  750h/mês do plano free; 2 serviços 24/7 estouram).
- O `Dockerfile` usa `DOTNET_gcServer=0` (Workstation GC) — o Server GC estourava os
  512 MB do plano free e causava crash `exit 139`.

> 🔴 **Pendência:** o auto-deploy do Render parou de pegar os commits novos. O site
> publicado ainda roda uma versão **antiga** (só Contato, sem menu, sem Soberania).
> É preciso ir no painel do Render → serviço `contato` → **Manual Deploy → Deploy
> latest commit**. Vale conferir também **Settings → Auto-Deploy = Yes**.

## Estado do código (importante)

**Commitado e no GitHub** (até `44b4355`):
- Fusão dos dois jogos com menu (`57c5fc0`)
- Fix do GC no Dockerfile
- Botão "⟳ Nova rodada" no telão do Contato

**Work in progress, NÃO commitado** (mudanças locais):
- `Infrastructure/RateLimitFilter.cs` (novo) — rate limit de 30 chamadas/10s por
  conexão, registrado globalmente no SignalR (vale pros dois jogos)
- Teto de 200 salas + limpeza de salas abandonadas (nos dois `GameService`)
- `MaximumReceiveMessageSize = 8 KB` no `Program.cs`
- Limite de tamanho de palavras/palpites no Contato

> 🐛 **Bug em aberto nessa parte:** num teste apareceram **dois jogadores com o mesmo
> ID de conexão**, e o ID real de um deles sumiu da lista — sintoma de reconexão
> (o cliente volta com ID novo, mas o registro do jogador fica com o antigo).
> **Ainda não foi confirmado se a causa é o `MaximumReceiveMessageSize` de 8 KB**
> (que derruba a conexão se estourar) ou se é bug pré-existente de reconexão.
> Investigar antes de commitar/deployar essa parte.

## Checklist do que NÃO fazer

- ❌ Não recriar `repos\Soberania` (foi apagada; o código vive em `JogosSeletor/Soberania/`)
- ❌ Não usar a rota `/gamehub` (não existe mais — são `/hubs/soberania` e `/hubs/contato`)
- ❌ Não importar os dois namespaces no mesmo arquivo
- ❌ Não deployar o work-in-progress sem resolver o bug de IDs duplicados
