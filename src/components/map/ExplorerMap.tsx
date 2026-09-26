'use client';

import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import { districts } from '@/data/districts';
import { treeSearchText } from '@/i18n/treeSearch';
import { districtStandings } from '@/lib/districts';
import type { District, TerritoryContribution } from '@/types/district';
import type { Tree } from '@/types/tree';
import type { ResultItem, TreeSearchResponse } from '@/types/treeSearch';

type Props = { trees: Tree[]; densityTrees: Tree[]; selectedId: string | null; onSelect: (tree: Tree) => void; userPosition: { lat: number; lng: number } | null; locateTick: number; focusTree: Tree | null; focusTick: number; contributions: TerritoryContribution[]; selectedDistrictId: string | null; onSelectDistrict: (district: District) => void; onInventoryChange: (count: number, live: boolean) => void; search: TreeSearchResponse | null; searchFocus: SearchFocus | null; onViewChange?: (center: { lat: number; lng: number }) => void };
export type SearchFocus = { item: ResultItem; tick: number };

type InventoryPoint = [number, number]; // GeoJSON coordinates: longitude, latitude
type WfsFeature = { geometry?: { type?: string; coordinates?: unknown } };
const WFS_URL = 'https://geo.stadt-muenster.de/mapserv/odgruen_serv';
const SNAPSHOT_URL = '/data/muenster-trees-snapshot.json';
const SEARCH_TOP_MARKERS = 25;
/** Room for the legend, the bottom bar and the navigation below the map. */
const SEARCH_BOTTOM_CLEARANCE = 200;
// Blue keeps search matches apart from the green inventory dots, the green quest markers and the gold presentation pin.
const SEARCH_COLOR = '#2f5fd8';
const escapeHtml = (value: string) => value.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);

function parseWfsPoints(data: { features?: WfsFeature[] }): InventoryPoint[] {
  return (data.features ?? []).flatMap((feature): InventoryPoint[] => {
    const coordinates = feature.geometry?.coordinates;
    if (feature.geometry?.type !== 'Point' || !Array.isArray(coordinates) || coordinates.length < 2) return [];
    const [lng, lat] = coordinates;
    return typeof lng === 'number' && typeof lat === 'number' && Number.isFinite(lng) && Number.isFinite(lat) ? [[lng, lat]] : [];
  });
}

function densityImage(points: InventoryPoint[]) {
  if (points.length === 0) return null;
  const bounds = points.reduce((current, [lng, lat]) => ({
    south: Math.min(current.south, lat - .00036),
    west: Math.min(current.west, lng - .00058),
    north: Math.max(current.north, lat + .00036),
    east: Math.max(current.east, lng + .00058),
  }), { south: Infinity, west: Infinity, north: -Infinity, east: -Infinity });
  const canvas = document.createElement('canvas');
  canvas.width = 1400;
  canvas.height = 1100;
  const context = canvas.getContext('2d');
  if (!context) return null;

  // A roughly 30–35 m footprint follows street-tree rows without tinting whole blocks.
  const radiusX = .0005 / (bounds.east - bounds.west) * canvas.width;
  const radiusY = .0003 / (bounds.north - bounds.south) * canvas.height;
  points.forEach(([lng, lat]) => {
    const x = (lng - bounds.west) / (bounds.east - bounds.west) * canvas.width;
    const y = (bounds.north - lat) / (bounds.north - bounds.south) * canvas.height;
    context.save();
    context.translate(x, y);
    context.scale(radiusX, radiusY);
    const gradient = context.createRadialGradient(0, 0, 0, 0, 0, 1);
    gradient.addColorStop(0, 'rgba(62, 174, 92, .17)');
    gradient.addColorStop(.42, 'rgba(103, 192, 116, .09)');
    gradient.addColorStop(1, 'rgba(127, 194, 125, 0)');
    context.fillStyle = gradient;
    context.fillRect(-1, -1, 2, 2);
    context.restore();
  });

  return { url: canvas.toDataURL('image/png'), bounds };
}

