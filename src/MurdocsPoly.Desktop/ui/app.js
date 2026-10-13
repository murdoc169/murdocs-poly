"use strict";
const $ = s => document.querySelector(s);
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

// ---- bridge to the app --------------------------------------------------------------------------------
const pending = new Map();
let nextId = 1;
function call(cmd, args = {}) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ id, cmd, args });
  });
}
window.chrome?.webview?.addEventListener("message", e => {
  const m = e.data, p = pending.get(m.id);
  if (!p) return;
  pending.delete(m.id);
  m.ok ? p.resolve(m.data) : p.reject(new Error(m.error));
});

// ---- state --------------------------------------------------------------------------------------------
const TABS = [["Trending", ""], ["Politics", "politics"], ["Sports", "sports"], ["Crypto", "crypto"], ["Esports", "esports"], ["Finance", "finance"],
  ["Geopolitics", "geopolitics"], ["Tech", "tech"], ["Elections", "elections"], ["Culture", "culture"], ["Economy", "economy"]];
const SPORTS = [
  ["NFL", "🏈", ["NFL"]], ["MLB", "⚾", ["MLB"]], ["College Football", "🏟️", ["College Football", "CFB"]], ["UFC", "🥊", ["UFC"]],
  ["UEFA Champions League", "⭐", ["UCL"]], ["UNL", "🌍", ["UNL"]], ["Soccer", "⚽", ["Soccer"]], ["Tennis", "🎾", ["Tennis"]], ["Cricket", "🏏", ["Cricket"]],
  ["Basketball", "🏀", ["Basketball", "NBA"]], ["Baseball", "⚾", ["Baseball"]], ["Football", "🏈", ["Football"]], ["Hockey", "🏒", ["Hockey", "NHL"]],
  ["Rugby", "🏉", ["Rugby"]], ["Table Tennis", "🏓", ["Table Tennis"]], ["Darts", "🎯", ["Darts"]], ["Handball", "🤾", ["Handball"]], ["Golf", "⛳", ["Golf"]],
  ["Combat", "🥋", ["UFC", "Boxing", "MMA"]], ["Esports", "🎮", ["Esports"]],
];
const S = { nav: "sports", view: "live", sport: null, query: "", cards: [], markets: {}, sel: null, side: "buy", mode: "market", amount: "", limit: "", msg: null, status: null };

// ---- helpers ------------------------------------------------------------------------------------------
const shade = (hex, f) => {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex || "");
  if (!m) return null;
  const n = parseInt(m[1], 16);
  const c = k => Math.round(((n >> k) & 255) * f);
  return `rgb(${c(16)},${c(8)},${c(0)})`;
};
const cents = c => (c == null ? "--" : `${c}¢`);
const num = s => { const v = parseFloat(String(s).replace(",", ".")); return isFinite(v) ? v : 0; };
const money = v => "$" + v.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

function btnHtml(b, card, extra = "") {
  const sel = S.sel && S.sel.marketId === b.marketId && S.sel.index === b.index ? " sel" : "";
  const off = b.cents == null ? " off" : "";
  let cls = "btn", style = "";
  if (b.kind === "moneyline") {
    if (b.label === "DRAW") cls += " draw";
    else { cls += " team"; const bg = shade(b.color, 0.7); if (bg) style = `background:${bg};`; }
  }
  if (b.kind === "yes") cls += " yes";
  if (b.kind === "no") cls += " no";
  return `<button class="${cls}${sel}${off}${extra}" style="${style}" data-m="${esc(b.marketId)}" data-i="${b.index}" data-e="${esc(card.eventId)}"><span class="lb">${esc(b.label)}</span><span>${cents(b.cents)}</span></button>`;
}

const ICON_BOOK = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M12 4v16M7 9h2M7 13h2M15 9h2M15 13h2"/></svg>';

function logo(t) {
  return t.logo ? `<img class="logo-img" src="${esc(t.logo)}" alt="" onerror="this.outerHTML='<span class=&quot;logo-fb&quot;>${esc((t.abbr || t.name).slice(0, 3).toUpperCase())}</span>'">`
                : `<span class="logo-fb">${esc((t.abbr || t.name).slice(0, 3).toUpperCase())}</span>`;
}

