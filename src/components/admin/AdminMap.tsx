'use client';

import { useEffect, useRef } from 'react';
import L from 'leaflet';
import { trees } from '@/data/trees';
import type { Observation } from '@/types/observation';

const colors = { pending: '#d49b38', needs_review: '#b77938', verified: '#2c8054', rejected: '#88938e' };

export default function AdminMap({ observations, selected, onSelect }: { observations: Observation[]; selected: Observation; onSelect: (id: string) => void }) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const layerRef = useRef<L.LayerGroup | null>(null);

  useEffect(() => {
    if (!container.current || mapRef.current) return;
    const map = L.map(container.current, { zoomControl: false, scrollWheelZoom: false }).setView([selected.lat, selected.lng], 15);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a> contributors',
    }).addTo(map);
    L.control.zoom({ position: 'bottomright' }).addTo(map);
    layerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;
    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(container.current);
    return () => { resize.disconnect(); map.remove(); mapRef.current = null; layerRef.current = null; };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    const layer = layerRef.current;
    if (!map || !layer) return;
    layer.clearLayers();
    observations.forEach((observation) => {
      const active = observation.id === selected.id;
      const marker = L.circleMarker([observation.lat, observation.lng], {
        radius: active ? 12 : 6,
        color: '#fff', weight: active ? 3 : 2,
        fillColor: colors[observation.reviewStatus], fillOpacity: active ? 1 : .85,
      }).addTo(layer);
      marker.bindTooltip(observation.id, { direction: 'top' });
      marker.on('click', () => onSelect(observation.id));
    });
    const reference = trees.find((tree) => tree.id === selected.treeId);
    if (reference && (reference.lat !== selected.lat || reference.lng !== selected.lng)) {
      L.polyline([[reference.lat, reference.lng], [selected.lat, selected.lng]], { color: '#276b4b', weight: 2, dashArray: '5, 5' }).addTo(layer);
      L.circleMarker([reference.lat, reference.lng], { radius: 8, color: '#fff', weight: 2, fillColor: '#174932', fillOpacity: 1 }).bindTooltip('Referenzstandort').addTo(layer);
    }
    map.flyTo([selected.lat, selected.lng], Math.max(map.getZoom(), 15), { duration: .45 });
  }, [observations, selected, onSelect]);

  return <div ref={container} className="admin-map" role="application" aria-label="Karte der Demo-Beobachtungen in Münster" />;
}
