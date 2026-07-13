// ===== Celular do jogador =====
const $ = (id) => document.getElementById(id);

let connection;
let myId = null;
let roomCode = null;
let joined = false;
let lastResultSeen = null;
let cdTimer = null;
let prevPhase = null;
let audioCtx = null;
let curRevealed = "";   // prefixo revelado p/ exibir (ex: "FAR")
let curPrefix = "";     // mesmo prefixo normalizado p/ validar

// normalização igual à do servidor: MAIÚSCULO, sem acento, só letras/números
function norm(s) {
  return (s || "").normalize("NFD").replace(/\p{Diacritic}/gu, "").toUpperCase().replace(/[^A-Z0-9]/g, "");
}
function showFieldErr(id, msg) { const e = $(id); e.textContent = msg; e.hidden = false; }

const views = ["joinView", "waitView", "setWordView", "interceptView", "playerView"];
function show(view) {
  views.forEach(v => $(v).hidden = (v !== view));
}

async function start() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl("/gamehub")
    .withAutomaticReconnect()
    .build();

  connection.on("state", renderState);
  connection.on("secret", w => { $("secretWord").textContent = w || "—"; });

  await connection.start();
  myId = connection.connectionId;

  // pré-preenche o código vindo do QR (?code=ABCD)
  const params = new URLSearchParams(location.search);
  const code = params.get("code");
  if (code) $("codeInput").value = code.toUpperCase();
}

async function join() {
  const code = $("codeInput").value.trim().toUpperCase();
  const name = $("nameInput").value.trim();
  $("joinError").hidden = true;
  if (!code || !name) { showErr("Preencha código e nome."); return; }

  const res = await connection.invoke("JoinRoom", code, name);
  if (!res.ok) { showErr(res.error); return; }

  myId = res.playerId;
  roomCode = res.code;
  joined = true;
  $("roomCode").textContent = roomCode;
  $("roomPill").hidden = false;
}

function showErr(msg) { const e = $("joinError"); e.textContent = msg; e.hidden = false; }

function renderState(s) {
  if (!joined) return;
  roomCode = s.code;
  curRevealed = s.revealed || "";
  curPrefix = norm(curRevealed);

  const me = s.players.find(p => p.id === myId);
  const iAmInterceptor = me && me.id === s.interceptadorId;

  // som + vibração ao abrir a janela de contato (só na transição)
  if (s.phase === "ContactWindow" && prevPhase !== "ContactWindow") {
    contactAlert();
    $("finalInput").value = "";       // campo do chute final começa vazio
    $("finalErr").hidden = true;
  }
  prevPhase = s.phase;

  // feedback de resultado (toast) quando muda
  const key = s.phase + "|" + s.lastResult + "|" + s.lastResultWord + "|" + s.revealedCount;
  if (s.lastResult && key !== lastResultSeen && (s.phase === "Playing" || s.phase === "RoundOver")) {
    lastResultSeen = key;
    showToast(s);
    resultSound(s);
  }

  switch (s.phase) {
    case "Lobby":
      $("waitMsg").textContent = "Você entrou! Aguardando o telão iniciar a rodada…";
      show("waitView");
      break;

    case "AwaitingWord":
      if (iAmInterceptor) { $("secretInput").value = ""; show("setWordView"); }
      else { $("waitMsg").textContent = "O interceptador está escolhendo a palavra…"; show("waitView"); }
      break;

    case "Playing":
    case "ContactWindow":
      if (iAmInterceptor) renderInterceptor(s);
      else renderPlayer(s);
      break;

    case "RoundOver":
      $("waitMsg").textContent = "🎉 Vocês revelaram a palavra! Aguardando a próxima rodada…";
      show("waitView");
      break;
  }
}

function maskText(s) {
  let out = "";
  for (let i = 0; i < s.wordLength; i++) out += (i < s.revealedCount ? s.revealed[i] : "_") + " ";
  return out.trim();
}

function renderInterceptor(s) {
  show("interceptView");
  $("maskInfo").textContent = `Revelado: ${maskText(s)}`;
  const cw = s.phase === "ContactWindow";
  $("guessPanel").hidden = cw;      // esconde a fase livre durante o contato
  $("frozenPanel").hidden = !cw;
  if (cw) {
    startCountdown(s.contactDeadline, "icd");
    const used = !!s.interceptWindowGuessUsed;
    $("finalSent").hidden = !used;
    $("finalInput").disabled = used;
    $("finalBtn").disabled = used;
    $("finalInput").placeholder = curRevealed ? curRevealed + "…" : "Palpite final";
  } else {
    stopCountdown();
    renderBurned($("burnedMine"), s.interceptGuesses, false);
    $("clearBtn").hidden = (s.interceptGuesses || []).length === 0;
    $("prefixHintI").textContent = curRevealed ? `Os chutes precisam começar com "${curRevealed}"` : "";
    $("interceptInput").placeholder = curRevealed ? curRevealed + "…" : "Chutar palavra da dica";
  }
}

function renderPlayer(s) {
  show("playerView");
  $("maskInfoP").textContent = maskText(s);
  $("prefixHintP").textContent = curRevealed ? `(começa com "${curRevealed}")` : "";
  $("guessInput").placeholder = curRevealed ? curRevealed + "…" : "Ex: FACA";
  const guesses = s.interceptGuesses || [];
  $("burnedP").hidden = guesses.length === 0;
  renderBurned($("burnedPList"), guesses, true);
  const cw = s.phase === "ContactWindow";
  $("contactStatus").hidden = !cw;
  if (cw) {
    $("submittedCount").textContent = s.submittedPlayerIds.length;
    const mine = s.submittedPlayerIds.includes(myId);
    const my = $("mySubmit");
    my.hidden = !mine;
    if (mine) my.textContent = "Seu palpite foi enviado ✔ (pode reenviar até acabar o tempo)";
    startCountdown(s.contactDeadline, "ccd");
  } else {
    stopCountdown();
    $("mySubmit").hidden = true;
  }
}