function gameCard(c, flags) {
  const head = `<div class="head">${c.live ? '<span class="dot"></span>' : ""}<span class="st${c.live ? "" : " plain"}">${esc(c.status)}</span><span class="vol">${esc(c.volumeText)}</span>` +
    `<div class="icons"><button class="ibtn" data-ladder="${esc(c.eventId)}" title="Open the ladder">${ICON_BOOK}</button></div></div>`;
  const teams = c.teams.map(t => `<div class="team">${t.score != null ? (/\s/.test(t.score) ? `<span class="sets">${esc(t.score)}</span>` : `<span class="score">${esc(t.score)}</span>`) : ""}${logo(t)}<span class="nm">${esc(t.name)}<span class="rec">${esc(t.record ?? "")}</span></span></div>`).join("");
  const group = (list, cls) => list.length ? `<div class="group ${cls}">${list.map(b => btnHtml(b, c)).join("")}</div>` : (flags[cls || "x"] ? `<div class="group ${cls}"></div>` : "");
  return `<div class="card">${head}<div class="body"><div class="teams">${teams}</div>${group(c.moneyline, "ml")}${group(c.spread, "sp")}${group(c.total, "to")}</div></div>`;
}

function plainCard(c) {
  const head = `<div class="head">${c.live ? '<span class="dot"></span><span class="st">LIVE</span>' : ""}<span class="vol">${esc(c.volumeText)}</span>` +
    `<div class="icons"><button class="ibtn" data-ladder="${esc(c.eventId)}" title="Open the ladder">${ICON_BOOK}</button></div></div>`;
  const title = `<div class="plain-title">${c.image ? `<img src="${esc(c.image)}" alt="" onerror="this.remove()">` : ""}<span>${esc(c.title)}</span></div>`;
  const rows = c.rows.map(r => `<div class="row"><span class="tx" title="${esc(r.text)}">${esc(r.text)}</span>${r.buttons.map(b => btnHtml(b, c)).join("")}</div>`).join("");
  return `<div class="card">${head}${title}${rows}</div>`;
}

// ---- rendering ----------------------------------------------------------------------------------------
function renderTabs() {
  $("#tabs").innerHTML = TABS.map(([l, t]) => `<button class="${S.nav === (t || "trending") ? "on" : ""}" data-nav="${t || "trending"}">${l}</button>`).join("");
}

function renderSide() {
  const el = $("#side");
  const on = S.nav === "sports" && !S.query;
  $("#page").classList.toggle("nosidebar", !on);
  el.style.display = on ? "" : "none";
  if (!on) return;
  el.innerHTML =
    `<button class="item ${S.view === "live" && !S.sport ? "on" : ""}" data-view="live"><span class="ic red">●</span>Live</button>` +
    `<button class="item ${S.view === "futures" ? "on" : ""}" data-view="futures"><span class="ic">▥</span>Futures</button><hr><div class="cap">ALL SPORTS</div>` +
    SPORTS.map(([n, ic]) => `<button class="item ${S.sport === n ? "on" : ""}" data-sport="${esc(n)}"><span class="ic">${ic}</span>${esc(n)}</button>`).join("");
}

function filtered() {
  if (!(S.nav === "sports" && S.view === "live" && S.sport)) return S.cards;
  const keys = SPORTS.find(s => s[0] === S.sport)[2].map(k => k.toLowerCase());
  return S.cards.filter(c => (c.tags || []).some(t => keys.includes(t.toLowerCase())) || keys.includes((c.league || "").toLowerCase()));
}

function renderMain() {
  const label = S.query ? `Results for “${S.query}”` : S.nav === "sports" ? (S.view === "live" ? "Sports Live" : "Sports Futures") : (TABS.find(t => (t[1] || "trending") === S.nav) || ["Markets"])[0];
  const cards = filtered();
  let html = `<h1>${esc(label)}</h1>`;
  if (!cards.length) html += `<div class="empty">${S.loading ? "Loading..." : "Nothing here right now."}</div>`;
  else if (cards.some(c => c.kind === "game")) {
    const groups = new Map();
    for (const c of cards) { const k = c.league || "Other"; if (!groups.has(k)) groups.set(k, []); groups.get(k).push(c); }
    for (const [league, list] of groups) {
      const flags = { ml: list.some(c => c.moneyline.length), sp: list.some(c => c.spread.length), to: list.some(c => c.total.length) };
      html += `<div class="league"><h2>${esc(league)}</h2>${flags.ml ? '<span class="col ml">Moneyline</span>' : ""}${flags.sp ? '<span class="col">Spread</span>' : ""}${flags.to ? '<span class="col">Total</span>' : ""}</div>`;
      html += list.map(c => c.kind === "game" ? gameCard(c, flags) : plainCard(c)).join("");
    }
  } else html += cards.map(plainCard).join("");
  const main = $("#main"), y = window.scrollY;
  main.innerHTML = html;
  window.scrollTo(0, y);
}

