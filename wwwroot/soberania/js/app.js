// ---------- SignalR ----------
const conn = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/soberania")
  .withAutomaticReconnect()
  .build();

// ---------- identidade estável ----------
// O connectionId muda a cada reconexão, então ele NÃO pode identificar o jogador.
// Guardamos um token por aba (sessionStorage: cada aba = um jogador; sobrevive a F5).
const myToken = (() => {
  const KEY = "soberania.token";
  let t = null;
  try { t = sessionStorage.getItem(KEY); } catch { /* modo privado */ }
  if (!t) {
    t = (crypto.randomUUID ? crypto.randomUUID() : String(Math.random()).slice(2) + Date.now());
    try { sessionStorage.setItem(KEY, t); } catch { /* ignora */ }
  }
  return t;
})();

// ---------- estado local ----------
let myId = null;
let myCode = null;
let myName = null;
let myTitle = "Presidente";
let lastState = null;

// ---------- metadados das fases (só a UI; a lógica vem depois) ----------
const PHASES = ["Negociacao", "Investimento", "Acoes", "Represarias", "Resultados"];
const PHASE_LABEL = {
  Negociacao: "Negociação",
  Investimento: "Investimento",
  Acoes: "Ações",
  Represarias: "Represálias",
  Resultados: "Resultados",
};
const PHASE_DESC = {
  Negociacao: "Envie propostas de troca para outros líderes ou NPCs em troca dos recursos do jogo.",
  Investimento: "Saca 3 cartas por turno. Compre as que puder pagar — elas vão para sua mão.",
  Acoes: "Jogue as cartas compradas, escolhendo os alvos.",
  Represarias: "Reaja a quem te afetou na fase de Ações.",
  Resultados: "Soma de tudo: impostos, crescimento e os efeitos caem nos cofres de cada país.",
};
const PHASE_NOTE = {
  Acoes: "Jogar as cartas da mão (com alvo) será definido no próximo passo.",
  Represarias: "A segunda rodada de reação será definida depois.",
};

const RES = [
  { key: "dinheiro", label: "Dinheiro", emoji: "💰" },
  { key: "terra", label: "Terra", emoji: "🗺️" },
  { key: "petroleo", label: "Petróleo", emoji: "🛢️" },
  { key: "alimento", label: "Alimento", emoji: "🌾" },
  { key: "militares", label: "Militares", emoji: "🪖" },
  { key: "divida", label: "Dívida", emoji: "📉" },
];

const STATS = [
  { key: "populacao", label: "População", emoji: "👥" },
  { key: "credibilidade", label: "Credibilidade", emoji: "🎖️" },
  { key: "satisfacao", label: "Satisfação", emoji: "😀" },
  { key: "evasao", label: "Migração", emoji: "🧳" },
];

// ---------- helpers de DOM ----------
const $ = (id) => document.getElementById(id);
const show = (el) => el.removeAttribute("hidden");
const hide = (el) => el.setAttribute("hidden", "");

function toast(msg) {
  const t = $("toast");
  t.textContent = msg;
  show(t);
  clearTimeout(toast._t);
  toast._t = setTimeout(() => hide(t), 2500);
}

function showView(name) {
  for (const v of ["homeView", "lobbyView", "gameView"]) hide($(v));
  show($(name));
}

// ---------- home ----------
$("createBtn").onclick = async () => {
  const name = $("nameInput").value.trim();
  if (!name) return homeError("Digite seu nome.");
  const r = await conn.invoke("CreateRoom", name, myToken);
  if (!r.ok) return homeError(r.error);
  myName = name;
  setIdentity(r);
};

$("joinBtn").onclick = async () => {
  const name = $("nameInput").value.trim();
  const code = $("codeInput").value.trim().toUpperCase();
  if (!name) return homeError("Digite seu nome.");
  if (!code) return homeError("Digite o código da sala.");
  const r = await conn.invoke("JoinRoom", code, name, myToken);
  if (!r.ok) return homeError(r.error);
  myName = name;
  setIdentity(r);
};

// o evento "state" pode chegar antes de invoke() resolver; ao fixar a identidade,
// re-renderizamos o último estado para o host já enxergar seus controles.
function setIdentity(r) {
  myId = r.playerId; myCode = r.code;
  if (lastState) applyState(lastState);
}

function homeError(msg) {
  const e = $("homeError");
  e.textContent = msg; show(e);
}

// ---------- título ----------
$("titlePicker").addEventListener("click", (ev) => {
  const btn = ev.target.closest(".chip");
  if (!btn) return;
  myTitle = btn.dataset.title;
  for (const c of $("titlePicker").children) c.classList.toggle("active", c === btn);
});

// ---------- controles do host ----------
$("startBtn").onclick = () => conn.invoke("StartGame", myCode);

// A fase troca sozinha: quando todos ficam prontos ou o tempo acaba.
$("readyBtn").onclick = () => conn.invoke("SetReady", myCode, !(lastState && lastState.meReady));

// cronômetro local, só visual (a decisão de virar a fase é do servidor)
let phaseDeadline = null;
setInterval(() => {
  const el = $("phaseTimer");
  if (!el) return;
  if (!phaseDeadline) { el.textContent = "--:--"; el.classList.remove("urgent"); return; }
  const restam = Math.max(0, Math.round((phaseDeadline - Date.now()) / 1000));
  const m = String(Math.floor(restam / 60)).padStart(2, "0");
  const s = String(restam % 60).padStart(2, "0");
  el.textContent = `⏱ ${m}:${s}`;
  el.classList.toggle("urgent", restam <= 10);
}, 250);

// ---------- render do estado ----------
conn.on("state", (s) => applyState(s));

function applyState(s) {
  lastState = s;
  myCode = s.code;
  if (s.meId) myId = s.meId;   // identidade autoritativa vinda do servidor (evita corrida)
  $("roomCode").textContent = s.code;
  show($("roomPill"));

  if (s.phase === "Lobby") { renderLobby(s); showView("lobbyView"); }
  else { renderGame(s); showView("gameView"); }
}

function me(s) { return s.players.find((p) => p.id === myId); }
function iAmHost(s) { return s.hostId === myId; }

