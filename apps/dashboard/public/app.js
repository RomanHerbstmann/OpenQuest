/* global maplibregl */
const $ = (id) => document.getElementById(id);

const I18N = {
  de: {
    eyebrow: "OpenQuest · Münster",
    title: "Münsters Stadtbäume",
    lede: (n, h) => `<b>${n}</b> Bäume aus dem Baumkataster, <b>${h}</b> davon mit gemessener Höhe. Frag einfach.`,
    placeholder: "z. B. größte Bäume, Birken in Hiltrup, Linden über 20 m …",
    examples: ["größte Bäume", "Birken", "älteste Eichen", "Platanen in der Innenstadt", "seltene Bäume", "Linden über 25 m", "Bäume ohne Gattung", "wie viele Ginkgos?"],
    thinking: "Jev denkt …",
    source: { jev: "Jev", rules: "Regeln" },
    sort: { none: "keine Sortierung", tallest: "höchste zuerst", shortest: "niedrigste zuerst", oldest: "älteste zuerst", youngest: "jüngste zuerst", rarest: "seltenste zuerst", most_common: "häufigste zuerst" },
    labels: { sort: "Sortierung", genus: "Gattung", area: "Gebiet", street: "Straße", height: "Höhe", intent: "Absicht", count: "Anzahl", list: "Liste" },
    notes: {
      age_is_height_proxy: "Das Alter ist im Baumkataster nicht erfasst. Sortiert wird nach der Höhe aus dem Oberflächenmodell als grobe Näherung.",
      below_2m_excluded: "Bäume unter 2 m im Oberflächenmodell sind ausgeblendet (meist Jungpflanzung, Rückschnitt oder nicht mehr vorhanden).",
      jev_unavailable: "Jev war nicht erreichbar, die Suche wurde mit einfachen Regeln interpretiert.",
    },
    noHeight: "keine Höhe",
    unknownGenus: "Gattung unbekannt",
    legendTitle: "Höhe (nDOM)",
    legendMatch: "Treffer",
    popup: { height: "Höhe", street: "Straße", district: "Bezirk", quarter: "Stadtteil", raw: "Kataster", id: "ID" },
    empty: "Keine Treffer.",
    error: "Die Suche ist fehlgeschlagen.",
    shown: (n, total) => (total > n ? `Top ${n} von ${total} gezeigt` : ""),
  },
  en: {
    eyebrow: "OpenQuest · Münster",
    title: "Münster's street trees",
    lede: (n, h) => `<b>${n}</b> trees from the city inventory, <b>${h}</b> with measured height. Just ask.`,
    placeholder: "e.g. tallest trees, birches in Hiltrup, lindens over 20 m …",
    examples: ["tallest trees", "birches", "oldest oaks", "plane trees in the city centre", "rare trees", "lindens over 25 m", "trees without genus", "how many ginkgos?"],
    thinking: "Jev is thinking …",
    source: { jev: "Jev", rules: "rules" },
    sort: { none: "no ordering", tallest: "tallest first", shortest: "lowest first", oldest: "oldest first", youngest: "youngest first", rarest: "rarest first", most_common: "most common first" },
    labels: { sort: "order", genus: "genus", area: "area", street: "street", height: "height", intent: "intent", count: "count", list: "list" },
    notes: {
      age_is_height_proxy: "Age is not recorded in the inventory. Trees are ordered by height from the surface model as a rough proxy.",
      below_2m_excluded: "Trees below 2 m in the surface model are hidden (mostly young plantings, pruned or no longer there).",
      jev_unavailable: "Jev was unreachable, the search was interpreted with simple rules.",
    },
    noHeight: "no height",
    unknownGenus: "genus unknown",
    legendTitle: "Height (nDOM)",
    legendMatch: "match",
    popup: { height: "Height", street: "Street", district: "District", quarter: "Quarter", raw: "Inventory", id: "ID" },
    empty: "No matches.",
    error: "Search failed.",
    shown: (n, total) => (total > n ? `Top ${n} of ${total} shown` : ""),
  },
};

const store = {
  get: (k, d) => {
    try { return localStorage.getItem(k) ?? d; } catch { return d; }
  },
  set: (k, v) => {
    try { localStorage.setItem(k, v); } catch { /* storage blocked */ }
  },
};

let lang = store.get("oq-lang", "de");
let theme = store.get("oq-theme", "dark");
let data = null;
let map = null;
let lastResult = null;
let markers = [];
const t = () => I18N[lang];
const nf = () => new Intl.NumberFormat(lang === "de" ? "de-DE" : "en-GB");

