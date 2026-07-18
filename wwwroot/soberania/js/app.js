// ---------- SignalR ----------
const conn = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/soberania")
  .withAutomaticReconnect()
  .build();

// ---------- estado local ----------
let myId = null;
let myCode = null;
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
  { key: "aprovacao", label: "Aprovação", emoji: "📊" },
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
  const r = await conn.invoke("CreateRoom", name);
  if (!r.ok) return homeError(r.error);
  setIdentity(r);
};

$("joinBtn").onclick = async () => {
  const name = $("nameInput").value.trim();
  const code = $("codeInput").value.trim().toUpperCase();
  if (!name) return homeError("Digite seu nome.");
  if (!code) return homeError("Digite o código da sala.");
  const r = await conn.invoke("JoinRoom", code, name);
  if (!r.ok) return homeError(r.error);
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
$("nextBtn").onclick = () => conn.invoke("NextPhase", myCode);

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
        ${p.connected ? "" : '<span class="off">offline</span>'}
      </div>
      ${privado}`;
    nat.appendChild(div);
  }

  // controles do host
  if (iAmHost(s)) {
    show($("hostControls"));
    $("nextBtn").textContent = s.phase === "Resultados" ? "Próxima rodada ⟳" : "Avançar fase ▶";
  } else {
    hide($("hostControls"));
  }
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
    const low = st.key === "aprovacao" && stats[st.key] < 20;
    const suffix = st.key === "populacao" ? "mi" : (st.key === "credibilidade" || st.key === "aprovacao" ? "%" : "");
    return `
    <div class="stat ${low ? "low" : ""}">
      <div class="slabel">${st.emoji} ${st.label}</div>
      <div class="sval">${stats[st.key]}${suffix}</div>
    </div>`;
  }).join("");
}

// ---------- investimento (draft de cartas) ----------
function custoHtml(custo) {
  const parts = RES.filter((r) => custo[r.key]).map((r) => `${custo[r.key]} ${r.emoji}`);
  return parts.length ? parts.join(" · ") : "grátis";
}

// mode: "offer" (comprar) | "hand" (só exibir) | "play" (jogar na fase de Ações)
function cardHtml(c, mode) {
  const alvo = c.alvo && c.alvo !== "Nenhum"
    ? `<span class="calvo ${c.alvo}">${c.alvo === "Proprio" ? "Você" : c.alvo}</span>` : "";
  const custo = mode === "offer" ? `<div class="ccusto"><span class="lbl">Custo:</span> ${custoHtml(c.custo)}</div>` : "";
  let btn = "";
  if (mode === "offer") {
    btn = `<button class="btn small ${c.podePagar ? "primary" : ""}" ${c.podePagar ? "" : "disabled"} data-offer="${c.id}">
             ${c.podePagar ? "Comprar" : "Sem saldo"}</button>`;
  } else if (mode === "play") {
    btn = `<button class="btn primary small playbtn" data-play="${c.id}" data-alvo="${c.alvo}">Jogar</button>`;
  }
  return `
    <div class="card ${mode !== "offer" ? "hand" : ""} ${mode === "play" ? "playable" : ""}">
      <div class="chead"><span class="cemoji">${c.emoji ?? "🃏"}</span><span class="cnome">${c.nome}</span>${alvo}</div>
      <div class="cdesc">${c.descricao ?? ""}</div>
      ${custo}${btn}
    </div>`;
}

function renderInvestimento(s) {
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

function renderAcoes(s) {
  buildRelInputs();
  const isAcoes = s.phase === "Acoes";
  const isRepr = s.phase === "Represarias";
  const mine = me(s);

  // quem me agrediu nesta rodada (alvos válidos de represália)
  const aggMap = new Map();
  for (const e of (s.events || [])) {
    if (e.againstMe && (e.kind === "militar" || e.kind === "difamar" || e.kind === "carta") && e.actorId)
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
  $("btnAtacar").style.display = "";

  if (podeAgir) {
    // mão jogável — em Represálias, só cartas de inimigo
    let hand = (mine && mine.mao) || [];
    if (isRepr) hand = hand.filter((c) => c.alvo === "Inimigo");
    $("acaoHand").innerHTML = hand.length ? hand.map((c) => cardHtml(c, "play")).join("")
      : `<div class="empty-note">${isRepr ? "Nenhuma carta de ataque na mão." : "Sem cartas na mão. Compre no Investimento."}</div>`;
    $("acaoHand").querySelectorAll("button[data-play]").forEach((b) =>
      b.onclick = () => playCard(b.dataset.play, b.dataset.alvo));

    // alvos: todos (Ações) ou só agressores (Represálias)
    const alvos = isRepr
      ? aggressors.map((a) => ({ id: a.id, text: a.label }))
      : s.players.filter((p) => !p.isMe && p.chose && !p.deposto).map((p) => ({ id: p.id, text: `${p.emoji ?? ""} ${p.countryName} (${p.name})` }));
    $("actTarget").innerHTML = alvos.length
      ? alvos.map((a) => `<option value="${a.id}">${a.text}</option>`).join("")
      : '<option value="">— sem alvos —</option>';
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
    div.className = "event" + (e.againstMe ? " againstMe" : (e.mine ? " mine" : ""));
    div.textContent = e.text;
    el.appendChild(div);
  }
}

async function playCard(handId, alvo) {
  let targetId = null;
  if (alvo === "Inimigo") {
    targetId = $("actTarget").value;
    if (!targetId) return acaoMsg("Escolha um alvo para essa carta.", true);
  }
  const r = await conn.invoke("PlayCard", myCode, handId, targetId);
  if (!r.ok) acaoMsg(r.error, true);
}

$("btnAtacar").onclick = async () => {
  const t = $("actTarget").value; if (!t) return acaoMsg("Escolha um alvo.", true);
  const r = await conn.invoke("MilitaryAttack", myCode, t);
  if (!r.ok) acaoMsg(r.error, true);
};
$("btnDifamar").onclick = async () => {
  const t = $("actTarget").value; if (!t) return acaoMsg("Escolha um alvo.", true);
  const r = await conn.invoke("Defame", myCode, t);
  if (!r.ok) acaoMsg(r.error, true);
};
$("btnCriarRel").onclick = async () => {
  const t = $("actTarget").value; if (!t) return acaoMsg("Escolha um alvo.", true);
  const give = readRel("relgive"), get = readRel("relget");
  const r = await conn.invoke("ProposeRelation", myCode, t, give, get);
  if (!r.ok) acaoMsg(r.error, true); else acaoMsg("Relação proposta. Aguardando aceite.", false);
};

function acaoMsg(msg, isErr) {
  const m = $("acaoMsg");
  m.textContent = msg; m.className = "tiny " + (isErr ? "err" : "ok"); show(m);
  clearTimeout(acaoMsg._t); acaoMsg._t = setTimeout(() => hide(m), 3500);
}

// ---------- resultados ----------
function renderResultados(s) {
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
  for (const n of s.npcs) targets.appendChild(targetCard(n.id, true, n.emoji, n.name, `dá ${resSummary(n.da)} · quer ${resSummary(n.quer)}`, n));

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
  if (!s.outgoing.length) out.innerHTML = '<div class="empty-note">Você ainda não enviou propostas.</div>';
  for (const p of s.outgoing) {
    const div = document.createElement("div");
    div.className = "prop";
    const st = p.status.toLowerCase();
    div.innerHTML = `
      <div class="phead"><span>${p.counterpartEmoji ?? "❔"}</span><span class="pwho">${p.counterpartLabel}</span></div>
      <div class="pterms">Você dá <span class="give">${resSummary(p.offer)}</span> · quer <span class="get">${resSummary(p.request)}</span></div>
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
    $("tbNpcHint").textContent = `A ${npc.name} só fecha se você oferecer pelo menos o que ela pede. Pré-preenchi o negócio dela.`;
    show($("tbNpcHint"));
  } else {
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
conn.onreconnected(() => { if (myCode) toast("Reconectado."); });
conn.start().catch((e) => { console.error(e); toast("Falha ao conectar."); });