// ---------- lobby ----------
function renderLobby(s) {
  // marca meu título ativo
  for (const c of $("titlePicker").children)
    c.classList.toggle("active", c.dataset.title === myTitle);

  // grid de países
  const mine = me(s);
  const grid = $("countryGrid");
  grid.innerHTML = "";
  for (const c of s.countries) {
    const el = document.createElement("button");
    el.className = "country";
    if (mine && mine.countryId === c.id) el.classList.add("mine");
    else if (c.taken) el.classList.add("taken");
    el.innerHTML = `<div class="flag">${c.emoji}</div><div class="cname">${c.name}</div>`;
    const takenByOther = c.taken && !(mine && mine.countryId === c.id);
    if (!takenByOther) el.onclick = () => conn.invoke("ChooseCountry", myCode, c.id, myTitle);
    grid.appendChild(el);
  }

  // lista de jogadores
  const ul = $("lobbyPlayers");
  ul.innerHTML = "";
  for (const p of s.players) {
    const li = document.createElement("li");
    if (!p.connected) li.classList.add("off");
    li.innerHTML = `
      <span>${p.emoji ?? "❔"}</span>
      <span>${p.name}${p.isHost ? ' <span class="host-tag">HOST</span>' : ""}</span>
      <span class="badge ${p.chose ? "ready" : ""}">${p.chose ? `✓ ${p.countryName} · ${p.title}` : "escolhendo…"}</span>`;
    ul.appendChild(li);
  }

  // botão iniciar (só host)
  const actives = s.players.filter((p) => p.connected);
  const allChose = actives.every((p) => p.chose);
  const enough = actives.length >= s.minPlayers;
  if (iAmHost(s)) {
    show($("startBtn"));
    $("startBtn").disabled = !(allChose && enough);
    $("startHint").textContent = !enough
      ? `Precisa de pelo menos ${s.minPlayers} jogadores.`
      : (!allChose ? "Esperando todos escolherem um país." : "Tudo pronto!");
  } else {
    hide($("startBtn"));
    $("startHint").textContent = "Aguardando o host iniciar…";
  }
}

// ---------- jogo ----------
function renderGame(s) {
  $("roundNum").textContent = s.round;

  // passos das fases
  const steps = $("phaseSteps");
  steps.innerHTML = "";
  const curIdx = PHASES.indexOf(s.phase);
  PHASES.forEach((ph, i) => {
    const d = document.createElement("div");
    d.className = "step" + (i === curIdx ? " active" : (i < curIdx ? " done" : ""));
    d.textContent = `${i + 1}. ${PHASE_LABEL[ph]}`;
    steps.appendChild(d);
  });

  $("phaseTitle").textContent = PHASE_LABEL[s.phase] ?? s.phase;
  $("phaseDesc").textContent = PHASE_DESC[s.phase] ?? "";
  $("placeholderNote").textContent = PHASE_NOTE[s.phase] ?? "";

  // painel específico da fase
  const panels = { Negociacao: "negPanel", Investimento: "invPanel", Acoes: "acoesPanel", Represarias: "acoesPanel", Resultados: "resPanel" };
  for (const id of ["negPanel", "invPanel", "acoesPanel", "resPanel"]) hide($(id));
  const activePanel = panels[s.phase];
  if (activePanel) { hide($("phasePlaceholder")); show($(activePanel)); }
  else show($("phasePlaceholder"));
  if (s.phase === "Negociacao") renderNeg(s);
  else if (s.phase === "Investimento") renderInvestimento(s);
  else if (s.phase === "Acoes" || s.phase === "Represarias") renderAcoes(s);
  else if (s.phase === "Resultados") renderResultados(s);

  // meu cofre + itens internos (o servidor sempre manda os meus)
  const mine = me(s);
  $("myFlag").textContent = mine ? `${mine.emoji} ${mine.countryName} · ${mine.title}${mine.deposto ? " · 💀 deposto" : ""}` : "";
  $("myCofre").innerHTML = mine && mine.cofre ? cofreHtml(mine.cofre) : "";
  $("myStats").innerHTML = mine && mine.stats ? statsHtml(mine.stats) : "";

  // todas as nações — recursos/itens alheios só aparecem em Resultados
  const nat = $("nations");
  nat.innerHTML = "";
  for (const p of s.players) {
    const div = document.createElement("div");
    div.className = "nation";
    const privado = p.cofre
      ? `<div class="cofre">${cofreHtml(p.cofre)}</div><div class="stats-row">${statsHtml(p.stats)}</div>`
      : (p.chose ? '<div class="empty-note">🔒 recursos ocultos até os Resultados</div>' : "");
    div.innerHTML = `
      <div class="nhead">
        <span>${p.emoji ?? "❔"}</span>
        <span class="who">${p.countryName ?? "—"}</span>
        <span class="role">${p.name} · ${p.title}${p.isHost ? " · host" : ""}${p.isMe ? " · você" : ""}${p.deposto ? " · 💀" : ""}</span>
        ${p.maoCount ? `<span class="badge">🃏 ${p.maoCount}</span>` : ""}
        ${p.ready ? '<span class="badge ready">✅ pronto</span>' : ""}
        ${p.connected ? "" : '<span class="off">offline</span>'}
      </div>
      ${privado}`;
    nat.appendChild(div);
  }

  // cronômetro + prontidão (substitui o antigo controle do host)
  phaseDeadline = s.phaseDeadline || null;
  const euAtivo = mine && mine.chose && !mine.deposto;
  if (euAtivo) {
    show($("readyBar"));
    $("readyBtn").textContent = s.meReady ? "⏳ Aguardando os outros (clique p/ cancelar)" : "✅ Estou pronto";
    $("readyBtn").classList.toggle("primary", !s.meReady);
  } else hide($("readyBar"));
  $("readyHint").textContent = `${s.readyCount ?? 0}/${s.readyTotal ?? 0} prontos — a fase vira quando todos estiverem prontos ou o tempo acabar.`;
}

function cofreHtml(cofre) {
  return RES.map((r) => `
    <div class="res ${r.key === "divida" ? "divida" : ""}">
      <div class="rlabel">${r.emoji} ${r.label}</div>
      <div class="rval">${cofre[r.key]}</div>
    </div>`).join("");
}

function statsHtml(stats) {
  if (!stats) return "";
  return STATS.map((st) => {
    const v = stats[st.key] ?? 0;
    // satisfação (-100..+100) e migração (saldo) vão a negativo; negativo é sinal de encrenca
    const low = (st.key === "satisfacao" || st.key === "evasao") && v < 0;
    const suffix = st.key === "populacao" ? "mi" : "%";
    const sinal = (st.key === "satisfacao" || st.key === "evasao") && v > 0 ? "+" : "";
    return `
    <div class="stat ${low ? "low" : ""}">
      <div class="slabel">${st.emoji} ${st.label}</div>
      <div class="sval">${sinal}${v}${suffix}</div>
    </div>`;
  }).join("");
}

