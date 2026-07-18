// ===== Telão (TV) — cria a sala, mostra QR e o estado do jogo =====
const $ = (id) => document.getElementById(id);

let connection;
let roomCode = null;
let qr = null;
let countdownTimer = null;
let prevPhase = null;
let audioCtx = null;
let lastResultSeen = null;

async function start() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/contato")
    .withAutomaticReconnect()
    .build();

  connection.on("state", renderState);

  connection.onreconnected(async () => {
    // ao reconectar, cria uma sala nova só se ainda não temos uma
    if (!roomCode) roomCode = await connection.invoke("CreateRoom");
  });

  await connection.start();
  roomCode = await connection.invoke("CreateRoom");
}

function buildJoinUrl(code) {
  return `${location.origin}/contato/play.html?code=${code}`;
}

function renderState(s) {
  roomCode = s.code;

  // beep ao abrir a janela de contato (só na transição)
  if (s.phase === "ContactWindow" && prevPhase !== "ContactWindow") contactBeep();
  prevPhase = s.phase;

  $("roomCode").textContent = s.code;
  $("lobbyCode").textContent = s.code;
  $("roomPill").hidden = false;

  // QR + link (só monta uma vez)
  const url = buildJoinUrl(s.code);
  $("joinUrl").textContent = url;
  if (!qr) {
    qr = new QRCode($("qrcode"), { text: url, width: 220, height: 220,
      colorDark: "#0b0f1a", colorLight: "#ffffff" });
  }

  const inLobby = s.phase === "Lobby";
  $("lobby").hidden = !inLobby;
  $("round").hidden = inLobby;

  renderPlayers(s);
  renderScore(s);

  if (inLobby) {
    const actives = s.players.filter(p => p.connected).length;
    $("startBtn").disabled = actives < 3;
  } else {
    renderRound(s);
  }
}

function renderPlayers(s) {
  const ul = $("playerList");
  ul.innerHTML = "";
  s.players.forEach(p => {
    const li = document.createElement("li");
    li.className = "player" + (p.connected ? "" : " off");
    li.innerHTML = `<span class="avatar">${p.name.charAt(0).toUpperCase()}</span>
                    <span class="pname">${escapeHtml(p.name)}</span>`;
    ul.appendChild(li);
  });
  $("playerCount").textContent = s.players.filter(p => p.connected).length;
}

function renderScore(s) {
  const ul = $("scoreList");
  if (!ul) return;
  ul.innerHTML = "";
  [...s.players].sort((a, b) => b.score - a.score).forEach(p => {
    const li = document.createElement("li");
    li.innerHTML = `<span>${escapeHtml(p.name)}${p.isInterceptador ? " 🎯" : ""}</span>
                    <b>${p.score}</b>`;
    ul.appendChild(li);
  });
}