const treeGlyph = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m12 3-6 8h3l-4 6h14l-4-6h3l-6-8Z"/><path d="M12 17v4"/></svg>';
// Lucide "camera" and "search" paths: photo quest and "which tree is this" quest.
const cameraGlyph = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z"/><circle cx="12" cy="13" r="3"/></svg>';
const identifyGlyph = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/><path d="M9.5 9a1.6 1.6 0 1 1 2 1.6c-.5.2-.5.6-.5 1.1"/><path d="M11 14h.01"/></svg>';

function questIcon(tree: Tree, selected: boolean) {
  const quest = tree.quest!;
  const classes = ['tree-marker', 'quest-marker', quest.kind, quest.claim ? 'claimed' : '', selected ? 'selected' : ''].filter(Boolean).join(' ');
  return L.divIcon({
    className: classes,
    html: `<span class="marker-face"><span class="marker-tree" aria-hidden="true">${quest.kind === 'photo' ? cameraGlyph : identifyGlyph}</span><span class="marker-xp">${quest.claim ? '✓' : quest.rewardPoints}</span></span>`,
    iconSize: [34, 36],
    iconAnchor: [17, 18],
  });
}

function markerIcon(tree: Tree, selected: boolean) {
  if (tree.quest) return questIcon(tree, selected);
  const status = tree.status;
  return L.divIcon({
    className: `tree-marker ${tree.presentation ? 'presentation' : status} ${selected ? 'selected' : ''}`,
    html: `<span class="marker-face"><span class="marker-tree" aria-hidden="true">${tree.presentation ? '★' : status === 'missing' ? '?' : treeGlyph}</span>${tree.presentation ? '<span class="marker-badge">MS</span>' : status === 'verified' ? '<span class="marker-badge">✓</span>' : ''}</span>`,
    iconSize: [34, 36],
    iconAnchor: [17, 18],
  });
}

function searchIcon(rank: number) {
  return L.divIcon({ className: 'search-marker', html: `<span>${rank}</span>`, iconSize: [28, 28], iconAnchor: [14, 14], popupAnchor: [0, -14] });
}

function searchPopup(item: ResultItem) {
  const t = treeSearchText.de;
  const name = item.genusDe || item.rawGenus || t.unknownGenus;
  const height = item.heightM !== null ? `${item.heightM.toLocaleString('de-DE')} m` : t.noHeight;
  const place = [item.street, item.quarter].filter(Boolean).join(' · ') || t.unknownStreet;
  return `<div class="search-popup"><strong>${item.rank}. ${escapeHtml(name)}</strong>${item.genus ? `<em>${escapeHtml(item.genus)}</em>` : ''}<span>${escapeHtml(height)} · ${escapeHtml(place)}</span></div>`;
}

/** Map area (container pixels) not covered by the search panel above or the map controls below. */
function freeMapArea(map: L.Map) {
  const mapTop = map.getContainer().getBoundingClientRect().top;
  const panelBottom = document.querySelector('.tree-search')?.getBoundingClientRect().bottom ?? mapTop + 230;
  const height = map.getSize().y;
  const top = Math.min(Math.max(60, panelBottom - mapTop + 24), height / 2);
  return { top, bottom: Math.max(top + 80, height - SEARCH_BOTTOM_CLEARANCE) };
}