// ---------- investimento (draft de cartas) ----------
function custoHtml(custo) {
  const parts = RES.filter((r) => custo[r.key]).map((r) => `${custo[r.key]} ${r.emoji}`);
  return parts.length ? parts.join(" · ") : "grátis";
}

// mode: "offer" (comprar) | "hand" (só exibir) | "play" (jogar) | "fixa" (ação sempre disponível)
function cardHtml(c, mode) {
  const alvo = c.alvo && c.alvo !== "Nenhum"
    ? `<span class="calvo ${c.alvo}">${c.alvo === "Proprio" ? "Você" : c.alvo}</span>` : "";
  const mostraCusto = mode === "offer" || (mode === "fixa" && c.custo);
  const custo = mostraCusto ? `<div class="ccusto"><span class="lbl">Custo:</span> ${custoHtml(c.custo)}</div>` : "";
  let btn = "";
  if (mode === "offer") {
    btn = `<button class="btn small ${c.podePagar ? "primary" : ""}" ${c.podePagar ? "" : "disabled"} data-offer="${c.id}">
             ${c.podePagar ? "Comprar" : "Sem saldo"}</button>`;
  } else if (mode === "play") {
    btn = `<button class="btn primary small playbtn" data-play="${c.id}" data-alvo="${c.alvo}">Jogar</button>`;
  } else if (mode === "fixa") {
    btn = `<button class="btn danger small playbtn" data-fixa="${c.id}">Usar</button>`;
  }
  return `
    <div class="card ${mode === "offer" ? "" : "hand"} ${mode === "play" ? "playable" : ""} ${mode === "fixa" ? "fixa" : ""}">
      <div class="chead"><span class="cemoji">${c.emoji ?? "🃏"}</span><span class="cnome">${c.nome}</span>${alvo}</div>
      <div class="cdesc">${c.descricao ?? ""}</div>
      ${custo}${btn}
    </div>`;
}

// ---------- aplicações financeiras ----------
let abaInvest = "cartas";
$("tabCartas").onclick = () => { abaInvest = "cartas"; if (lastState) renderInvestimento(lastState); };
$("tabAplicacoes").onclick = () => { abaInvest = "aplicacoes"; if (lastState) renderInvestimento(lastState); };
$("imClose").onclick = () => hide($("investModal"));
$("investModal").onclick = (e) => { if (e.target === $("investModal")) hide($("investModal")); };

let investSelecionado = null;

function projecao(valor, taxa, rodadas) {
  let m = valor;
  for (let i = 0; i < rodadas; i++) m *= 1 + taxa / 100;
  return Math.round(m);
}

function atualizaPrevisao() {
  if (!investSelecionado) return;
  const v = Math.max(0, parseInt($("imValor").value, 10) || 0);
  const r = Math.max(1, parseInt($("imRodadas").value, 10) || 1);
  const bruto = projecao(v, investSelecionado.rendimentoEfetivo, r);
  const seQuebrar = Math.round(v * (100 - investSelecionado.perdaSeQuebrar) / 100);
  $("imPrevisao").innerHTML =
    `Se der certo: <b class="ok">${bruto} 💰</b> (+${bruto - v}) · ` +
    `Se quebrar (${investSelecionado.risco}% de chance): <b class="err">${seQuebrar} 💰</b>`;
}
$("imValor").oninput = atualizaPrevisao;
$("imRodadas").oninput = atualizaPrevisao;

function abrirModalInvest(def, saldo) {
  investSelecionado = def;
  $("imTitle").textContent = `${def.emoji} ${def.nome}`;
  $("imSub").textContent = `${def.origem} · ${def.rendimentoEfetivo}% por rodada · risco ${def.risco}%`;
  $("imSaldo").textContent = saldo;
  $("imPrazoFaixa").textContent = `${def.prazoMin} a ${def.prazoMax} rodadas`;
  $("imRodadas").min = def.prazoMin; $("imRodadas").max = def.prazoMax;
  $("imRodadas").value = def.prazoMin;
  $("imValor").max = saldo;
  $("imValor").value = Math.min(100, saldo);
  hide($("imErro"));
  atualizaPrevisao();
  show($("investModal"));
}

$("imConfirmar").onclick = async () => {
  if (!investSelecionado) return;
  const valor = parseInt($("imValor").value, 10) || 0;
  const rodadas = parseInt($("imRodadas").value, 10) || 1;
  const r = await conn.invoke("Invest", myCode, investSelecionado.id, valor, rodadas);
  if (!r.ok) { const e = $("imErro"); e.textContent = r.error; show(e); return; }
  hide($("investModal"));
  toast(`Aplicado! ${valor} 💰 a ${r.taxa}%/rodada por ${r.rodadas} rodada(s).`);
};

function renderAplicacoes(s) {
  const mine = me(s);
  const saldo = (mine && mine.cofre) ? mine.cofre.dinheiro : 0;

  $("investOptions").innerHTML = (s.investimentos || []).map((d) => {
    const delta = d.rendimentoEfetivo - d.rendimentoBase;
    const ajuste = delta === 0 ? "" :
      `<span class="${delta > 0 ? "ok" : "err"}"> (${delta > 0 ? "+" : ""}${delta} pelo seu país)</span>`;
    return `
      <div class="card invest">
        <div class="chead"><span class="cemoji">${d.emoji}</span><span class="cnome">${d.nome}</span>
          <span class="calvo risco-${d.risco >= 40 ? "alto" : (d.risco >= 15 ? "medio" : "baixo")}">risco ${d.risco}%</span></div>
        <div class="cdesc">${d.descricao}</div>
        <div class="ccusto"><span class="lbl">Origem:</span> ${d.origem}</div>
        <div class="ccusto"><span class="lbl">Rende:</span> <b>${d.rendimentoEfetivo}%</b>/rodada${ajuste}</div>
        <div class="ccusto"><span class="lbl">Prazo:</span> ${d.prazoMin}–${d.prazoMax} rodadas</div>
        <button class="btn primary small" data-invest="${d.id}">Aplicar</button>
      </div>`;
  }).join("") || '<div class="empty-note">Nenhuma aplicação disponível.</div>';

  $("investOptions").querySelectorAll("button[data-invest]").forEach((b) =>
    b.onclick = () => abrirModalInvest(s.investimentos.find((d) => d.id === b.dataset.invest), saldo));

  const ativas = (mine && mine.aplicacoes) || [];
  $("investAtivas").innerHTML = ativas.length ? ativas.map((a) => `
    <div class="prop">
      <div class="phead"><span>${a.emoji ?? "💹"}</span><span class="pwho">${a.nome}</span>
        <span class="badge">${a.rodadasRestantes}/${a.prazoTotal} rodadas</span></div>
      <div class="pterms">${a.valor} 💰 aplicados a <b>${a.taxa}%</b>/rodada · risco ${a.risco}%
        · projeção: <b class="ok">${projecao(a.valor, a.taxa, a.prazoTotal)} 💰</b></div>
    </div>`).join("")
    : '<div class="empty-note">Nenhuma aplicação em andamento.</div>';
}