function renderRound(s) {
  const interceptor = s.players.find(p => p.id === s.interceptadorId);
  $("interceptorName").textContent = interceptor ? interceptor.name : "—";

  // palavra mascarada
  renderWord(s);

  // palavras queimadas pelo interceptador
  const burned = s.interceptGuesses || [];
  $("burnedStrip").hidden = burned.length === 0 || s.phase === "RoundOver";
  const bl = $("burnedStripList");
  bl.innerHTML = "";
  burned.forEach(w => {
    const chip = document.createElement("span");
    chip.className = "burned-chip";
    chip.textContent = w;
    bl.appendChild(chip);
  });

  // status
  let status = "";
  if (s.phase === "AwaitingWord") status = `${interceptor?.name || "Interceptador"} está escolhendo a palavra…`;
  else if (s.phase === "Playing") status = "Dêem dicas! Apertem CONTATO no celular quando combinarem.";
  else if (s.phase === "ContactWindow") status = "Alguém fez CONTATO! O interceptador pode tentar bloquear…";
  else if (s.phase === "RoundOver") status = "";
  $("statusLine").textContent = status;

  // barra de contato + contagem
  const cw = s.phase === "ContactWindow";
  $("contactBar").hidden = !cw;
  clearInterval(countdownTimer);
  if (cw && s.contactDeadline) {
    const tick = () => {
      const left = Math.max(0, Math.ceil((s.contactDeadline - Date.now()) / 1000));
      $("countdown").textContent = left;
    };
    tick();
    countdownTimer = setInterval(tick, 200);
  }

  // som do resultado (uma vez, na mudança)
  const rkey = s.phase + "|" + s.lastResult + "|" + s.lastResultWord + "|" + s.revealedCount;
  if (s.lastResult && rkey !== lastResultSeen && (s.phase === "Playing" || s.phase === "RoundOver")) {
    lastResultSeen = rkey;
    resultSound(s);
  }

  // banner de resultado
  const banner = $("resultBanner");
  if (s.phase === "RoundOver") {
    banner.hidden = false;
    banner.className = "result-banner win";
    banner.innerHTML = `🎉 Os jogadores venceram!`;
  } else if (s.lastResult && s.phase === "Playing") {
    banner.hidden = false;
    if (s.lastResult === "success") {
      banner.className = "result-banner success";
      banner.innerHTML = `✅ Contato! A palavra da dica era <b>${escapeHtml(s.lastResultWord)}</b>. Nova letra revelada!`;
    } else if (s.lastResult === "blocked") {
      banner.className = "result-banner blocked";
      banner.innerHTML = `🛡️ Interceptado! O interceptador adivinhou <b>${escapeHtml(s.lastResultWord)}</b>.`;
    } else {
      banner.className = "result-banner failed";
      banner.innerHTML = `❌ Sem contato — os palpites não bateram.`;
    }
  } else {
    banner.hidden = true;
  }

  // botão próxima rodada (só o telão)
  $("nextBtn").hidden = s.phase !== "RoundOver";
  // saída de emergência: reiniciar rodada (ex: interceptador caiu)
  const rb = $("restartBtn");
  if (rb) rb.hidden = !(s.phase === "AwaitingWord" || s.phase === "Playing");
}

function renderWord(s) {
  const box = $("wordDisplay");
  box.innerHTML = "";
  for (let i = 0; i < s.wordLength; i++) {
    const cell = document.createElement("div");
    const shown = i < s.revealedCount;
    cell.className = "letter" + (shown ? " on" : "");
    cell.textContent = shown ? s.revealed[i] : "";
    box.appendChild(cell);
  }
}

// ---- som ----
function ensureAudio() {
  try {
    if (!audioCtx) audioCtx = new (window.AudioContext || window.webkitAudioContext)();
    if (audioCtx.state === "suspended") audioCtx.resume();
  } catch (e) { /* sem áudio */ }
}
function tone(freq, startMs, durMs, type = "square", peak = 0.35) {
  if (!audioCtx) return;
  const t0 = audioCtx.currentTime + startMs / 1000;
  const osc = audioCtx.createOscillator();
  const g = audioCtx.createGain();
  osc.type = type;
  osc.frequency.value = freq;
  g.gain.setValueAtTime(0.0001, t0);
  g.gain.exponentialRampToValueAtTime(peak, t0 + 0.01);
  g.gain.exponentialRampToValueAtTime(0.0001, t0 + durMs / 1000);
  osc.connect(g); g.connect(audioCtx.destination);
  osc.start(t0); osc.stop(t0 + durMs / 1000 + 0.03);
}
function contactBeep() {
  ensureAudio();
  tone(660, 0, 130);
  tone(990, 150, 170);
}
function playDing() {   // contato certo
  ensureAudio();
  tone(988, 0, 150, "triangle", 0.4);
  tone(1319, 130, 260, "triangle", 0.4);
}
function playBuzz() {   // bloqueio
  ensureAudio();
  tone(160, 0, 200, "sawtooth", 0.32);
  tone(120, 210, 300, "sawtooth", 0.32);
}
function resultSound(s) {
  if (s.lastResult === "success") playDing();
  else if (s.lastResult === "blocked") playBuzz();
}

function escapeHtml(str) {
  return (str ?? "").replace(/[&<>"']/g, c =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

$("startBtn").addEventListener("click", () => { ensureAudio(); connection.invoke("StartRound", roomCode); });
$("nextBtn").addEventListener("click", () => { ensureAudio(); connection.invoke("StartRound", roomCode); });
$("restartBtn")?.addEventListener("click", () => { ensureAudio(); connection.invoke("StartRound", roomCode); });

start().catch(err => {
  $("foot").textContent = "Erro ao conectar: " + err;
  console.error(err);
});