export default function ExplorerMap({ trees, densityTrees, selectedId, onSelect, userPosition, locateTick, focusTree, focusTick, contributions, selectedDistrictId, onSelectDistrict, onInventoryChange, search, searchFocus, onViewChange }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markersRef = useRef<L.Marker[]>([]);
  const userMarkerRef = useRef<L.CircleMarker | null>(null);
  const districtLayerRef = useRef<L.LayerGroup | null>(null);
  const densityLayerRef = useRef<L.ImageOverlay | null>(null);
  const inventoryDotsRef = useRef<L.LayerGroup | null>(null);
  const inventoryRendererRef = useRef<L.Canvas | null>(null);
  const liveLoadedRef = useRef(false);
  const searchLayerRef = useRef<L.LayerGroup | null>(null);
  const searchRendererRef = useRef<L.Canvas | null>(null);
  const searchMarkersRef = useRef<Map<number, L.Marker>>(new Map());
  const [inventoryPoints, setInventoryPoints] = useState<InventoryPoint[]>([]);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = L.map(containerRef.current, { zoomControl: false, attributionControl: false, scrollWheelZoom: false }).setView([51.9607, 7.6261], 14);
    const attribution = L.control.attribution({ position: 'bottomleft', prefix: false }).addTo(map);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a> contributors',
    }).addTo(map);
    attribution.addAttribution('Baumdaten: <a href="https://opendata.stadt-muenster.de/dataset/digitales-baumkataster-m%C3%BCnster" target="_blank" rel="noopener noreferrer">Stadt Münster</a> · dl-de/by-2.0');
    L.control.zoom({ position: 'bottomright' }).addTo(map);
    const densityPane = map.createPane('tree-density');
    densityPane.style.zIndex = '320';
    densityPane.style.pointerEvents = 'none';
    const dotsPane = map.createPane('tree-dots');
    dotsPane.style.zIndex = '330';
    dotsPane.style.pointerEvents = 'none';
    inventoryRendererRef.current = L.canvas({ pane: 'tree-dots', padding: .1 });
    inventoryDotsRef.current = L.layerGroup().addTo(map);
    // Search matches sit above the inventory dots; their numbered markers sit above the quest markers.
    const searchPane = map.createPane('tree-search');
    searchPane.style.zIndex = '340';
    searchPane.style.pointerEvents = 'none';
    searchRendererRef.current = L.canvas({ pane: 'tree-search', padding: .3 });
    map.createPane('tree-search-markers').style.zIndex = '640';
    map.createPane('district-labels').style.zIndex = '450';
    districtLayerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;
    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(containerRef.current);
    return () => { resize.disconnect(); map.remove(); mapRef.current = null; districtLayerRef.current = null; densityLayerRef.current = null; inventoryDotsRef.current = null; inventoryRendererRef.current = null; searchLayerRef.current = null; searchRendererRef.current = null; };
  }, []);

  useEffect(() => {
    let active = true;
    fetch(SNAPSHOT_URL)
      .then((response) => { if (!response.ok) throw new Error(`Baum-Snapshot HTTP ${response.status}`); return response.json(); })
      .then((snapshot: { points?: InventoryPoint[] }) => {
        if (!active || liveLoadedRef.current || !Array.isArray(snapshot.points)) return;
        setInventoryPoints(snapshot.points);
        onInventoryChange(snapshot.points.length, false);
      })
      .catch(() => { /* The live WFS request below can still populate the map. */ });
    return () => { active = false; };
  }, [onInventoryChange]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    let request: AbortController | null = null;
    let covered: L.LatLngBounds | null = null;
    const loadVisible = () => {
      const visible = map.getBounds();
      if (covered?.contains(visible)) return;
      request?.abort();
      const controller = new AbortController();
      request = controller;
      const requested = visible.pad(.25);
      const url = new URL(WFS_URL);
      url.search = new URLSearchParams({
        SERVICE: 'WFS', VERSION: '2.0.0', REQUEST: 'GetFeature', TYPENAMES: 'ms:Baeume', OUTPUTFORMAT: 'geojson',
        BBOX: `${requested.getSouth()},${requested.getWest()},${requested.getNorth()},${requested.getEast()},urn:ogc:def:crs:EPSG::4326`,
      }).toString();
      fetch(url, { signal: controller.signal })
        .then((response) => { if (!response.ok) throw new Error(`Baumkataster HTTP ${response.status}`); return response.json(); })
        .then((data: { features?: WfsFeature[] }) => {
          const points = parseWfsPoints(data);
          if (controller.signal.aborted || points.length === 0) return;
          covered = requested;
          liveLoadedRef.current = true;
          setInventoryPoints(points);
          onInventoryChange(points.length, true);
        })
        .catch(() => { /* Keep the bundled city snapshot if the WFS is unavailable. */ });
    };
    map.on('moveend', loadVisible);
    loadVisible();
    return () => { map.off('moveend', loadVisible); request?.abort(); };
  }, [onInventoryChange]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    densityLayerRef.current?.remove();
    const points = inventoryPoints.length > 0 ? inventoryPoints : densityTrees.filter((tree) => !tree.presentation).map((tree): InventoryPoint => [tree.lng, tree.lat]);
    const image = densityImage(points);
    densityLayerRef.current = image ? L.imageOverlay(image.url, [[image.bounds.south, image.bounds.west], [image.bounds.north, image.bounds.east]], { pane: 'tree-density', interactive: false, opacity: .85 }).addTo(map) : null;
  }, [densityTrees, inventoryPoints]);

  useEffect(() => {
    const map = mapRef.current;
    const layer = inventoryDotsRef.current;
    const renderer = inventoryRendererRef.current;
    if (!map || !layer || !renderer) return;
    const drawDots = () => {
      layer.clearLayers();
      if (map.getZoom() < 16) return;
      const visible = map.getBounds().pad(.08);
      inventoryPoints.forEach(([lng, lat]) => {
        if (!visible.contains([lat, lng])) return;
        L.circleMarker([lat, lng], { renderer, radius: 3.2, color: '#fffef3', weight: 1, fillColor: '#35a266', fillOpacity: .9, interactive: false }).addTo(layer);
      });
    };
    map.on('moveend', drawDots);
    drawDots();
    return () => { map.off('moveend', drawDots); layer.clearLayers(); };
  }, [inventoryPoints]);

  useEffect(() => {
    const layer = districtLayerRef.current;
    if (!layer) return;
    layer.clearLayers();
    districts.forEach((district) => {
      const standing = districtStandings(district, contributions);
      const color = standing.owner?.color ?? (standing.tied ? '#b78066' : '#8ca99a');
      const selected = selectedDistrictId === district.id;
      L.polygon(district.polygon, { color: selected ? color : '#7d9b86', weight: selected ? 2.5 : 1.2, opacity: selected ? .8 : .42, fillColor: color, fillOpacity: selected ? .12 : .035, dashArray: selected ? undefined : '7 7' })
        .on('click', () => onSelectDistrict(district))
        .addTo(layer);
      L.marker(district.center, {
        pane: 'district-labels', keyboard: true, title: `${district.name}, ${standing.owner ? `führt ${standing.owner.name}` : standing.tied ? 'umkämpft' : 'noch frei'}`,
        icon: L.divIcon({ className: 'district-label-marker', html: `<span class="district-map-label"><span class="district-map-dot" style="background:${color}"></span>${district.shortName}</span>`, iconSize: [100, 26], iconAnchor: [50, 13] }),
      }).on('click', () => onSelectDistrict(district)).addTo(layer);
    });
  }, [contributions, selectedDistrictId, onSelectDistrict]);

  // Reports the map center after every move; the live mode reloads nearby quests from it (debounced by the caller).
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !onViewChange) return;
    const report = () => { const center = map.getCenter(); onViewChange({ lat: center.lat, lng: center.lng }); };
    map.on('moveend', report);
    report();
    return () => { map.off('moveend', report); };
  }, [onViewChange]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    markersRef.current.forEach((marker) => marker.remove());
    markersRef.current = trees.map((tree) => {
      const marker = L.marker([tree.lat, tree.lng], { icon: markerIcon(tree, tree.id === selectedId), title: tree.quest ? `${tree.quest.title}, ${tree.species}` : `${tree.species}, ${tree.area}`, keyboard: true, zIndexOffset: tree.presentation ? 1000 : tree.quest?.claim ? 900 : tree.quest?.kind === 'verify' ? 500 : 0 });
      marker.on('click', () => onSelect(tree));
      marker.addTo(map);
      return marker;
    });
  }, [trees, selectedId, onSelect]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    userMarkerRef.current?.remove();
    userMarkerRef.current = userPosition ? L.circleMarker([userPosition.lat, userPosition.lng], { radius: 9, color: '#ffffff', weight: 3, fillColor: '#4289e8', fillOpacity: 1 }).addTo(map) : null;
  }, [userPosition]);

  useEffect(() => {
    if (userPosition && locateTick > 0) mapRef.current?.flyTo([userPosition.lat, userPosition.lng], 15, { duration: 0.8 });
  }, [userPosition, locateTick]);

  useEffect(() => {
    if (focusTree && focusTick > 0) mapRef.current?.flyTo([focusTree.lat, focusTree.lng], 17, { duration: 0.8 });
  }, [focusTree, focusTick]);

  // Search matches: every position as a canvas dot (thousands stay fast), the top results as numbered markers.
  useEffect(() => {
    const map = mapRef.current;
    const renderer = searchRendererRef.current;
    if (!map || !renderer) return;
    map.closePopup();
    searchLayerRef.current?.remove();
    searchLayerRef.current = null;
    searchMarkersRef.current = new Map();
    if (!search) return;
    const layer = L.layerGroup();
    for (const [lat, lon] of search.positions) {
      L.circleMarker([lat, lon], { renderer, radius: 4, color: '#ffffff', weight: 1.4, fillColor: SEARCH_COLOR, fillOpacity: .92, interactive: false }).addTo(layer);
    }
    const top = search.items.slice(0, SEARCH_TOP_MARKERS);
    for (const item of [...top].reverse()) {
      const marker = L.marker([item.lat, item.lon], { pane: 'tree-search-markers', icon: searchIcon(item.rank), zIndexOffset: 10000 - item.rank, keyboard: true, title: `${item.rank}. ${item.genusDe || item.rawGenus || treeSearchText.de.unknownGenus}` });
      marker.bindPopup(searchPopup(item), { closeButton: false, className: 'search-popup-wrap' });
      marker.addTo(layer);
      searchMarkersRef.current.set(item.rank, marker);
    }
    layer.addTo(map);
    searchLayerRef.current = layer;
    const points: L.LatLngExpression[] = top.length ? top.map((item) => [item.lat, item.lon]) : search.positions;
    const { top: padTop } = freeMapArea(map);
    if (points.length) map.flyToBounds(L.latLngBounds(points), { paddingTopLeft: [40, padTop], paddingBottomRight: [40, SEARCH_BOTTOM_CLEARANCE], maxZoom: 17, duration: 0.8 });
  }, [search]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !searchFocus) return;
    const { item } = searchFocus;
    const marker = searchMarkersRef.current.get(item.rank);
    if (marker) map.once('moveend', () => marker.openPopup());
    // Center the tree in the free area below the search panel, not in the middle of the map.
    const zoom = Math.max(map.getZoom(), 18);
    const area = freeMapArea(map);
    const offsetY = (area.top + area.bottom) / 2 - map.getSize().y / 2;
    const center = map.unproject(map.project([item.lat, item.lon], zoom).subtract([0, offsetY]), zoom);
    map.flyTo(center, zoom, { duration: 0.8 });
  }, [searchFocus]);

  return <div ref={containerRef} className="explorer-map" role="application" aria-label={search ? 'Interaktive Karte mit Stadtbäumen, Baumdichte, Quest-Bäumen und Suchtreffern in Münster' : 'Interaktive Karte mit Stadtbäumen, Baumdichte und spielbaren Quest-Bäumen in Münster'} />;
}