function renderInvestimento(s) {
  // abas: cartas x aplicações
  const emCartas = abaInvest === "cartas";
  $("tabCartas").classList.toggle("active", emCartas);
  $("tabAplicacoes").classList.toggle("active", !emCartas);
  if (emCartas) { show($("invCartas")); hide($("invAplicacoes")); }
  else { hide($("invCartas")); show($("invAplicacoes")); renderAplicacoes(s); }

  const mine = me(s);
  const offers = (mine && mine.ofertas) || [];
  const oc = $("offerCards");
  oc.innerHTML = offers.length ? offers.map((c) => cardHtml(c, "offer")).join("")
    : '<div class="empty-note">Sem cartas para comprar.</div>';
  oc.querySelectorAll("button[data-offer]").forEach((b) =>
    b.onclick = () => conn.invoke("BuyCard", myCode, b.dataset.offer));

  const hand = (mine && mine.mao) || [];
  $("handCount").textContent = hand.length;
  $("handCards").innerHTML = hand.length ? hand.map((c) => cardHtml(c, "hand")).join("")
    : '<div class="empty-note">Você ainda não comprou cartas.</div>';
}

// ---------- ações / represálias ----------
let relInputsBuilt = false;
function buildRelInputs() {
  if (relInputsBuilt) return;
  const mk = (prefix) => RES.map((r) =>
    `<div class="res-in">
       <label>${r.emoji} ${r.label}</label>
       <input id="${prefix}_${r.key}" type="number" min="0" value="0" inputmode="numeric" />
     </div>`).join("");
  $("relGiveInputs").innerHTML = mk("relgive");
  $("relGetInputs").innerHTML = mk("relget");
  relInputsBuilt = true;
}
function readRel(prefix) {
  const out = {};
  for (const r of RES) out[r.key] = Math.max(0, parseInt($(`${prefix}_${r.key}`).value, 10) || 0);
  return out;
}

// ---------- impostos ----------
let abaAcoes = "acoes";
let arrastandoTax = false;
$("tabAcoes").onclick = () => { abaAcoes = "acoes"; if (lastState) renderAcoes(lastState); };
$("tabImpostos").onclick = () => { abaAcoes = "impostos"; if (lastState) renderAcoes(lastState); };

// projeta localmente enquanto arrasta (o servidor recalcula igual ao confirmar)
function previsaoImposto(s, taxa) {
  const mine = me(s);
  if (!mine || !mine.stats) return { dinheiro: 0, satisf: 0 };
  const pop = mine.stats.populacao;
  const satisf = Math.max(0, mine.stats.satisfacao);
  const neutra = s.taxaNeutra || 50;
  const divisor = s.taxaSatisfacaoDivisor || 5;
  return {
    dinheiro: Math.floor(Math.floor(pop * satisf / 100) * taxa / neutra),
    satisf: Math.trunc((neutra - taxa) / divisor)
  };
}

function pintaPrevisao(s, taxa) {
  const p = previsaoImposto(s, taxa);
  $("taxValue").textContent = `${taxa}%`;
  $("taxMoney").textContent = `+${p.dinheiro}`;
  const sat = $("taxSatisf");
  sat.textContent = (p.satisf > 0 ? "+" : "") + p.satisf;
  sat.className = "sval " + (p.satisf > 0 ? "up" : (p.satisf < 0 ? "down" : ""));
  $("taxHint").textContent = p.satisf < 0
    ? `Você arrecada mais agora, mas perde ${Math.abs(p.satisf)} de satisfação por rodada — e satisfação baixa derruba a própria arrecadação depois.`
    : (p.satisf > 0
        ? `Abre mão de dinheiro para ganhar ${p.satisf} de satisfação por rodada.`
        : "Taxa neutra: não mexe na satisfação.");
}

$("taxSlider").oninput = () => {
  arrastandoTax = true;
  if (lastState) pintaPrevisao(lastState, parseInt($("taxSlider").value, 10));
};
$("taxSlider").onchange = async () => {
  const taxa = parseInt($("taxSlider").value, 10);
  await conn.invoke("SetTaxRate", myCode, taxa);
  arrastandoTax = false;
};

function renderImpostos(s) {
  // não sobrescreve a barra enquanto o dedo está nela
  if (!arrastandoTax) $("taxSlider").value = s.taxaImposto ?? 50;
  pintaPrevisao(s, parseInt($("taxSlider").value, 10));
  $("taxSlider").disabled = s.phase !== "Acoes";
}