function renderTrade() {
  const el = $("#trade");
  if (!S.sel || !S.markets[S.sel.marketId]) {
    el.innerHTML = `<div class="panel"><div class="hint">Pick a price on any card to trade it.</div></div><div class="note">${esc(S.status?.why || "")}</div>`;
    return;
  }
  const m = S.markets[S.sel.marketId], o = m.outcomes;
  const cur = o[S.sel.index], p = (S.mode === "limit" && num(S.limit) > 0 ? num(S.limit) : (cur.cents ?? 0)) / 100;
  const amt = num(S.amount);
  let summary = "";
  if (amt > 0 && p > 0) {
    const shares = Math.floor((amt / p) * 100) / 100;
    summary = S.side === "buy" ? `${shares} shares · pays ${money(shares)} if ${esc(cur.name)} wins · profit ${money(shares - amt)}` : `Sells about ${shares} shares for ${money(amt)}`;
  }
  const trading = S.status && S.status.why === "";
  el.innerHTML =
    `<div class="panel"><div class="ev"><div><div class="t">${esc(S.sel.title)}</div><div class="o">${esc(m.question)}</div></div></div>` +
    `<div class="tabs2"><button class="${S.side === "buy" ? "on" : ""}" data-side="buy">Buy</button><button class="${S.side === "sell" ? "on" : ""}" data-side="sell">Sell</button>` +
    `<select id="mode"><option value="market" ${S.mode === "market" ? "selected" : ""}>Market</option><option value="limit" ${S.mode === "limit" ? "selected" : ""}>Limit</option></select></div>` +
    `<div class="outs">${o.map((x, i) => `<button class="btn o${i}${i === S.sel.index ? " sel" : ""}" data-pick="${i}">${esc(x.name)} ${cents(x.cents)}</button>`).join("")}</div>` +
    `<div class="limit ${S.mode === "limit" ? "on" : ""}"><span class="lb">Limit price (¢)</span><input id="limit" inputmode="decimal" value="${esc(S.limit)}" placeholder="${cur.cents ?? ""}"></div>` +
    `<div class="amount"><span class="lb">Amount</span><input id="amount" inputmode="decimal" placeholder="$0" value="${S.amount ? "$" + esc(S.amount) : ""}" class="${S.amount ? "has" : ""}"></div>` +
    `<div class="chips">${[1, 5, 10, 100].map(v => `<button data-add="${v}">+$${v}</button>`).join("")}</div>` +
    `<div class="summary">${summary}</div>` +
    `<button class="go" id="go" ${trading ? "" : "disabled"}>${trading ? "Trade" : "Read-only"}</button>` +
    (S.msg ? `<div class="msg ${S.msg.ok ? "ok" : "err"}">${esc(S.msg.text)}</div>` : "") + `</div>` +
    `<div class="note">${esc(S.status?.why || "Orders are sent only where Polymarket allows trading.")}</div>`;
}

function renderStatus() {
  $("#status").textContent = S.status ? (S.status.why === "" ? "Trading on" : S.status.why.replace(/^Read-only\.\s*/, "Read-only: ")) : "";
}

function renderAll() { renderTabs(); renderSide(); renderMain(); renderTrade(); renderStatus(); }

// ---- data ---------------------------------------------------------------------------------------------
let loadSeq = 0;
async function load(silent) {
  const seq = ++loadSeq;
  if (!silent) { S.loading = true; S.cards = []; renderMain(); }
  try {
    const data = await call("cards", { nav: S.nav, view: S.view, query: S.query });
    if (seq !== loadSeq) return;
    S.cards = data.cards;
    S.markets = {};
    for (const c of S.cards) for (const m of c.markets) S.markets[m.id] = m;
  } catch (e) { if (!silent) S.cards = []; console.error(e); }
  S.loading = false;
  renderMain(); renderTrade();
}

