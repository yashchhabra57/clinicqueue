// Shared helpers for the lobby board and the front-desk console.

export function clinicSlug() {
  return new URLSearchParams(location.search).get("clinic") || "northside";
}

/** "about 8 min (4-14)" style text for a {mean, low, high} range, or a fallback. */
export function formatRange(range, { short = false } = {}) {
  if (!range) return "no estimate";
  if (range.mean < 0.5) return "next up";
  const mean = Math.max(1, Math.round(range.mean));
  if (short) return `~${mean} min`;
  const low = Math.max(0, Math.round(range.low));
  const high = Math.max(mean, Math.round(range.high));
  return `about ${mean} min (${low}–${high})`;
}

export function minutes(value) {
  if (value == null) return "—";
  return `${Math.round(value)} min`;
}

export function percent(value) {
  if (value == null) return "—";
  return `${Math.round(value * 100)}%`;
}

export function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (key === "class") node.className = value;
    else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
    else if (value !== false && value != null) node.setAttribute(key, value === true ? "" : value);
  }
  for (const child of children.flat()) {
    if (child == null || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

/**
 * Connects to the board hub, joins the clinic and calls onBoard with every pushed snapshot.
 * SignalR reconnects on its own; we re-join after each reconnect because groups don't survive it.
 */
export async function connectBoard(slug, onBoard, onStatus) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/board")
    .withAutomaticReconnect()
    .build();

  connection.on("board", onBoard);
  connection.onreconnecting(() => onStatus("reconnecting…"));
  connection.onreconnected(async () => {
    onStatus("live");
    onBoard(await connection.invoke("Join", slug));
  });
  connection.onclose(() => onStatus("offline"));

  await connection.start();
  onStatus("live");
  onBoard(await connection.invoke("Join", slug));
  return connection;
}