function renderAcoes(s) {
  // abas: ações x impostos
  const emAcoes = abaAcoes === "acoes";
  $("tabAcoes").classList.toggle("active", emAcoes);
  $("tabImpostos").classList.toggle("active", !emAcoes);
  if (emAcoes) { show($("acoesConteudo")); hide($("impostosConteudo")); }
  else { hide($("acoesConteudo")); show($("impostosConteudo")); }
  renderImpostos(s);

  buildRelInputs();
  const isAcoes = s.phase === "Acoes";
  const isRepr = s.phase === "Represarias";
  const mine = me(s);

  // quem me agrediu nesta rodada (alvos válidos de represália)
  const aggMap = new Map();
  for (const e of (s.events || [])) {
    // só agressões da fase de Ações dão direito a revide (não os próprios revides)
    if (e.againstMe && e.phase === "Acoes" && (e.kind === "militar" || e.kind === "difamar" || e.kind === "carta") && e.actorId)
      if (!aggMap.has(e.actorId)) aggMap.set(e.actorId, e.actorLabel || "agressor");
  }
  const aggressors = [...aggMap.entries()].map(([id, label]) => ({ id, label }));

  // banner da fase
  const banner = $("acoesBanner");
  if (isRepr) {
    banner.textContent = aggressors.length
      ? "🛡️ Represálias — revide quem te agrediu nesta rodada (ataque, difamação ou carta contra o agressor)."
      : "🛡️ Represálias — ninguém te agrediu nesta rodada. Nada a revidar.";
    show(banner);
  } else hide(banner);

  // desastre natural desta rodada (o jogo agindo sozinho)
  const db = $("disasterBanner");
  const desastres = (s.events || []).filter((e) => e.kind === "desastre");
  if (desastres.length) {
    db.innerHTML = desastres.map((e) => `<div>${e.text}</div>`).join("");
    show(db);
  } else hide(db);

  // efeitos contínuos ativos
  const ob = $("ongoingBanner");
  if (s.ongoing && s.ongoing.length) {
    ob.innerHTML = "⏳ Efeitos ativos: " + s.ongoing.map((e) => `${e.emoji} ${e.nome} (${e.roundsLeft} turno(s))`).join(" · ");
    show(ob);
  } else hide(ob);

  // ferramentas ofensivas: Ações (contra todos) ou Represálias (só contra agressores)
  const tools = $("acoesTools");
  const podeAgir = mine && !mine.deposto && (isAcoes || (isRepr && aggressors.length > 0));
  tools.style.display = podeAgir ? "" : "none";
  // criar relação só existe na fase de Ações
  $("relCreate").style.display = isAcoes ? "" : "none";

  if (podeAgir) {
    // alvos possíveis: todos (Ações) ou só agressores (Represálias)
    alvosAtuais = isRepr
      ? aggressors.map((a) => ({ id: a.id, label: a.label, emoji: "" }))
      : s.players.filter((p) => !p.isMe && p.chose && !p.deposto)
                 .map((p) => ({ id: p.id, label: `${p.countryName} (${p.name})`, emoji: p.emoji }));

    npcsAtuais = (s.npcs || []).map((n) => ({
      id: n.id, label: n.name, emoji: n.emoji, isNpc: true,
      hint: n.bloqueado ? `💣 já sabotada (${n.bloqueioRounds} rodada(s))`
          : (n.restritaDono ? `🔒 Relação Restrita de ${n.restritaDono}` : `vende ${resSummary(n.da)}`)
    }));

    // ações fixas viram cartas (sempre disponíveis, não são compradas)
    $("acaoFixas").innerHTML = ACOES_FIXAS.map((a) => cardHtml({
      id: a.id, nome: a.nome, emoji: a.emoji, descricao: a.descricao, alvo: "Inimigo", custo: a.custo
    }, "fixa")).join("");
    $("acaoFixas").querySelectorAll("button[data-fixa]").forEach((b) =>
      b.onclick = () => acaoFixa(b.dataset.fixa));

    // mão comprada — em Represálias, só cartas de inimigo
    let hand = (mine && mine.mao) || [];
    if (isRepr) hand = hand.filter((c) => c.alvo === "Inimigo");
    $("acaoHand").innerHTML = hand.length ? hand.map((c) => cardHtml(c, "play")).join("")
      : `<div class="empty-note">${isRepr ? "Nenhuma carta de ataque na mão." : "Sem cartas na mão. Compre no Investimento."}</div>`;
    $("acaoHand").querySelectorAll("button[data-play]").forEach((b) =>
      b.onclick = () => playCard(b.dataset.play, b.dataset.alvo));
  }

  // relações comerciais (aceitar/cortar valem em Ações e Represálias)
  renderRelations(s);

  // acontecimentos da rodada
  renderEvents(s, $("eventsList"));
}

function renderRelations(s) {
  const list = $("relList");
  const rels = s.relations || [];
  list.innerHTML = "";
  if (!rels.length) { list.innerHTML = '<div class="empty-note">Nenhuma relação comercial.</div>'; return; }
  for (const r of rels) {
    const div = document.createElement("div");
    div.className = "prop";
    const ativa = r.status === "Ativa";
    let actions = "";
    if (r.podeResponder) actions = `<div class="pactions"><button class="btn ok small" data-relok="${r.id}">Aceitar</button><button class="btn danger small" data-relno="${r.id}">Recusar</button></div>`;
    else actions = `<div class="pactions"><button class="btn danger small" data-relcut="${r.id}">${ativa ? "Cortar" : "Cancelar"}</button></div>`;
    div.innerHTML = `
      <div class="phead"><span>${r.counterpartEmoji ?? "❔"}</span><span class="pwho">${r.counterpartLabel}</span>
        <span class="pstatus ${r.status.toLowerCase()}">${ativa ? "🤝 Ativa" : "⏳ Pendente"}</span></div>
      <div class="pterms">Você dá <span class="give">${resSummary(r.euDou)}</span>/rodada · recebe <span class="get">${resSummary(r.euRecebo)}</span>/rodada</div>
      ${actions}`;
    const okB = div.querySelector("[data-relok]"), noB = div.querySelector("[data-relno]"), cutB = div.querySelector("[data-relcut]");
    if (okB) okB.onclick = () => conn.invoke("RespondRelation", myCode, r.id, true);
    if (noB) noB.onclick = () => conn.invoke("RespondRelation", myCode, r.id, false);
    if (cutB) cutB.onclick = () => conn.invoke("CutRelation", myCode, r.id);
    list.appendChild(div);
  }
}

function renderEvents(s, el) {
  const evs = s.events || [];
  el.innerHTML = "";
  if (!evs.length) { el.innerHTML = '<div class="empty-note">Nada aconteceu ainda.</div>'; return; }
  for (const e of evs) {
    const div = document.createElement("div");
    div.className = "event" + (e.kind === "desastre" ? " desastre"
      : (e.againstMe ? " againstMe" : (e.mine ? " mine" : "")));
    div.textContent = e.text;
    el.appendChild(div);
  }
}

// ---------- modal de alvo ----------
// Escolher alvo por dropdown ficava escondido; agora toda ação com alvo abre este popup.
let alvosAtuais = [];
let npcsAtuais = [];

