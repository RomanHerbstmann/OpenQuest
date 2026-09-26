// Cities and districts: draw the districts of a city on a map, import GeoJSON, see the leaderboard.
// A district is an ordered ring of points: the numbers on the map show the order in which the points are connected.

import { h, clear, toast } from './dom.js';
import { get, post, put, del, ApiError } from './api.js';
import { t } from './de.js';

const PALETTE = ['#2c8054', '#d49b38', '#3b6ea8', '#a8483b', '#7a5fa8', '#2f9aa3', '#8a8f2a', '#b5588c'];
const DEFAULT_VIEW = { lat: 51.16, lon: 10.45, zoom: 6 };
const TILES = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';

/** GeoJSON Polygon ([lon, lat], closed) for the points in their order. */
function polygonOf(points) {
  const ring = points.map((p) => [p.lon, p.lat]);
  if (ring.length) ring.push(ring[0]);
  return { type: 'Polygon', coordinates: [ring] };
}

function pointsOf(geometry) {
  const ring = geometry?.coordinates?.[0] ?? [];
  const points = ring.map(([lon, lat]) => ({ lat, lon }));
  if (points.length > 1 && points[0].lat === points.at(-1).lat && points[0].lon === points.at(-1).lon) points.pop();
  return points;
}

function describeProblem(problem) {
  const text = t.problems[problem.code];
  return text ? text(problem) : problem.message ?? problem.code;
}