// OpenStreetMap raster tiles; dark mode inverts them in the raster paint (no second tile set, no key).
const BASEMAP_TILES = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
const BASEMAP_PAINT = {
  dark: { "raster-brightness-min": 1, "raster-brightness-max": 0, "raster-saturation": -0.85, "raster-contrast": -0.35, "raster-hue-rotate": 200, "raster-opacity": 0.9 },
  light: { "raster-brightness-min": 0, "raster-brightness-max": 1, "raster-saturation": -0.6, "raster-contrast": -0.1, "raster-hue-rotate": 0, "raster-opacity": 0.85 },
};
const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();
const HEIGHT_STOPS = { dark: ["#1d3a31", "#199e70", "#8fe3bf"], light: ["#cfeee1", "#1baf7a", "#0b5c3f"] };

function heightColor() {
  const [a, b, c] = HEIGHT_STOPS[theme];
  return ["case", ["<", ["get", "h"], 0], css("--ink-3"), ["interpolate", ["linear"], ["get", "h"], 0, a, 150, b, 300, c]];
}

/* ---------- i18n + theme ---------- */
function applyLang() {
  document.documentElement.lang = lang;
  $("lang").textContent = lang === "de" ? "EN" : "DE";
  document.querySelectorAll("[data-i18n]").forEach((el) => (el.textContent = t()[el.dataset.i18n]));
  document.querySelectorAll("[data-i18n-placeholder]").forEach((el) => (el.placeholder = t()[el.dataset.i18nPlaceholder]));
  if (data) $("lede").innerHTML = t().lede(nf().format(data.trees.id.length), nf().format(data.trees.height.filter((h) => h >= 0).length));
  $("examples").innerHTML = "";
  for (const ex of t().examples) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "chip";
    b.textContent = ex;
    b.onclick = () => ((($("q").value = ex)), search(ex));
    $("examples").append(b);
  }
  renderLegend();
  if (lastResult) renderResult(lastResult);
}

function applyTheme() {
  document.documentElement.dataset.theme = theme;
  if (!map) return;
  for (const [k, v] of Object.entries(BASEMAP_PAINT[theme])) map.setPaintProperty("basemap", k, v);
  if (map.getLayer("trees")) {
    map.setPaintProperty("trees", "circle-color", heightColor());
    map.setPaintProperty("matches", "circle-color", css("--s2"));
    map.setPaintProperty("matches", "circle-stroke-color", css("--page"));
  }
  renderLegend();
}

function renderLegend() {
  const [a, b, c] = HEIGHT_STOPS[theme];
  $("legend").innerHTML = `<div>${t().legendTitle}</div><div class="ramp" style="background:linear-gradient(90deg,${a},${b},${c})"></div><div class="scale"><span>0 m</span><span>15</span><span>30+</span></div>${lastResult ? `<div class="row"><span class="dot"></span>${t().legendMatch}</div>` : ""}`;
}

/* ---------- map ---------- */
function toGeoJSON(d) {
  const T = d.trees;
  const features = new Array(T.id.length);
  for (let i = 0; i < T.id.length; i++) {
    features[i] = { type: "Feature", geometry: { type: "Point", coordinates: [T.lon[i], T.lat[i]] }, properties: { i, h: T.height[i] } };
  }
  return { type: "FeatureCollection", features };
}