function abrirModalAlvo({ titulo, sub, alvos, onPick }) {
  if (!alvos.length) return acaoMsg("Nenhum alvo disponível.", true);
  $("modalTitle").textContent = titulo;
  $("modalSub").textContent = sub || "";
  const box = $("modalTargets");
  box.innerHTML = "";
  for (const a of alvos) {
    const btn = document.createElement("button");
    btn.className = "target" + (a.isNpc ? " npc-target" : "");
    btn.innerHTML = `<div class="tname">${a.emoji ?? ""} ${a.label}${a.isNpc ? '<span class="npc-tag">NPC</span>' : ""}</div>
                     ${a.hint ? `<div class="toffer">${a.hint}</div>` : ""}`;
    btn.onclick = () => { fecharModal(); onPick(a.id); };
    box.appendChild(btn);
  }
  show($("targetModal"));
}
function fecharModal() { hide($("targetModal")); }
$("modalClose").onclick = fecharModal;
$("targetModal").onclick = (e) => { if (e.target === $("targetModal")) fecharModal(); };
document.addEventListener("keydown", (e) => { if (e.key === "Escape") fecharModal(); });

// ---------- ações fixas (sempre disponíveis, como cartas) ----------
const ACOES_FIXAS = [
  { id: "atacar",  nome: "Ataque Militar", emoji: "⚔️", descricao: "Invade o alvo. O dano depende da sua força militar contra a dele.", custo: null },
  { id: "difamar", nome: "Difamação",      emoji: "📢", descricao: "Derruba a credibilidade e a satisfação do alvo.", custo: { dinheiro: 150, terra: 0, petroleo: 0, alimento: 0, militares: 0, divida: 0 } },
];

function acaoFixa(id) {
  const meta = ACOES_FIXAS.find((a) => a.id === id);
  abrirModalAlvo({
    titulo: `${meta.emoji} ${meta.nome}`,
    sub: "Escolha o país alvo",
    alvos: alvosAtuais,
    onPick: async (targetId) => {
      const metodo = id === "atacar" ? "MilitaryAttack" : "Defame";
      const r = await conn.invoke(metodo, myCode, targetId);
      if (!r.ok) acaoMsg(r.error, true);
    }
  });
}

async function playCard(handId, alvo) {
  if (alvo !== "Inimigo" && alvo !== "Npc") {   // Proprio/Todos não precisam de alvo
    const r = await conn.invoke("PlayCard", myCode, handId, null);
    if (!r.ok) acaoMsg(r.error, true);
    return;
  }
  // cartas "Npc" miram os países fornecedores; as demais, os jogadores
  const alvos = alvo === "Npc" ? npcsAtuais : alvosAtuais;
  abrirModalAlvo({
    titulo: alvo === "Npc" ? "Escolher país NPC" : "Escolher alvo da carta",
    sub: alvo === "Npc" ? "Qual fornecedor atingir?" : "Contra quem usar esta carta?",
    alvos,
    onPick: async (targetId) => {
      const r = await conn.invoke("PlayCard", myCode, handId, targetId);
      if (!r.ok) acaoMsg(r.error, true);
    }
  });
}

$("btnCriarRel").onclick = () => {
  const give = readRel("relgive"), get = readRel("relget");
  abrirModalAlvo({
    titulo: "🤝 Relação comercial",
    sub: "Com quem firmar o acordo recorrente?",
    alvos: alvosAtuais,
    onPick: async (targetId) => {
      const r = await conn.invoke("ProposeRelation", myCode, targetId, give, get);
      if (!r.ok) acaoMsg(r.error, true); else acaoMsg("Relação proposta. Aguardando aceite.", false);
    }
  });
};

function acaoMsg(msg, isErr) {
  const m = $("acaoMsg");
  m.textContent = msg; m.className = "tiny " + (isErr ? "err" : "ok"); show(m);
  clearTimeout(acaoMsg._t); acaoMsg._t = setTimeout(() => hide(m), 3500);
}

// ---------- resultados: abas + gráficos de evolução ----------
// Escalas incompatíveis (dinheiro em milhares, população em milhões, satisfação 0-100),
// então são TRÊS gráficos separados — nunca dois eixos no mesmo desenho.
let abaSelecionada = null;

const METRICAS = [
  { key: "dinheiro",  label: "💰 Dinheiro",  cor: "#c98500", suf: "" },
  { key: "populacao", label: "👥 População", cor: "#3987e5", suf: "mi" },
  { key: "satisfacao", label: "😀 Satisfação", cor: "#199e70", suf: "%" },
];

function miniChart(hist, m) {
  const pts = hist.map((h) => ({ x: h.round, y: h[m.key] }));
  const atual = pts.length ? pts[pts.length - 1].y : 0;
  const anterior = pts.length > 1 ? pts[pts.length - 2].y : null;
  const delta = anterior === null ? null : atual - anterior;
  const deltaTxt = delta === null ? ""
    : `<span class="chart-delta ${delta >= 0 ? "up" : "down"}">${delta >= 0 ? "▲" : "▼"} ${Math.abs(delta)}${m.suf}</span>`;

  let corpo;
  if (pts.length < 2) {
    corpo = '<div class="chart-empty">Precisa de 2 rodadas para desenhar a evolução.</div>';
  } else {
    const W = 300, H = 90, pad = 6;
    const ys = pts.map((p) => p.y);
    let min = Math.min(...ys), max = Math.max(...ys);
    if (min === max) { min -= 1; max += 1; }          // série plana ainda precisa de altura
    const sx = (i) => pad + (i * (W - pad * 2)) / (pts.length - 1);
    const sy = (v) => H - pad - ((v - min) / (max - min)) * (H - pad * 2);
    const linha = pts.map((p, i) => `${i ? "L" : "M"}${sx(i).toFixed(1)},${sy(p.y).toFixed(1)}`).join(" ");
    const area = `${linha} L${sx(pts.length - 1).toFixed(1)},${H - pad} L${sx(0).toFixed(1)},${H - pad} Z`;
    const marcadores = pts.map((p, i) =>
      `<circle class="chart-dot" cx="${sx(i).toFixed(1)}" cy="${sy(p.y).toFixed(1)}" r="4" fill="${m.cor}"
        stroke="var(--panel)" stroke-width="2"><title>Rodada ${p.x}: ${p.y}${m.suf}</title></circle>`).join("");
    corpo = `
      <svg class="chart-svg" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" role="img"
           aria-label="${m.label} por rodada">
        <line class="grid" x1="${pad}" y1="${H - pad}" x2="${W - pad}" y2="${H - pad}" />
        <path d="${area}" fill="${m.cor}" opacity="0.12" />
        <path d="${linha}" fill="none" stroke="${m.cor}" stroke-width="2"
              stroke-linejoin="round" stroke-linecap="round" />
        ${marcadores}
      </svg>
      <div class="tiny muted">Rodada ${pts[0].x} → ${pts[pts.length - 1].x}</div>`;
  }

  return `
    <div class="chart-card">
      <div class="chart-head">
        <span class="chart-title">${m.label}</span>
        <span class="chart-now" style="color:${m.cor}">${atual}${m.suf}</span>
        ${deltaTxt}
      </div>
      ${corpo}
    </div>`;
}

