'use client';

import { useEffect, useRef, useState } from 'react';
import L from 'leaflet';
import { districts } from '@/data/districts';
import { districtStandings } from '@/lib/districts';
import type { District, TerritoryContribution } from '@/types/district';
import type { Tree } from '@/types/tree';

type Props = { trees: Tree[]; densityTrees: Tree[]; selectedId: string | null; onSelect: (tree: Tree) => void; userPosition: { lat: number; lng: number } | null; locateTick: number; focusTree: Tree | null; focusTick: number; focusDistrict: District | null; districtFocusTick: number; contributions: TerritoryContribution[]; selectedDistrictId: string | null; onSelectDistrict: (district: District) => void; onInventoryChange: (count: number, live: boolean) => void };

type InventoryPoint = [number, number]; // GeoJSON coordinates: longitude, latitude
type WfsFeature = { geometry?: { type?: string; coordinates?: unknown } };
const WFS_URL = 'https://geo.stadt-muenster.de/mapserv/odgruen_serv';
const SNAPSHOT_URL = '/data/muenster-trees-snapshot.json';

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

function markerIcon(tree: Tree, selected: boolean) {
  const status = tree.status;
  const treeGlyph = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m12 3-6 8h3l-4 6h14l-4-6h3l-6-8Z"/><path d="M12 17v4"/></svg>';
  return L.divIcon({
    className: `tree-marker ${tree.presentation ? 'presentation' : status} ${selected ? 'selected' : ''}`,
    html: `<span class="marker-face"><span class="marker-tree" aria-hidden="true">${tree.presentation ? '★' : status === 'missing' ? '?' : treeGlyph}</span>${tree.presentation ? '<span class="marker-badge">MS</span>' : status === 'verified' ? '<span class="marker-badge">✓</span>' : ''}</span>`,
    iconSize: [34, 36],
    iconAnchor: [17, 18],
  });
}

function escapeHtml(value: string) {
  return value.replace(/[&<>"']/g, (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character] ?? character);
}

const crownIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m2 8 5 4 5-7 5 7 5-4-2 11H4L2 8Z"/><path d="M4 22h16"/></svg>';

function districtCard(district: District, standing: ReturnType<typeof districtStandings>, selected: boolean) {
  const color = standing.owner?.color ?? (standing.tied ? '#c58b52' : '#6a9b83');
  const state = standing.owner ? 'beansprucht' : standing.tied ? 'umkämpft' : 'frei';
  const rows = standing.ranking.slice(0, 3).map((player) => `<span class="district-map-rank ${standing.owner?.id === player.id ? 'is-owner' : ''}"><span class="district-map-position">${standing.owner?.id === player.id ? crownIcon : standing.topCount === 0 ? '–' : player.rank}</span><span class="district-map-player">${escapeHtml(player.name)}</span><b>${player.count}</b></span>`).join('');
  return `<div class="district-map-card district-${district.id} ${selected ? 'is-selected' : ''} ${standing.tied ? 'is-contested' : ''}" style="--district-accent:${color}"><span class="district-map-head"><strong>${escapeHtml(district.shortName)}</strong><small>${state}</small></span><span class="district-map-ranks">${rows}</span></div>`;
}

export default function ExplorerMap({ trees, densityTrees, selectedId, onSelect, userPosition, locateTick, focusTree, focusTick, focusDistrict, districtFocusTick, contributions, selectedDistrictId, onSelectDistrict, onInventoryChange }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markersRef = useRef<L.Marker[]>([]);
  const userMarkerRef = useRef<L.CircleMarker | null>(null);
  const districtLayerRef = useRef<L.LayerGroup | null>(null);
  const densityLayerRef = useRef<L.ImageOverlay | null>(null);
  const inventoryDotsRef = useRef<L.LayerGroup | null>(null);
  const inventoryRendererRef = useRef<L.Canvas | null>(null);
  const liveLoadedRef = useRef(false);
  const [inventoryPoints, setInventoryPoints] = useState<InventoryPoint[]>([]);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = L.map(containerRef.current, { zoomControl: false, attributionControl: false, scrollWheelZoom: false }).setView([51.9607, 7.6261], 14);
    const syncDistrictZoom = () => map.getContainer().classList.toggle('district-compact', map.getZoom() < 15);
    map.on('zoomend', syncDistrictZoom);
    syncDistrictZoom();
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
    map.createPane('district-labels').style.zIndex = '650';
    districtLayerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;
    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(containerRef.current);
    return () => { resize.disconnect(); map.off('zoomend', syncDistrictZoom); map.remove(); mapRef.current = null; districtLayerRef.current = null; densityLayerRef.current = null; inventoryDotsRef.current = null; inventoryRendererRef.current = null; };
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
      const color = standing.owner?.color ?? (standing.tied ? '#c58b52' : '#6a9b83');
      const selected = selectedDistrictId === district.id;
      L.polygon(district.polygon, { className: `district-boundary ${selected ? 'is-selected' : ''}`, color, weight: selected ? 3.7 : 2.5, opacity: selected ? 1 : .9, fillColor: color, fillOpacity: selected ? .23 : .14 })
        .on('click', () => onSelectDistrict(district))
        .addTo(layer);
      L.marker(district.center, {
        pane: 'district-labels', keyboard: true, title: `${district.name}: ${standing.owner ? `${standing.owner.name} führt` : standing.tied ? 'umkämpft' : 'noch frei'}. Top 3: ${standing.ranking.slice(0, 3).map((player) => `${player.name} ${player.count}`).join(', ')}`,
        icon: L.divIcon({ className: 'district-label-marker', html: districtCard(district, standing, selected), iconSize: [150, 112], iconAnchor: [75, 56] }),
      }).on('click', () => onSelectDistrict(district)).addTo(layer);
    });
  }, [contributions, selectedDistrictId, onSelectDistrict]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    markersRef.current.forEach((marker) => marker.remove());
    markersRef.current = trees.map((tree) => {
      const marker = L.marker([tree.lat, tree.lng], { icon: markerIcon(tree, tree.id === selectedId), title: `${tree.sampleAsset ? `Gattung ${tree.inventory?.genus} · Art offen` : tree.species}, ${tree.area}`, keyboard: true, zIndexOffset: tree.presentation ? 1000 : 0 });
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

  useEffect(() => {
    if (focusDistrict && districtFocusTick > 0) mapRef.current?.flyTo(focusDistrict.center, 16, { duration: .85 });
  }, [focusDistrict, districtFocusTick]);

  return <div ref={containerRef} className="explorer-map" role="application" aria-label="Interaktive Karte mit Stadtbäumen, Baumdichte und spielbaren Quest-Bäumen in Münster" />;
}