// ---- som + vibração ----
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
function vibrate(pattern) { try { if (navigator.vibrate) navigator.vibrate(pattern); } catch (e) { /* ignore */ } }

function contactAlert() {
  vibrate([120, 60, 120]);
  ensureAudio();
  tone(660, 0, 130);    // dois beeps subindo = tensão do 3-2-1
  tone(990, 150, 170);
}
function playDing() {   // contato certo: duas notas alegres subindo
  ensureAudio();
  tone(988, 0, 150, "triangle", 0.4);
  tone(1319, 130, 260, "triangle", 0.4);
}
function playBuzz() {   // bloqueio: buzzer grave e áspero
  ensureAudio();
  tone(160, 0, 200, "sawtooth", 0.32);
  tone(120, 210, 300, "sawtooth", 0.32);
}
function resultSound(s) {
  if (s.lastResult === "success") { playDing(); vibrate(80); }
  else if (s.lastResult === "blocked") { playBuzz(); vibrate([200, 80, 200]); }
  // "failed" fica em silêncio
}

function renderBurned(container, words, danger) {
  container.innerHTML = "";
  (words || []).forEach(w => {
    const chip = document.createElement("span");
    chip.className = "burned-chip" + (danger ? " danger" : "");
    chip.textContent = w;
    container.appendChild(chip);
  });
}

function startCountdown(deadline, elId) {
  stopCountdown();
  if (!deadline) return;
  const tick = () => {
    const left = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
    const el = $(elId); if (el) el.textContent = left;
  };
  tick();
  cdTimer = setInterval(tick, 200);
}
function stopCountdown() { clearInterval(cdTimer); cdTimer = null; }

function showToast(s) {
  const t = $("toast");
  if (s.phase === "RoundOver") { t.textContent = "🎉 Vocês venceram a rodada!"; t.className = "toast win"; }
  else if (s.lastResult === "success") { t.textContent = `✅ Contato! (${s.lastResultWord}) Nova letra!`; t.className = "toast success"; }
  else if (s.lastResult === "blocked") { t.textContent = `🛡️ Interceptado! (${s.lastResultWord})`; t.className = "toast blocked"; }
  else if (s.lastResult === "failed") { t.textContent = "❌ Palpites não bateram."; t.className = "toast failed"; }
  else return;
  t.hidden = false;
  clearTimeout(t._h);
  t._h = setTimeout(() => { t.hidden = true; }, 3500);
}

// ---- ações ----
$("joinBtn").addEventListener("click", () => { ensureAudio(); join().catch(e => showErr("" + e)); });
$("nameInput").addEventListener("keydown", e => { if (e.key === "Enter") $("joinBtn").click(); });

$("setWordBtn").addEventListener("click", () => {
  const w = $("secretInput").value.trim();
  if (w.replace(/[^a-zA-ZÀ-ÿ]/g, "").length < 2) return;
  connection.invoke("SetSecretWord", roomCode, w);
});

$("contactBtn").addEventListener("click", () => {
  const w = $("guessInput").value.trim();
  if (!w) { $("guessInput").focus(); return; }
  if (curPrefix && !norm(w).startsWith(curPrefix)) {
    showFieldErr("contactErr", `A palavra precisa começar com "${curRevealed}".`);
    return;
  }
  $("contactErr").hidden = true;
  connection.invoke("SubmitContact", roomCode, w);
});
$("guessInput").addEventListener("keydown", e => { if (e.key === "Enter") $("contactBtn").click(); });
$("guessInput").addEventListener("input", () => { $("contactErr").hidden = true; });

$("interceptBtn").addEventListener("click", () => {
  const w = $("interceptInput").value.trim();
  if (!w) return;
  if (curPrefix && !norm(w).startsWith(curPrefix)) {
    showFieldErr("interceptErr", `O chute precisa começar com "${curRevealed}".`);
    return;
  }
  $("interceptErr").hidden = true;
  connection.invoke("SubmitIntercept", roomCode, w);
  $("interceptInput").value = "";
  // cooldown visual de 2s (o servidor também limita)
  const btn = $("interceptBtn");
  btn.disabled = true;
  let left = 2;
  btn.textContent = `Aguarde ${left}s…`;
  const t = setInterval(() => {
    left--;
    if (left <= 0) { clearInterval(t); btn.disabled = false; btn.textContent = "Chutar 🔥"; }
    else btn.textContent = `Aguarde ${left}s…`;
  }, 1000);
});
$("interceptInput").addEventListener("keydown", e => { if (e.key === "Enter") $("interceptBtn").click(); });
$("interceptInput").addEventListener("input", () => { $("interceptErr").hidden = true; });

$("clearBtn").addEventListener("click", () => connection.invoke("ClearInterceptGuesses", roomCode));

// chute único do interceptador durante a janela de contato
$("finalBtn").addEventListener("click", () => {
  const w = $("finalInput").value.trim();
  if (!w) return;
  if (curPrefix && !norm(w).startsWith(curPrefix)) {
    showFieldErr("finalErr", `O chute precisa começar com "${curRevealed}".`);
    return;
  }
  $("finalErr").hidden = true;
  connection.invoke("SubmitIntercept", roomCode, w);
});
$("finalInput").addEventListener("keydown", e => { if (e.key === "Enter") $("finalBtn").click(); });
$("finalInput").addEventListener("input", () => { $("finalErr").hidden = true; });

start().catch(err => { $("foot").textContent = "Erro: " + err; console.error(err); });