function renderAbasResultados(s) {
  const jogadores = s.players.filter((p) => p.chose);
  if (!jogadores.length) return;
  if (!jogadores.some((p) => p.id === abaSelecionada)) {
    abaSelecionada = (jogadores.find((p) => p.isMe) || jogadores[0]).id;
  }

  const tabs = $("resTabs");
  tabs.innerHTML = "";
  for (const p of jogadores) {
    const b = document.createElement("button");
    b.className = "res-tab" + (p.id === abaSelecionada ? " active" : "");
    b.setAttribute("role", "tab");
    b.setAttribute("aria-selected", p.id === abaSelecionada ? "true" : "false");
    b.textContent = `${p.emoji ?? "❔"} ${p.countryName}${p.isMe ? " (você)" : ""}`;
    b.onclick = () => { abaSelecionada = p.id; renderAbasResultados(lastState); };
    tabs.appendChild(b);
  }

  const alvo = jogadores.find((p) => p.id === abaSelecionada);
  const hist = (alvo && alvo.history) || [];
  $("resCharts").innerHTML = hist.length
    ? METRICAS.map((m) => miniChart(hist, m)).join("")
    : '<div class="chart-empty">Sem histórico ainda.</div>';
}

function renderResultados(s) {
  renderAbasResultados(s);
  const ob = $("resOngoing");
  if (s.ongoing && s.ongoing.length) {
    ob.innerHTML = "⏳ Efeitos ativos: " + s.ongoing.map((e) => `${e.emoji} ${e.nome} (${e.roundsLeft} turno(s))`).join(" · ");
    show(ob);
  } else hide(ob);

  const list = $("resultsList");
  list.innerHTML = "";

  // acontecimentos da rodada (todos, revelados em Resultados)
  if (s.events && s.events.length) {
    const evBlock = document.createElement("div");
    evBlock.className = "result-block";
    evBlock.innerHTML = "<h4>📜 Acontecimentos da rodada</h4>";
    const evList = document.createElement("div");
    evList.className = "events-list";
    renderEvents(s, evList);
    evBlock.appendChild(evList);
    list.appendChild(evBlock);
  }

  for (const p of s.players.filter((x) => x.chose)) {
    const lines = p.lastResults || [];
    const block = document.createElement("div");
    block.className = "result-block";
    block.innerHTML = `<h4>${p.emoji ?? "❔"} ${p.countryName} — ${p.name}</h4>` +
      (lines.length ? lines.map((l) => {
        const cls = l.includes("GOLPE") || l.includes("deposto") ? "bad" : (l.includes("⚠️") ? "warn" : "");
        return `<div class="result-line ${cls}">${l}</div>`;
      }).join("") : '<div class="empty-note">Sem alterações.</div>');
    list.appendChild(block);
  }
}

// ---------- negociação ----------
let selectedTargetId = null;
let selectedIsNpc = false;
let resInputsBuilt = false;

function buildResInputs() {
  if (resInputsBuilt) return;
  const mk = (prefix) => RES.map((r) =>
    `<div class="res-in">
       <label>${r.emoji} ${r.label}</label>
       <input id="${prefix}_${r.key}" type="number" min="0" value="0" inputmode="numeric" />
     </div>`).join("");
  $("offerInputs").innerHTML = mk("off");
  $("requestInputs").innerHTML = mk("req");
  resInputsBuilt = true;
}

function setRes(prefix, cofre) {
  for (const r of RES) $(`${prefix}_${r.key}`).value = cofre ? (cofre[r.key] || 0) : 0;
}
function readRes(prefix) {
  const out = {};
  for (const r of RES) out[r.key] = Math.max(0, parseInt($(`${prefix}_${r.key}`).value, 10) || 0);
  return out;
}
function resSummary(cofre) {
  const parts = RES.filter((r) => cofre[r.key]).map((r) => `${cofre[r.key]} ${r.emoji}`);
  return parts.length ? parts.join(" + ") : "nada";
}

function renderNeg(s) {
  buildResInputs();

  // alvos: outros jogadores (com país) + NPCs
  const targets = $("negTargets");
  targets.innerHTML = "";
  const others = s.players.filter((p) => !p.isMe && p.chose && p.connected);
  for (const p of others) targets.appendChild(targetCard(p.id, false, p.emoji, p.countryName, null));
  for (const n of s.npcs) {
    let hint;
    if (n.bloqueado) hint = `💣 sabotada — não vende por ${n.bloqueioRounds} rodada(s)`;
    else hint = `dá ${resSummary(n.da)} · quer ${resSummary(n.quer)}`
              + (n.temAgio ? ` ⚠️ inflado por ${n.restritaDono}` : "")
              + (n.souDonoRestrita ? " 🔒 sua Relação Restrita" : "");
    targets.appendChild(targetCard(n.id, true, n.emoji, n.name, hint, n));
  }

  if (others.length === 0 && s.npcs.length === 0)
    targets.innerHTML = '<div class="empty-note">Ninguém para negociar ainda.</div>';

  // se o alvo selecionado sumiu, fecha o builder
  const stillValid = selectedIsNpc
    ? s.npcs.some((n) => n.id === selectedTargetId)
    : others.some((p) => p.id === selectedTargetId);
  if (!stillValid) { selectedTargetId = null; hide($("tradeBuilder")); }
  for (const el of targets.children)
    if (el.dataset) el.classList.toggle("sel", el.dataset.tid === selectedTargetId);

  // propostas recebidas
  const inc = $("incomingList");
  inc.innerHTML = "";
  if (!s.incoming.length) inc.innerHTML = '<div class="empty-note">Nenhuma proposta recebida.</div>';
  for (const p of s.incoming) {
    const div = document.createElement("div");
    div.className = "prop";
    // para o destinatário: o remetente OFERECE p.offer e QUER p.request
    div.innerHTML = `
      <div class="phead"><span>${p.counterpartEmoji ?? "❔"}</span><span class="pwho">${p.counterpartLabel}</span> quer negociar</div>
      <div class="pterms">Te dá <span class="give">${resSummary(p.offer)}</span> · quer <span class="get">${resSummary(p.request)}</span></div>
      <div class="pactions">
        <button class="btn ok small">Aceitar</button>
        <button class="btn danger small">Recusar</button>
      </div>`;
    const [okB, noB] = div.querySelectorAll("button");
    okB.onclick = () => conn.invoke("RespondProposal", myCode, p.id, true);
    noB.onclick = () => conn.invoke("RespondProposal", myCode, p.id, false);
    inc.appendChild(div);
  }

  // minhas propostas enviadas
  const out = $("outgoingList");
  out.innerHTML = "";
  if (!s.outgoing.length) out.innerHTML = '<div class="empty-note">Nenhuma negociação ainda.</div>';
  for (const p of s.outgoing) {
    const div = document.createElement("div");
    div.className = "prop";
    const st = p.status.toLowerCase();
    // quem RECEBEU a proposta lê ao contrário: a "oferta" do remetente é o que ele ganha
    const euDou = p.souRemetente ? p.offer : p.request;
    const euRecebo = p.souRemetente ? p.request : p.offer;
    const direcao = p.souRemetente ? "enviada" : "recebida";
    div.innerHTML = `
      <div class="phead"><span>${p.counterpartEmoji ?? "❔"}</span><span class="pwho">${p.counterpartLabel}</span>
        <span class="badge">${direcao}</span></div>
      <div class="pterms">Você dá <span class="give">${resSummary(euDou)}</span> · recebe <span class="get">${resSummary(euRecebo)}</span></div>
      <div class="pstatus ${st}">${statusLabel(p.status)}${p.note ? " — " + p.note : ""}</div>`;
    out.appendChild(div);
  }
}

