import { clinicSlug, connectBoard, el, formatRange, minutes, percent } from "./common.js";
import { renderCurve, renderDay } from "./charts.js";

const slug = clinicSlug();
const api = `/api/clinics/${encodeURIComponent(slug)}`;
const KEY_STORAGE = "clinicqueue.staffKey";
const $ = (id) => document.getElementById(id);

let staffKey = sessionStorage.getItem(KEY_STORAGE);
let desk = null;
let forecastTimer = null;

$("lobby-link").href = `board.html?clinic=${encodeURIComponent(slug)}`;

async function call(path, { method = "GET", body } = {}) {
  const res = await fetch(api + path, {
    method,
    headers: { "X-Staff-Key": staffKey ?? "", ...(body ? { "Content-Type": "application/json" } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (res.status === 401) {
    sessionStorage.removeItem(KEY_STORAGE);
    staffKey = null;
    showKeyPrompt("That key was not accepted.");
    throw new Error("unauthorized");
  }
  const data = res.headers.get("content-type")?.includes("json") ? await res.json() : null;
  if (!res.ok) {
    const detail = data?.detail ?? Object.values(data?.errors ?? {}).flat().join(" ") ?? res.statusText;
    throw new Error(detail || `Request failed (${res.status})`);
  }
  return data;
}

function showError(message) {
  $("error").textContent = message ?? "";
}

async function act(fn) {
  showError("");
  try {
    await fn();
  } catch (err) {
    if (err.message !== "unauthorized") showError(err.message);
  }
}

function showKeyPrompt(message) {
  $("desk").hidden = true;
  $("key-panel").hidden = false;
  showError(message ?? "");
}

$("key-form").addEventListener("submit", (e) => {
  e.preventDefault();
  staffKey = $("key-input").value;
  sessionStorage.setItem(KEY_STORAGE, staffKey);
  start();
});

$("checkin-form").addEventListener("submit", (e) => {
  e.preventDefault();
  act(async () => {
    const visit = await call("/visits", {
      method: "POST",
      body: { initials: $("initials").value, reason: $("reason").value || null },
    });
    $("checkin-result").textContent = `Ticket ${visit.ticketCode} issued for ${visit.initials}.`;
    e.target.reset();
    $("initials").focus();
  });
});

$("call-next").addEventListener("click", () => act(() => call("/visits/call-next", { method: "POST" })));
$("staff-minus").addEventListener("click", () => setProviders(-1));
$("staff-plus").addEventListener("click", () => setProviders(+1));

function setProviders(delta) {
  if (!desk) return;
  const next = Math.max(0, desk.providersOnShift + delta);
  act(() => call("/staffing", { method: "PUT", body: { providersOnShift: next } }));
}

function stat(label, value, hint) {
  return el("div", { class: "stat" },
    el("div", { class: "label" }, label),
    el("div", { class: "value" }, value),
    hint ? el("div", { class: "hint" }, hint) : null);
}

function render(board) {
  desk = board;
  const m = board.model;
  document.title = `${board.name} – Front Desk`;
  $("clinic-name").textContent = board.name;
  $("providers").textContent = board.providersOnShift;
  $("walkin").textContent = `A patient checking in now: ${formatRange(board.walkInEstimate)}.`;

  const rec = $("recommendation");
  if (m.recommendedProviders == null) {
    rec.textContent = "demand exceeds any staffing level";
    rec.className = "pill bad";
  } else if (m.recommendedProviders > board.providersOnShift) {
    rec.textContent = `model suggests ${m.recommendedProviders}`;
    rec.className = "pill warn";
  } else {
    rec.textContent = `meets target (needs ${m.recommendedProviders})`;
    rec.className = "pill";
  }

  const target = `${percent(board.target.serviceLevel)} within ${board.target.withinMinutes} min`;
  $("stats").replaceChildren(
    stat("Arrivals / hour", m.arrivalsPerHour.toFixed(1),
      `${m.recentArrivals} in last ${Math.round(m.windowMinutes)} min · usual ${m.historicalArrivalsPerHour.toFixed(1)}`),
    stat("Visit length", minutes(m.visitMinutes), `from ${m.visitSamples} recent visits`),
    stat("Utilization", m.utilization == null ? "—" : percent(m.utilization),
      m.isStable ? "of provider time" : "over capacity: line will grow"),
    stat("Chance a patient waits", percent(m.probabilityOfWaiting), "Erlang C"),
    stat("Average wait", m.isStable ? minutes(m.meanWaitMinutes) : "∞", "steady state"),
    stat("Seen on time", m.isStable ? percent(m.serviceLevel) : "0%", `target ${target}`),
  );

  $("waiting").replaceChildren(...(board.waiting.length
    ? board.waiting.map((v) => el("tr", {},
        el("td", {}, el("strong", {}, v.ticketCode)),
        el("td", {}, v.initials),
        el("td", {}, v.reason ?? ""),
        el("td", { class: "num" }, minutes(v.waitedMinutes)),
        el("td", { class: "num" }, formatRange(v.estimatedWait, { short: true })),
        el("td", {},
          el("button", { onclick: () => act(() => call(`/visits/${v.id}/call`, { method: "POST" })) }, "Call"), " ",
          el("button", { onclick: () => act(() => call(`/visits/${v.id}/leave`, { method: "POST" })) }, "Left"))))
    : [el("tr", {}, el("td", { colspan: 6, class: "empty" }, "Nobody is waiting."))]));

  $("rooms").replaceChildren(...(board.inRoom.length
    ? board.inRoom.map((v) => el("tr", {},
        el("td", {}, el("strong", {}, v.ticketCode)),
        el("td", {}, v.initials),
        el("td", { class: "num" }, minutes(v.minutesInRoom)),
        el("td", {},
          el("button", { class: "primary", onclick: () => act(() => call(`/visits/${v.id}/complete`, { method: "POST" })) }, "Done"), " ",
          el("button", { onclick: () => act(() => call(`/visits/${v.id}/return`, { method: "POST" })) }, "Undo call"))))
    : [el("tr", {}, el("td", { colspan: 4, class: "empty" }, "All rooms are free."))]));

  $("call-next").disabled = board.waiting.length === 0 || board.inRoom.length >= board.providersOnShift;
}

async function refreshDesk() {
  await act(async () => render(await call("/desk")));
}

async function refreshForecast() {
  await act(async () => {
    const f = await call("/forecast");
    renderCurve($("curve"), f);
    renderDay($("day"), f);
  });
}

async function start() {
  $("key-panel").hidden = true;
  showError("");
  try {
    render(await call("/desk"));
  } catch (err) {
    if (err.message !== "unauthorized") showError(err.message);
    return;
  }
  $("desk").hidden = false;
  refreshForecast();

  // The hub pushes the public board; we use each push as a cue to re-fetch the staff view.
  connectBoard(slug, () => {
    refreshDesk();
    clearTimeout(forecastTimer);
    forecastTimer = setTimeout(refreshForecast, 1500);
  }, (s) => ($("status").textContent = s)).catch(() => {
    $("status").textContent = "polling";
    setInterval(refreshDesk, 10000);
  });
}

if (staffKey) start();
else showKeyPrompt();
