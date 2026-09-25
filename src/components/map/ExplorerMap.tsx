'use client';

import { useEffect, useRef } from 'react';
import L from 'leaflet';
import type { Tree } from '@/types/tree';

type Props = { trees: Tree[]; selectedId: string | null; onSelect: (tree: Tree) => void; userPosition: { lat: number; lng: number } | null; locateTick: number };

function markerIcon(tree: Tree, selected: boolean) {
  const status = tree.status;
  return L.divIcon({
    className: `tree-marker ${status} ${selected ? 'selected' : ''}`,
    html: `<span class="marker-face"><span class="marker-tree" aria-hidden="true">${status === 'missing' ? '?' : '✦'}</span>${status === 'verified' ? '<span class="marker-badge">✓</span>' : ''}</span>`,
    iconSize: [46, 53],
    iconAnchor: [23, 46],
  });
}

export default function ExplorerMap({ trees, selectedId, onSelect, userPosition, locateTick }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const markersRef = useRef<L.Marker[]>([]);
  const userMarkerRef = useRef<L.CircleMarker | null>(null);

  useEffect(() => {
    if (!containerRef.current || mapRef.current) return;
    const map = L.map(containerRef.current, { zoomControl: false, scrollWheelZoom: false }).setView([51.9607, 7.6261], 14);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a> contributors',
    }).addTo(map);
    L.control.zoom({ position: 'bottomright' }).addTo(map);
    mapRef.current = map;
    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(containerRef.current);
    return () => { resize.disconnect(); map.remove(); mapRef.current = null; };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    markersRef.current.forEach((marker) => marker.remove());
    markersRef.current = trees.map((tree) => {
      const marker = L.marker([tree.lat, tree.lng], { icon: markerIcon(tree, tree.id === selectedId), title: `${tree.species}, ${tree.area}`, keyboard: true });
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

  return <div ref={containerRef} className="explorer-map" role="application" aria-label="Interaktive Karte mit Demo-Bäumen in Münster" />;
}
