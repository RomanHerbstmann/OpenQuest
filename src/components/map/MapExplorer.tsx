'use client';

import dynamic from 'next/dynamic';
import { useCallback, useMemo, useState } from 'react';
import { Crosshair, Filter, MapPin, Sparkles, Trees } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { TreeBottomSheet } from '@/components/tree/TreeBottomSheet';
import { usePlayer } from '@/context/PlayerContext';
import { trees } from '@/data/trees';
import { levelProgress } from '@/lib/levels';
import type { Tree } from '@/types/tree';

const ExplorerMap = dynamic(() => import('./ExplorerMap'), { ssr: false, loading: () => <div className="map-loading">Karte wird geladen …</div> });
type FilterValue = 'all' | 'open' | 'verified';
const filters: Array<{ value: FilterValue; label: string }> = [{ value: 'all', label: 'Alle Bäume' }, { value: 'open', label: 'Offene Checks' }, { value: 'verified', label: 'Bestätigt' }];

export function MapExplorer() {
  const { progress, ready } = usePlayer();
  const [selected, setSelected] = useState<Tree | null>(null);
  const [filter, setFilter] = useState<FilterValue>('all');
  const [userPosition, setUserPosition] = useState<{ lat: number; lng: number } | null>(null);
  const [locateTick, setLocateTick] = useState(0);
  const [locationMessage, setLocationMessage] = useState('');
  const visibleTrees = useMemo(() => trees.filter((tree) => filter === 'all' || (filter === 'open' ? tree.status === 'unverified' : tree.status === 'verified')), [filter]);
  const onSelect = useCallback((tree: Tree) => setSelected(tree), []);
  const onClose = useCallback(() => setSelected(null), []);
  const level = levelProgress(ready ? progress.xp : 0);

  const locate = () => {
    if (!navigator.geolocation) { setLocationMessage('Standort ist hier nicht verfügbar. Du kannst die Karte frei erkunden.'); return; }
    setLocationMessage('Standort wird gesucht …');
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => { setUserPosition({ lat: coords.latitude, lng: coords.longitude }); setLocateTick((tick) => tick + 1); setLocationMessage('Dein Standort wird auf der Karte angezeigt.'); },
      () => setLocationMessage('Standort nicht verfügbar. Du kannst die Karte frei erkunden.'),
      { enableHighAccuracy: true, timeout: 10000 },
    );
  };

  return <main className="map-screen">
    <ExplorerMap trees={visibleTrees} selectedId={selected?.id ?? null} onSelect={onSelect} userPosition={userPosition} locateTick={locateTick} />
    <header className="map-header">
      <div className="header-main"><Brand /><span className="demo-tag">DEMO</span></div>
      <div className="header-progress"><div><span>LEVEL {level.level}</span><strong>{ready ? progress.xp : 0} XP</strong></div><div className="progress-track"><span style={{ width: `${level.percent}%` }} /></div></div>
    </header>
    <div className="map-intro"><span className="intro-spark"><Sparkles size={18} /></span><div><strong>Dein nächster Fund wartet.</strong><span>Erkunde Münsters Stadtbäume.</span></div></div>
    <div className="filter-bar" aria-label="Kartenfilter"><Filter size={16} aria-hidden="true" />{filters.map((item) => <button key={item.value} type="button" className={filter === item.value ? 'filter-chip active' : 'filter-chip'} onClick={() => { setFilter(item.value); setSelected(null); }} aria-pressed={filter === item.value}>{item.label}</button>)}</div>
    <div className="map-bottom-area">
      <div className="map-count"><span className="count-icon"><Trees size={20} /></span><div><strong>{visibleTrees.length} Bäume</strong><span>auf deiner Karte</span></div></div>
      <button className="locate-button" type="button" onClick={locate} aria-label="Meinen Standort anzeigen"><Crosshair size={23} /></button>
    </div>
    {locationMessage && <div className="location-message" role="status"><MapPin size={15} />{locationMessage}<button type="button" onClick={() => setLocationMessage('')} aria-label="Hinweis schließen">×</button></div>}
    {selected && <TreeBottomSheet tree={selected} onClose={onClose} userPosition={userPosition} />}
  </main>;
}
