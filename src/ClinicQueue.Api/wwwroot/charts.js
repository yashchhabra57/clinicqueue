// Small hand-rolled SVG charts. No chart library: two bar charts don't justify the download.

const NS = "http://www.w3.org/2000/svg";

function svg(tag, attrs = {}, text) {
  const node = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) node.setAttribute(k, v);
  if (text != null) node.textContent = text;
  return node;
}

function frame(width, height) {
  return svg("svg", { viewBox: `0 0 ${width} ${height}`, width: "100%", role: "img" });
}

/** Service level (share seen within the target) for each provider count, with the target as a dashed line. */
export function renderCurve(container, forecast) {
  const W = 520, H = 220, L = 36, R = 8, T = 12, B = 34;
  const points = forecast.curve;
  const root = frame(W, H);
  const plotW = W - L - R, plotH = H - T - B;
  const slot = plotW / points.length;
  const y = (v) => T + plotH * (1 - v);

  for (const tick of [0, 0.5, 1]) {
    root.append(svg("line", { class: "axis", x1: L, x2: W - R, y1: y(tick), y2: y(tick) }));
    root.append(svg("text", { x: L - 6, y: y(tick) + 4, "text-anchor": "end" }, `${tick * 100}%`));
  }

  points.forEach((p, i) => {
    const x = L + i * slot + slot * 0.15;
    const w = slot * 0.7;
    const level = p.isStable ? p.serviceLevel : 0;
    const cls = ["bar", !p.isStable ? "unstable" : p.meetsTarget ? "" : "miss",
      p.providers === forecast.providersOnShift ? "current" : ""].join(" ").trim();
    const h = p.isStable ? Math.max(2, plotH * level) : plotH;
    const rect = svg("rect", { class: cls, x, width: w, y: p.isStable ? y(level) : T, height: h, rx: 3 });
    rect.append(svg("title", {}, p.isStable
      ? `${p.providers} providers: ${Math.round(level * 100)}% seen within ${forecast.target.withinMinutes} min, avg wait ${Math.round(p.meanWaitMinutes)} min`
      : `${p.providers} providers: not enough, the line grows without limit`));
    root.append(rect);
    root.append(svg("text", { x: x + w / 2, y: H - B + 16, "text-anchor": "middle" }, p.providers));
  });

  const ty = y(forecast.target.serviceLevel);
  root.append(svg("line", { class: "target", x1: L, x2: W - R, y1: ty, y2: ty }));
  root.append(svg("text", { x: W - R, y: ty - 4, "text-anchor": "end" }, `target ${Math.round(forecast.target.serviceLevel * 100)}%`));
  root.append(svg("text", { x: L + plotW / 2, y: H - 4, "text-anchor": "middle" }, "providers on shift (outlined = current)"));
  container.replaceChildren(root);
}

/** Average arrivals per hour of day (bars) with the recommended head count as a step line. */
export function renderDay(container, forecast) {
  const W = 520, H = 220, L = 30, R = 30, T = 12, B = 34;
  const open = forecast.day.filter((h) => h.arrivalsPerHour > 0);
  const hours = open.length ? forecast.day.slice(Math.max(0, open[0].hour - 1), Math.min(24, open[open.length - 1].hour + 2)) : forecast.day;
  const root = frame(W, H);
  const plotW = W - L - R, plotH = H - T - B;
  const slot = plotW / hours.length;
  const maxArrivals = Math.max(1, ...hours.map((h) => h.arrivalsPerHour));
  const maxStaff = Math.max(1, ...hours.map((h) => h.recommendedProviders ?? 0));

  root.append(svg("line", { class: "axis", x1: L, x2: W - R, y1: T + plotH, y2: T + plotH }));
  root.append(svg("text", { x: L - 4, y: T + 8, "text-anchor": "end" }, Math.ceil(maxArrivals)));
  root.append(svg("text", { x: W - R + 4, y: T + 8 }, maxStaff));

  let path = "";
  hours.forEach((h, i) => {
    const x = L + i * slot;
    const bh = (h.arrivalsPerHour / maxArrivals) * plotH;
    const rect = svg("rect", { class: "bar", x: x + slot * 0.15, width: slot * 0.7, y: T + plotH - bh, height: Math.max(0, bh), rx: 2, opacity: 0.55 });
    rect.append(svg("title", {}, `${h.hour}:00 – ${h.arrivalsPerHour} arrivals/hour, staff ${h.recommendedProviders ?? "?"}`));
    root.append(rect);
    const sy = T + plotH - ((h.recommendedProviders ?? 0) / maxStaff) * plotH;
    path += `${i === 0 ? "M" : "L"}${x},${sy} L${x + slot},${sy} `;
    if (i % 2 === 0) {
      const label = h.hour === 0 ? "12a" : h.hour < 12 ? `${h.hour}a` : h.hour === 12 ? "12p" : `${h.hour - 12}p`;
      root.append(svg("text", { x: x + slot / 2, y: H - B + 16, "text-anchor": "middle" }, label));
    }
  });
  root.append(svg("path", { class: "staff", d: path }));
  root.append(svg("text", { x: L + plotW / 2, y: H - 4, "text-anchor": "middle" }, "bars: arrivals/hour (left) · line: providers needed (right)"));
  container.replaceChildren(root);
}