function statusLabel(s) {
  return { Pendente: "⏳ Pendente", Aceita: "✅ Aceita", Recusada: "❌ Recusada" }[s] ?? s;
}

function targetCard(id, isNpc, emoji, name, offerText, npc) {
  const btn = document.createElement("button");
  btn.className = "target";
  btn.dataset.tid = id;
  btn.innerHTML = `
    <div class="tname">${emoji ?? "❔"} ${name}${isNpc ? '<span class="npc-tag">NPC</span>' : ""}</div>
    ${offerText ? `<div class="toffer">${offerText}</div>` : ""}`;
  btn.onclick = () => selectTarget(id, isNpc, `${emoji ?? ""} ${name}`, npc);
  return btn;
}

function selectTarget(id, isNpc, label, npc) {
  selectedTargetId = id; selectedIsNpc = isNpc;
  $("tbTarget").textContent = label;
  show($("tradeBuilder"));
  hide($("negMsg"));
  // NPC: pré-preenche com o negócio fixo dele (ofereço o que ele quer, peço o que ele dá)
  if (isNpc && npc) {
    setRes("off", npc.quer); setRes("req", npc.da);
    let hint = `A ${npc.name} só fecha se você oferecer pelo menos o que ela pede. Pré-preenchi o negócio dela.`;
    if (npc.bloqueado) hint = `💣 ${npc.name} foi sabotada e não vende por ${npc.bloqueioRounds} rodada(s).`;
    else if (npc.temAgio) hint += ` ⚠️ O preço está inflado porque ${npc.restritaDono} tem Relação Restrita com ela.`;
    else if (npc.souDonoRestrita) hint += ` 🔒 Você tem a Relação Restrita: paga o preço normal e os outros pagam mais.`;
    $("tbNpcHint").textContent = hint;
    show($("tbNpcHint"));
    // botão de comprar a Relação Restrita (acordo caro e exclusivo)
    const rb = $("restritaBtn");
    if (!npc.souDonoRestrita && !npc.restritaDono) {
      rb.textContent = `🔒 Fechar Relação Restrita com ${npc.name} (${npc.precoRestrita} 💰)`;
      rb.onclick = async () => {
        const r = await conn.invoke("BuyRestrita", myCode, npc.id);
        const m = $("negMsg");
        m.textContent = r.ok ? `🔒 Relação Restrita fechada com ${npc.name}!` : r.error;
        m.className = "tiny " + (r.ok ? "ok" : "err"); show(m);
      };
      show(rb);
    } else hide(rb);
  } else {
    hide($("restritaBtn"));
    setRes("off", null); setRes("req", null);
    hide($("tbNpcHint"));
  }
  for (const el of $("negTargets").children)
    if (el.dataset) el.classList.toggle("sel", el.dataset.tid === id);
}

$("cancelTargetBtn").onclick = () => { selectedTargetId = null; hide($("tradeBuilder")); for (const el of $("negTargets").children) el.classList?.remove("sel"); };

$("sendProposalBtn").onclick = async () => {
  if (!selectedTargetId) return;
  const offer = readRes("off");
  const request = readRes("req");
  const r = await conn.invoke("SendProposal", myCode, selectedTargetId, offer, request);
  const msg = $("negMsg");
  if (!r.ok) { msg.textContent = r.error; msg.className = "tiny err"; show(msg); return; }
  if (r.resolved) {
    const aceita = r.status === "Aceita";
    msg.textContent = (aceita ? "✅ " : "❌ ") + (r.note ?? "");
    msg.className = "tiny " + (aceita ? "ok" : "err");
  } else {
    msg.textContent = "Proposta enviada. Aguardando resposta.";
    msg.className = "tiny";
  }
  show(msg);
};

// ---------- conexão ----------
// Ao reconectar, o SignalR entrega um connectionId NOVO e a conexão sai do grupo da sala.
// Sem re-entrar, o jogador vira órfão: o servidor guarda o ID morto e param de chegar os "state".
// O token faz o servidor reamarrar o slot existente em vez de criar outro jogador.
conn.onreconnected(async () => {
  if (!myCode || !myName) return;
  try {
    const r = await conn.invoke("JoinRoom", myCode, myName, myToken);
    if (r && r.ok) { setIdentity(r); toast("Reconectado."); }
    else toast("Reconectado, mas não deu para voltar à sala: " + ((r && r.error) || ""));
  } catch (e) {
    console.error(e);
    toast("Falha ao voltar para a sala.");
  }
});
conn.start().catch((e) => { console.error(e); toast("Falha ao conectar."); });