export function mountDistricts(root) {
  const state = {
    cities: [], cityId: null, districts: [], selectedId: null,
    draft: null,            // { districtId, name, description, color, isActive, points: [{lat, lon}] }
    check: null,            // result of the last dry run { valid, problems }
    checking: false,
    period: 'all', ranking: [],
  };
  let checkTimer = null;
  let checkSeq = 0;

  // ---- layout -----------------------------------------------------------------------------------------------
  const citySelect = h('select', { 'aria-label': t.city.label, onchange: () => selectCity(citySelect.value) });
  const cityButtons = h('div', { class: 'row' },
    h('button', { type: 'button', onclick: () => openCityDialog(null) }, t.city.add),
    h('button', { type: 'button', id: 'edit-city', onclick: () => openCityDialog(currentCity()) }, t.city.edit));
  const districtList = h('ul', { class: 'district-list' });
  const districtActions = h('div', { class: 'row' },
    h('button', { type: 'button', class: 'primary', id: 'draw', onclick: () => startDraft(null) }, t.districts.draw),
    h('button', { type: 'button', id: 'import', onclick: openImportDialog }, t.districts.import));
  const editor = h('section', { class: 'card editor', hidden: true });
  const periodSelect = h('select', { 'aria-label': t.leaderboard.heading, onchange: () => { state.period = periodSelect.value; loadRanking(); } },
    h('option', { value: 'all' }, t.leaderboard.all), h('option', { value: 'week' }, t.leaderboard.week));
  const rankingList = h('ol', { class: 'ranking' });
  const mapEl = h('div', { class: 'map', id: 'map' });

  const sidebar = h('aside', { class: 'sidebar' },
    h('section', { class: 'card' }, h('h2', null, t.city.label), citySelect, cityButtons),
    h('section', { class: 'card' }, h('h2', null, t.districts.heading), districtActions, districtList),
    editor,
    h('section', { class: 'card' }, h('div', { class: 'row between' }, h('h2', null, t.leaderboard.heading),
      h('div', { class: 'row' }, periodSelect, h('button', { type: 'button', class: 'icon', title: t.leaderboard.refresh, 'aria-label': t.leaderboard.refresh, onclick: () => loadRanking().catch(reportError) }, '↻'))), rankingList));
  root.append(sidebar, h('div', { class: 'mapwrap' }, mapEl));

  // ---- map ---------------------------------------------------------------------------------------------------
  const map = L.map(mapEl, { zoomControl: true }).setView([DEFAULT_VIEW.lat, DEFAULT_VIEW.lon], DEFAULT_VIEW.zoom);
  L.tileLayer(TILES, { maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a>-Mitwirkende' }).addTo(map);
  const districtLayer = L.layerGroup().addTo(map);
  const draftLayer = L.layerGroup().addTo(map);
  const problemLayer = L.layerGroup().addTo(map);
  let vertexMarkers = [];
  let draftShape = null;
  map.on('click', (event) => { if (state.draft) addPoint(event.latlng); });
  new ResizeObserver(() => map.invalidateSize()).observe(mapEl); // the pane is hidden while another tab is open

  // ---- data --------------------------------------------------------------------------------------------------
  const currentCity = () => state.cities.find((c) => c.id === state.cityId) ?? null;

  async function loadCities(preferId) {
    state.cities = await get('/admin/cities');
    clear(citySelect);
    for (const c of state.cities) citySelect.append(h('option', { value: c.id, selected: c.id === (preferId ?? state.cityId) }, c.isActive ? c.name : `${c.name} (${t.districts.inactive})`));
    if (!state.cities.length) citySelect.append(h('option', { value: '' }, t.city.none));
    const id = state.cities.find((c) => c.id === (preferId ?? state.cityId))?.id ?? state.cities[0]?.id ?? null;
    await selectCity(id);
  }

  async function selectCity(id) {
    cancelDraft();
    state.cityId = id || null;
    state.selectedId = null;
    citySelect.value = state.cityId ?? '';
    document.getElementById('edit-city')?.toggleAttribute('disabled', !state.cityId);
    for (const id of ['draw', 'import']) document.getElementById(id)?.toggleAttribute('disabled', !state.cityId);
    await loadDistricts({ fit: true });
    await loadRanking();
  }

  async function loadDistricts({ fit = false } = {}) {
    state.districts = state.cityId ? await get(`/admin/cities/${state.cityId}/districts?geometry=true`) : [];
    renderDistrictList();
    renderDistrictLayer();
    if (fit) fitToCity();
  }

  async function loadRanking() {
    state.ranking = state.cityId ? await get(`/cities/${state.cityId}/leaderboard?period=${state.period}`) : [];
    clear(rankingList);
    if (!state.ranking.length) rankingList.append(h('li', { class: 'muted' }, t.leaderboard.empty));
    for (const r of state.ranking) {
      rankingList.append(h('li', { onclick: () => selectDistrict(r.districtId) },
        h('span', { class: 'rank' }, r.rank), h('span', { class: 'swatch', style: { background: r.color || '#999' } }),
        h('span', { class: 'grow' }, r.name), h('span', { class: 'muted' }, `${r.contributors} ${t.leaderboard.people}`), h('strong', null, `${r.points}`)));
    }
  }

  function fitToCity() {
    const bounds = L.latLngBounds([]);
    for (const d of state.districts) for (const p of pointsOf(d.geometry)) bounds.extend([p.lat, p.lon]);
    const city = currentCity();
    if (bounds.isValid()) map.fitBounds(bounds.pad(0.1));
    else if (city?.centerLat != null) map.setView([city.centerLat, city.centerLon], city.defaultZoom ?? 12);
    else map.setView([DEFAULT_VIEW.lat, DEFAULT_VIEW.lon], DEFAULT_VIEW.zoom);
  }

  // ---- list and district layer ------------------------------------------------------------------------------
  function renderDistrictList() {
    clear(districtList);
    if (!state.districts.length && state.cityId) districtList.append(h('li', { class: 'muted' }, t.districts.empty));
    for (const d of state.districts) {
      districtList.append(h('li', { class: `district${d.id === state.selectedId ? ' selected' : ''}${d.isActive ? '' : ' off'}`, onclick: () => selectDistrict(d.id) },
        h('div', { class: 'row' },
          h('span', { class: 'swatch', style: { background: d.color || '#999' } }),
          h('strong', { class: 'grow' }, d.name),
          !d.isActive && h('span', { class: 'badge' }, t.districts.inactive),
          h('span', { class: 'muted' }, `${d.totalPoints}`)),
        d.description && h('div', { class: 'muted small' }, d.description),
        d.id === state.selectedId && h('div', { class: 'row actions' },
          h('button', { type: 'button', onclick: (e) => { e.stopPropagation(); startDraft(d); } }, t.districts.edit),
          h('button', { type: 'button', onclick: (e) => { e.stopPropagation(); toggleActive(d); } }, d.isActive ? t.districts.deactivate : t.districts.activate),
          h('button', { type: 'button', class: 'danger', onclick: (e) => { e.stopPropagation(); removeDistrict(d); } }, t.districts.delete))));
    }
  }

  function renderDistrictLayer() {
    districtLayer.clearLayers();
    const overlapped = new Set((state.check?.problems ?? []).map((p) => p.districtId).filter(Boolean));
    for (const d of state.districts) {
      if (state.draft?.districtId === d.id) continue; // the district being edited is drawn as draft
      const points = pointsOf(d.geometry);
      if (points.length < 3) continue;
      const selected = d.id === state.selectedId;
      const clash = overlapped.has(d.id);
      const layer = L.polygon(points.map((p) => [p.lat, p.lon]), {
        color: clash ? '#c62828' : d.color || '#888', weight: selected || clash ? 3 : 1.5,
        fillColor: clash ? '#c62828' : d.color || '#888', fillOpacity: d.isActive ? (selected ? 0.4 : 0.22) : 0.05,
        dashArray: d.isActive ? null : '5 5', interactive: !state.draft,
      }).addTo(districtLayer);
      layer.bindTooltip(d.name, { sticky: true });
      layer.on('click', () => selectDistrict(d.id));
    }
  }

  function selectDistrict(id) {
    if (state.draft) return;
    state.selectedId = id;
    renderDistrictList();
    renderDistrictLayer();
    const d = state.districts.find((x) => x.id === id);
    const points = pointsOf(d?.geometry);
    if (points.length) map.fitBounds(L.latLngBounds(points.map((p) => [p.lat, p.lon])).pad(0.3));
  }

  // ---- draft (drawing and editing) ---------------------------------------------------------------------------
  function startDraft(district) {
    if (!state.cityId) return;
    state.selectedId = district?.id ?? null;
    state.draft = district
      ? { districtId: district.id, name: district.name, description: district.description ?? '', color: district.color || PALETTE[0], isActive: district.isActive, points: pointsOf(district.geometry) }
      : { districtId: null, name: '', description: '', color: PALETTE[state.districts.length % PALETTE.length], isActive: true, points: [] };
    state.check = null;
    renderDistrictList();
    renderDistrictLayer();
    renderEditor();
    renderDraft();
    if (state.draft.points.length >= 3) scheduleCheck(0);
    map.getContainer().classList.add('drawing');
  }

  function cancelDraft() {
    state.draft = null;
    state.check = null;
    clearTimeout(checkTimer);
    checkSeq++;
    draftLayer.clearLayers();
    problemLayer.clearLayers();
    vertexMarkers = [];
    draftShape = null;
    editor.hidden = true;
    clear(editor);
    map.getContainer().classList.remove('drawing');
    renderDistrictList();
    renderDistrictLayer();
  }

  function addPoint(latlng) {
    state.draft.points.push({ lat: round(latlng.lat), lon: round(latlng.lng) });
    changed();
  }

  function removePoint(index) {
    state.draft.points.splice(index, 1);
    changed();
  }

  const round = (value) => Math.round(value * 1e7) / 1e7;

  function changed() {
    renderDraft();
    renderEditorPoints();
    scheduleCheck(250);
  }

  function renderDraft() {
    draftLayer.clearLayers();
    problemLayer.clearLayers();
    vertexMarkers = [];
    draftShape = null;
    const d = state.draft;
    if (!d) return;
    const latlngs = d.points.map((p) => [p.lat, p.lon]);
    draftShape = d.points.length >= 3
      ? L.polygon(latlngs, { color: d.color, weight: 3, fillColor: d.color, fillOpacity: 0.3, dashArray: '6 4', interactive: false })
      : L.polyline(latlngs, { color: d.color, weight: 3, interactive: false });
    draftShape.addTo(draftLayer);
    d.points.forEach((p, i) => {
      const marker = L.marker([p.lat, p.lon], {
        draggable: true,
        icon: L.divIcon({ className: `vertex${i === 0 ? ' first' : ''}`, html: String(i + 1), iconSize: [22, 22] }),
        title: `${i + 1}`,
      }).addTo(draftLayer);
      marker.on('drag', (event) => { p.lat = round(event.latlng.lat); p.lon = round(event.latlng.lng); draftShape.setLatLngs(state.draft.points.map((q) => [q.lat, q.lon])); });
      marker.on('dragend', () => { renderEditorPoints(); scheduleCheck(50); renderProblemMarkers(); });
      marker.on('contextmenu', () => removePoint(i));
      vertexMarkers.push(marker);
    });
    renderProblemMarkers();
  }

  function renderProblemMarkers() {
    problemLayer.clearLayers();
    for (const p of state.check?.problems ?? []) {
      if (p.lat == null || p.lon == null) continue;
      L.circleMarker([p.lat, p.lon], { radius: 9, color: '#c62828', weight: 3, fillColor: '#fff', fillOpacity: 0.9, interactive: false }).addTo(problemLayer);
    }
  }

  function scheduleCheck(delay) {
    clearTimeout(checkTimer);
    const d = state.draft;
    if (!d || d.points.length < 3) { state.check = null; state.checking = false; renderStatus(); renderDistrictLayer(); return; }
    state.checking = true;
    renderStatus();
    checkTimer = setTimeout(runCheck, delay);
  }

  async function runCheck() {
    const d = state.draft;
    if (!d) return;
    const seq = ++checkSeq;
    try {
      const result = await post(`/admin/cities/${state.cityId}/districts/validate`, { geometry: polygonOf(d.points), districtId: d.districtId ?? undefined });
      if (seq !== checkSeq) return;
      state.check = result;
    } catch (error) {
      if (seq !== checkSeq) return;
      state.check = { valid: false, problems: [{ code: 'error', message: error.message }] };
    }
    state.checking = false;
    renderStatus();
    renderProblemMarkers();
    renderDistrictLayer();
  }

  // ---- editor form --------------------------------------------------------------------------------------------
  const statusBox = h('div', { class: 'status' });
  const pointsBox = h('ol', { class: 'points' });
  const saveButton = h('button', { type: 'button', class: 'primary', onclick: save }, t.editor.save);

  function renderEditor() {
    const d = state.draft;
    editor.hidden = false;
    clear(editor);
    editor.append(
      h('h2', null, d.districtId ? t.editor.editHeading : t.editor.newHeading),
      h('p', { class: 'muted small' }, t.editor.hint),
      h('label', null, t.editor.name, h('input', { type: 'text', value: d.name, maxlength: 128, oninput: (e) => { d.name = e.target.value; renderStatus(); } })),
      h('label', null, t.editor.description, h('textarea', { rows: 3, maxlength: 2000, oninput: (e) => { d.description = e.target.value; } }, d.description)),
      h('div', { class: 'row' },
        h('label', { class: 'inline' }, t.editor.color, h('input', { type: 'color', value: d.color, oninput: (e) => { d.color = e.target.value; renderDraft(); } })),
        d.districtId && h('label', { class: 'inline' }, h('input', { type: 'checkbox', checked: d.isActive, onchange: (e) => { d.isActive = e.target.checked; } }), t.editor.active)),
      h('h3', null, t.editor.pointsHeading), pointsBox,
      h('div', { class: 'row' },
        h('button', { type: 'button', onclick: () => { if (d.points.length) removePoint(d.points.length - 1); } }, t.editor.removeLast),
        h('button', { type: 'button', onclick: () => { d.points.length = 0; changed(); } }, t.editor.restart)),
      statusBox,
      h('div', { class: 'row' }, saveButton, h('button', { type: 'button', onclick: cancelDraft }, t.editor.cancel)));
    renderEditorPoints();
    renderStatus();
  }

  function renderEditorPoints() {
    clear(pointsBox);
    const d = state.draft;
    if (!d) return;
    if (!d.points.length) pointsBox.append(h('li', { class: 'muted' }, t.editor.noPoints));
    d.points.forEach((p, i) => pointsBox.append(h('li', null,
      h('span', { class: 'grow mono' }, `${p.lat.toFixed(6)}, ${p.lon.toFixed(6)}`),
      h('button', { type: 'button', class: 'icon', title: t.editor.remove, 'aria-label': t.editor.remove, onclick: () => removePoint(i) }, '×'))));
    renderStatus();
  }

  function renderStatus() {
    const d = state.draft;
    if (!d) return;
    clear(statusBox);
    const problems = state.check?.problems ?? [];
    let ready = false;
    if (d.points.length < 3) statusBox.append(h('p', { class: 'muted' }, t.editor.needPoints));
    else if (state.checking) statusBox.append(h('p', { class: 'muted' }, t.editor.checking));
    else if (state.check?.valid) { statusBox.append(h('p', { class: 'ok' }, t.editor.valid)); ready = true; }
    else for (const p of problems) statusBox.append(h('p', { class: 'problem' }, describeProblem(p)));
    if (!d.name.trim()) { statusBox.append(h('p', { class: 'muted' }, t.editor.needName)); ready = false; }
    saveButton.disabled = !ready;
  }

  async function save() {
    const d = state.draft;
    if (!d) return;
    saveButton.disabled = true;
    try {
      const geometry = polygonOf(d.points);
      let saved;
      if (d.districtId) {
        await put(`/admin/districts/${d.districtId}`, { name: d.name.trim(), description: d.description, color: d.color, isActive: d.isActive });
        saved = await put(`/admin/districts/${d.districtId}/geometry`, { geometry });
      } else {
        saved = await post(`/admin/cities/${state.cityId}/districts`, { name: d.name.trim(), description: d.description || undefined, color: d.color, geometry });
      }
      toast(t.districts.saved);
      cancelDraft();
      await loadDistricts();
      await loadRanking();
      selectDistrict(saved.id);
    } catch (error) {
      reportError(error);
      if (error.details?.problems) { state.check = { valid: false, problems: error.details.problems }; renderStatus(); renderProblemMarkers(); renderDistrictLayer(); }
      else saveButton.disabled = false;
    }
  }

  async function toggleActive(d) {
    try {
      await put(`/admin/districts/${d.id}`, { isActive: !d.isActive });
      await loadDistricts();
      await loadRanking();
    } catch (error) { reportError(error); }
  }

  async function removeDistrict(d) {
    if (!confirm(t.districts.confirmDelete)) return;
    try {
      await del(`/admin/districts/${d.id}`);
      toast(t.districts.deleted);
      state.selectedId = null;
      await loadDistricts();
      await loadRanking();
    } catch (error) { reportError(error); }
  }

  // ---- city dialog ----------------------------------------------------------------------------------------------
  function openCityDialog(city) {
    const creating = !city;
    const values = { key: '', name: '', countryCode: 'DE', centerLat: '', centerLon: '', defaultZoom: '', timezone: 'Europe/Berlin', isActive: true, ...(city ?? {}) };
    const error = h('p', { class: 'problem', hidden: true });
    const field = (label, name, attrs = {}) => h('label', null, label, h('input', { name, value: values[name] ?? '', ...attrs }));
    const form = h('form', { method: 'dialog', class: 'form' },
      h('h2', null, creating ? t.city.create : t.city.edit),
      field(t.city.name, 'name', { required: true, maxlength: 128 }),
      creating && field(t.city.key, 'key', { maxlength: 64, pattern: '[a-z0-9]+(-[a-z0-9]+)*' }),
      field(t.city.country, 'countryCode', { maxlength: 2 }),
      h('div', { class: 'row' }, field(t.city.centerLat, 'centerLat', { type: 'number', step: 'any' }), field(t.city.centerLon, 'centerLon', { type: 'number', step: 'any' }), field(t.city.zoom, 'defaultZoom', { type: 'number', min: 1, max: 22 })),
      h('button', { type: 'button', onclick: () => {
        const c = map.getCenter();
        form.elements.centerLat.value = round(c.lat); form.elements.centerLon.value = round(c.lng); form.elements.defaultZoom.value = map.getZoom();
      } }, t.city.useCenter),
      field(t.city.timezone, 'timezone', { maxlength: 64 }),
      !creating && h('label', { class: 'inline' }, h('input', { type: 'checkbox', name: 'isActive', checked: values.isActive }), t.city.active),
      error,
      h('div', { class: 'row' },
        h('button', { type: 'submit', class: 'primary', value: 'save' }, creating ? t.city.create : t.editor.save),
        h('button', { type: 'button', onclick: () => dialog.close() }, t.editor.cancel),
        !creating && h('button', { type: 'button', class: 'danger', onclick: async () => {
          if (!confirm(t.city.confirmDelete)) return;
          try { await del(`/admin/cities/${city.id}`); toast(t.city.deleted); dialog.close(); await loadCities(null); } catch (e) { error.textContent = e.message; error.hidden = false; }
        } }, t.city.delete)));
    const dialog = h('dialog', { class: 'dialog' }, form);
    document.body.append(dialog);
    dialog.addEventListener('close', () => dialog.remove());
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const data = new FormData(form);
      const num = (name) => (data.get(name) === '' || data.get(name) === null ? undefined : Number(data.get(name)));
      const body = {
        name: data.get('name')?.toString().trim(), countryCode: data.get('countryCode')?.toString().trim() || undefined,
        centerLat: num('centerLat'), centerLon: num('centerLon'), defaultZoom: num('defaultZoom'), timezone: data.get('timezone')?.toString().trim() || undefined,
      };
      if (creating && data.get('key')) body.key = data.get('key').toString().trim();
      if (!creating) body.isActive = form.elements.isActive.checked;
      try {
        const saved = creating ? await post('/admin/cities', body) : await put(`/admin/cities/${city.id}`, body);
        toast(t.city.saved);
        dialog.close();
        await loadCities(saved.id);
      } catch (e) { error.textContent = e.message; error.hidden = false; }
    });
    dialog.showModal();
  }

  // ---- GeoJSON import --------------------------------------------------------------------------------------------
  function openImportDialog() {
    let collection = null;
    const nameSelect = h('select', { name: 'name' });
    const descSelect = h('select', { name: 'description' });
    const result = h('div', { class: 'import-result' });
    const submit = h('button', { type: 'submit', class: 'primary', disabled: true }, t.import.submit);
    const fileInput = h('input', { type: 'file', accept: '.geojson,.json,application/geo+json,application/json', onchange: async (event) => {
      clear(result); collection = null; submit.disabled = true;
      const file = event.target.files[0];
      if (!file) return;
      try {
        collection = JSON.parse(await file.text());
        if (collection.type !== 'FeatureCollection' || !Array.isArray(collection.features) || !collection.features.length) throw new Error('not a collection');
      } catch { collection = null; result.append(h('p', { class: 'problem' }, t.import.invalidFile)); return; }
      const keys = [...new Set(collection.features.flatMap((f) => Object.keys(f.properties ?? {})))];
      clear(nameSelect); clear(descSelect);
      descSelect.append(h('option', { value: '' }, t.import.none));
      for (const k of keys) { nameSelect.append(h('option', { value: k }, k)); descSelect.append(h('option', { value: k }, k)); }
      nameSelect.value = keys.find((k) => /name/i.test(k)) ?? keys[0] ?? '';
      submit.disabled = !keys.length;
    } });
    const form = h('form', { method: 'dialog', class: 'form' },
      h('h2', null, t.import.heading),
      h('label', null, t.import.file, fileInput),
      h('label', null, t.import.name, nameSelect),
      h('label', null, t.import.description, descSelect),
      result,
      h('div', { class: 'row' }, submit, h('button', { type: 'button', onclick: () => dialog.close() }, t.import.cancel)));
    const dialog = h('dialog', { class: 'dialog' }, form);
    document.body.append(dialog);
    dialog.addEventListener('close', () => dialog.remove());
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!collection) return;
      submit.disabled = true;
      clear(result);
      try {
        const done = await post(`/admin/cities/${state.cityId}/districts/import`, {
          geoJson: collection, nameProperty: nameSelect.value, descriptionProperty: descSelect.value || undefined,
        });
        toast(t.import.done(done.created));
        dialog.close();
        await loadDistricts({ fit: true });
        await loadRanking();
      } catch (error) {
        submit.disabled = false;
        const features = error.details?.features;
        if (features) {
          result.append(h('p', { class: 'problem' }, t.import.failedHeading),
            h('ul', { class: 'import-problems' }, features.map((f) => h('li', null, h('strong', null, t.import.feature(f.index, f.name)),
              h('ul', null, f.problems.map((p) => h('li', null, describeProblem(p))))))));
        } else result.append(h('p', { class: 'problem' }, error.message));
      }
    });
    dialog.showModal();
  }

  function reportError(error) {
    if (error instanceof ApiError && error.details?.problems) toast(t.problems[error.details.problems[0].code]?.(error.details.problems[0]) ?? error.message, 'error');
    else toast(error instanceof ApiError ? error.message : t.errors.network, 'error');
  }

  return {
    async activate() {
      setTimeout(() => map.invalidateSize(), 0);
      try {
        if (!state.cities.length) await loadCities(null);
        else if (!state.draft) { await loadDistricts(); await loadRanking(); } // points arrive while another tab is open
      } catch (error) { reportError(error); }
    },
  };
}