function initMap() {
  map = new maplibregl.Map({
    container: "map",
    center: [7.6261, 51.9607],
    zoom: 12.2,
    attributionControl: false,
    style: {
      version: 8,
      sources: {
        basemap: { type: "raster", tiles: [BASEMAP_TILES], tileSize: 256, maxzoom: 19, attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>' },
      },
      layers: [{ id: "basemap", type: "raster", source: "basemap", paint: BASEMAP_PAINT[theme] }],
    },
  });
  map.addControl(new maplibregl.NavigationControl({ showCompass: false }), "top-right");
  map.addControl(new maplibregl.AttributionControl({ compact: true, customAttribution: data.attribution.join(" · ") }), "bottom-right");

  map.on("load", () => {
    map.addSource("trees", { type: "geojson", data: toGeoJSON(data) });
    const radius = ["interpolate", ["linear"], ["zoom"], 10, 0.8, 13, 1.8, 16, 4, 19, 8];
    map.addLayer({ id: "trees", type: "circle", source: "trees", paint: { "circle-radius": radius, "circle-color": heightColor(), "circle-opacity": 0.85 } });
    map.addLayer({
      id: "matches",
      type: "circle",
      source: "trees",
      filter: ["==", ["get", "i"], -1],
      paint: {
        "circle-radius": ["interpolate", ["linear"], ["zoom"], 10, 1.8, 13, 3, 16, 6, 19, 10],
        "circle-color": css("--s2"),
        "circle-stroke-width": ["interpolate", ["linear"], ["zoom"], 12, 0, 15, 1],
        "circle-stroke-color": css("--page"),
      },
    });
    map.on("click", "trees", (e) => showPopup(e.features[0].properties.i, e.lngLat));
    map.on("click", "matches", (e) => showPopup(e.features[0].properties.i, e.lngLat));
    for (const l of ["trees", "matches"]) {
      map.on("mouseenter", l, () => (map.getCanvas().style.cursor = "pointer"));
      map.on("mouseleave", l, () => (map.getCanvas().style.cursor = ""));
    }
  });
}

function treeInfo(i) {
  const T = data.trees;
  const g = T.genus[i] >= 0 ? data.genera[T.genus[i]] : null;
  return {
    genus: g,
    height: T.height[i] >= 0 ? T.height[i] / 10 : null,
    street: T.street[i] >= 0 ? data.streets[T.street[i]] : null,
    district: T.district[i] >= 0 ? data.districts[T.district[i]] : null,
    quarter: T.quarter[i] >= 0 ? data.quarters[T.quarter[i]] : null,
    raw: data.rawGenus[T.raw[i]] || "",
    id: T.id[i],
  };
}

const esc = (s) => String(s ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

function showPopup(i, lngLat) {
  const info = treeInfo(i);
  const item = lastResult?.items.find((x) => x.index === i);
  const common = item ? (lang === "de" ? item.genusDe : item.genusEn) : null;
  const p = t().popup;
  const title = info.genus ? `${esc(common && common !== info.genus ? common : info.genus)} <i>${esc(info.genus)}</i>` : esc(t().unknownGenus);
  const rows = [
    [p.height, info.height !== null ? `${nf().format(info.height)} m` : t().noHeight],
    [p.street, info.street ?? "-"],
    [p.quarter, info.quarter ?? "-"],
    [p.district, info.district ?? "-"],
    [p.raw, info.raw || "-"],
  ];
  new maplibregl.Popup({ closeButton: true, maxWidth: "280px" })
    .setLngLat(lngLat ?? [data.trees.lon[i], data.trees.lat[i]])
    .setHTML(`<div class="pop-title">${title}</div><div class="pop-grid">${rows.map(([k, v]) => `<span>${esc(k)}</span><span>${esc(v)}</span>`).join("")}<span>${p.id}</span><span class="mono">${esc(info.id)}</span></div>`)
    .addTo(map);
}

/* ---------- search ---------- */
async function search(q) {
  q = q.trim();
  if (!q) return;
  document.body.classList.remove("is-hero");
  $("status").textContent = t().thinking;
  try {
    const res = await fetch("/api/search", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ q }) });
    if (!res.ok) throw new Error(String(res.status));
    lastResult = await res.json();
    $("status").textContent = lastResult.jevLatencyMs ? `${lastResult.jevLatencyMs} ms` : "";
    renderResult(lastResult, true);
  } catch {
    $("status").textContent = t().error;
  }
}