async function refreshStatus() { try { S.status = await call("status"); } catch { S.status = { why: "Read-only." }; } renderStatus(); renderTrade(); }

// ---- events -------------------------------------------------------------------------------------------
document.addEventListener("click", async e => {
  const t = e.target.closest("[data-nav],[data-view],[data-sport],[data-m],[data-ladder],[data-side],[data-pick],[data-add],#go,#acct");
  if (!t) return;
  if (t.dataset.nav) { S.nav = t.dataset.nav; S.query = ""; $("#q").value = ""; S.view = "live"; S.sport = null; renderTabs(); renderSide(); load(); }
  else if (t.dataset.view) { S.view = t.dataset.view; S.sport = null; renderSide(); load(); }
  else if (t.dataset.sport) { S.sport = S.sport === t.dataset.sport ? null : t.dataset.sport; S.view = "live"; renderSide(); renderMain(); }
  else if (t.dataset.m) {
    const card = S.cards.find(c => c.eventId === t.dataset.e);
    S.sel = { marketId: t.dataset.m, index: +t.dataset.i, eventId: t.dataset.e, title: card ? card.title : "" };
    S.msg = null; renderMain(); renderTrade();
  }
  else if (t.dataset.ladder) {
    const card = S.cards.find(c => c.eventId === t.dataset.ladder);
    const mid = S.sel && S.sel.eventId === t.dataset.ladder ? S.sel.marketId : (card?.moneyline[0]?.marketId ?? card?.rows[0]?.buttons[0]?.marketId);
    try { await call("ladder", { eventId: t.dataset.ladder, marketId: mid }); } catch (err) { S.msg = { ok: false, text: err.message }; renderTrade(); }
  }
  else if (t.dataset.side) { S.side = t.dataset.side; renderTrade(); }
  else if (t.dataset.pick) { S.sel.index = +t.dataset.pick; renderMain(); renderTrade(); }
  else if (t.dataset.add) { S.amount = String(+(num(S.amount) + +t.dataset.add).toFixed(2)); renderTrade(); }
  else if (t.id === "acct") { try { S.status = await call("account"); } catch (err) { console.error(err); } renderStatus(); renderTrade(); }
  else if (t.id === "go") {
    t.disabled = true; S.msg = { ok: true, text: "Sending..." }; renderTrade();
    try {
      const r = await call("trade", { marketId: S.sel.marketId, index: S.sel.index, side: S.side, mode: S.mode, amount: num(S.amount), limitCents: num(S.limit) });
      S.msg = { ok: !!r.ok, text: r.text };
    } catch (err) { S.msg = { ok: false, text: err.message }; }
    renderTrade(); load(true);
  }
});

document.addEventListener("input", e => {
  if (e.target.id === "amount") {
    const v = e.target.value.replace(/[^0-9.,]/g, "").replace(",", ".");
    S.amount = v; e.target.value = v ? "$" + v : ""; e.target.classList.toggle("has", !!v);
    const sm = document.querySelector(".summary"); if (sm) { const keep = S.msg; renderTradeKeepFocus(); S.msg = keep; }
  } else if (e.target.id === "limit") { S.limit = e.target.value.replace(/[^0-9.,]/g, ""); renderTradeKeepFocus("limit"); }
});
document.addEventListener("change", e => { if (e.target.id === "mode") { S.mode = e.target.value; renderTrade(); } });

function renderTradeKeepFocus(id = "amount") {
  const pos = document.getElementById(id)?.selectionStart;
  renderTrade();
  const el = document.getElementById(id);
  if (el) { el.focus(); try { el.setSelectionRange(el.value.length, el.value.length); } catch { } }
}

$("#q").addEventListener("keydown", e => {
  if (e.key !== "Enter") return;
  S.query = e.target.value.trim(); S.sport = null; renderSide(); load();
});

// ---- go -----------------------------------------------------------------------------------------------
renderAll();
refreshStatus();
load();
setInterval(() => { if (!document.hidden) load(true); }, 12000);
setInterval(refreshStatus, 120000);