function renderResult(r, fly = false) {
  const tr = t();
  const i = r.interpretation;
  $("panel").hidden = false;
  $("summary").textContent = r.summary[lang];
  const pct = (c) => `${Math.round(c * 100)}%`;
  const tags = [`<span class="tag src">${tr.source[i.source]}</span>`];
  if (i.sort.value !== "none") tags.push(`<span class="tag">${tr.labels.sort} <b>${tr.sort[i.sort.value]}</b> ${pct(i.sort.confidence)}</span>`);
  if (i.genus) tags.push(`<span class="tag">${tr.labels.genus} <b>${esc(i.genus.value === "unknown" ? tr.unknownGenus : i.genus.value)}</b> ${pct(i.genus.confidence)}</span>`);
  if (i.area) tags.push(`<span class="tag">${tr.labels.area} <b>${esc(i.area.value)}</b> ${pct(i.area.confidence)}</span>`);
  if (i.street) tags.push(`<span class="tag">${tr.labels.street} <b>${esc(i.street)}</b></span>`);
  if (i.minHeightM !== undefined || i.maxHeightM !== undefined) tags.push(`<span class="tag">${tr.labels.height} <b>${i.minHeightM !== undefined ? `>${i.minHeightM}` : ""}${i.maxHeightM !== undefined ? ` <${i.maxHeightM}` : ""} m</b></span>`);
  if (i.intent.value === "count") tags.push(`<span class="tag">${tr.labels.intent} <b>${tr.labels.count}</b> ${pct(i.intent.confidence)}</span>`);
  $("interp").innerHTML = tags.join("");

  const notes = i.notes.map((n) => tr.notes[n]).filter(Boolean);
  $("note").hidden = !notes.length;
  $("note").textContent = notes.join(" ");

  const list = $("results");
  list.innerHTML = "";
  if (i.intent.value === "count") {
    const li = document.createElement("li");
    li.className = "count-big";
    li.style.display = "block";
    li.textContent = nf().format(r.total);
    list.append(li);
  }
  if (!r.items.length) {
    const li = document.createElement("li");
    li.textContent = tr.empty;
    list.append(li);
  }
  for (const it of r.items) {
    const li = document.createElement("li");
    const common = lang === "de" ? it.genusDe : it.genusEn;
    const name = it.genus ? `${esc(common)}${common !== it.genus ? `<i>${esc(it.genus)}</i>` : ""}` : esc(tr.unknownGenus);
    const where = [it.street, it.quarter].filter(Boolean).map(esc).join(" · ");
    li.innerHTML = `<span class="rank">${it.rank}</span><span><div class="name">${name}</div><div class="where">${where}</div></span><span class="h">${it.heightM !== null ? `${nf().format(it.heightM)}<small> m</small>` : `<small>${tr.noHeight}</small>`}</span>`;
    li.onclick = () => focusTree(it);
    list.append(li);
  }
  const shown = tr.shown(r.items.length, r.total);
  if (shown) {
    const li = document.createElement("li");
    li.className = "where";
    li.style.display = "block";
    li.textContent = shown;
    list.append(li);
  }
  renderLegend();
  if (!map?.getLayer("matches")) return;

  const all = r.total === data.trees.id.length;
  map.setFilter("matches", all ? ["==", ["get", "i"], -1] : ["in", ["get", "i"], ["literal", r.matches]]);
  map.setPaintProperty("trees", "circle-opacity", all ? 0.85 : 0.25);
  markers.forEach((m) => m.remove());
  markers = r.items.slice(0, 25).map((it) => {
    const el = document.createElement("div");
    el.className = "marker";
    el.textContent = it.rank;
    el.onclick = (e) => (e.stopPropagation(), focusTree(it));
    return new maplibregl.Marker({ element: el }).setLngLat([it.lon, it.lat]).addTo(map);
  });
  if (fly && r.items.length) {
    const pts = (r.total <= 2000 ? r.matches.map((k) => [data.trees.lon[k], data.trees.lat[k]]) : r.items.map((x) => [x.lon, x.lat]));
    const b = pts.reduce((acc, p) => acc.extend(p), new maplibregl.LngLatBounds(pts[0], pts[0]));
    const left = window.innerWidth > 760 ? 400 : 40;
    map.fitBounds(b, { padding: { top: 90, bottom: window.innerWidth > 760 ? 60 : window.innerHeight * 0.5, left, right: 60 }, maxZoom: 16.5, duration: 900 });
  }
}

function focusTree(it) {
  map.flyTo({ center: [it.lon, it.lat], zoom: Math.max(map.getZoom(), 17), duration: 700 });
  document.querySelectorAll(".marker").forEach((m) => m.classList.toggle("active", m.textContent === String(it.rank)));
  showPopup(it.index);
}

/* ---------- boot ---------- */
$("lang").onclick = () => {
  lang = lang === "de" ? "en" : "de";
  store.set("oq-lang", lang);
  applyLang();
};
$("theme").onclick = () => {
  theme = theme === "dark" ? "light" : "dark";
  store.set("oq-theme", theme);
  applyTheme();
};
$("form").onsubmit = (e) => (e.preventDefault(), search($("q").value));
$("close").onclick = () => {
  $("panel").hidden = true;
  lastResult = null;
  markers.forEach((m) => m.remove());
  map.setFilter("matches", ["==", ["get", "i"], -1]);
  map.setPaintProperty("trees", "circle-opacity", 0.85);
  renderLegend();
};

document.documentElement.dataset.theme = theme;
applyLang();
data = await (await fetch("/api/trees")).json();
applyLang();
initMap();
$("q").focus();
