'use client';

import dynamic from 'next/dynamic';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { ArrowRight, Crosshair, Filter, Flag, MapPin, Star, Trees } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { DistrictSheet } from '@/components/district/DistrictSheet';
import { ScanModal } from '@/components/scan/ScanModal';
import { TreeBottomSheet } from '@/components/tree/TreeBottomSheet';
import { usePlayer } from '@/context/PlayerContext';
import { districts } from '@/data/districts';
import { presentationTree, trees } from '@/data/trees';
import { getDemoTerritoryContributions } from '@/data/territoryContributions';
import { contributionsFromObservations } from '@/lib/districts';
import { levelProgress } from '@/lib/levels';
import { loadObservations, OBSERVATIONS_STORAGE_KEY } from '@/lib/observations';
import type { District, TerritoryContribution } from '@/types/district';
import type { Tree } from '@/types/tree';

const ExplorerMap = dynamic(() => import('./ExplorerMap'), { ssr: false, loading: () => <div className="map-loading">Karte wird geladen …</div> });
const densityTrees = trees.filter((tree) => !tree.presentation);
type FilterValue = 'all' | 'open' | 'verified';
const filters: Array<{ value: FilterValue; label: string }> = [{ value: 'all', label: 'Alle Bäume' }, { value: 'open', label: 'Offene Checks' }, { value: 'verified', label: 'Bestätigt' }];

export function MapExplorer() {
  const { progress, ready } = usePlayer();
  const [selected, setSelected] = useState<Tree | null>(null);
  const [scanTree, setScanTree] = useState<Tree | null>(null);
  const [districtSheetOpen, setDistrictSheetOpen] = useState(false);
  const [selectedDistrict, setSelectedDistrict] = useState<District | null>(null);
  const [demoContributions] = useState(() => getDemoTerritoryContributions());
  const [localContributions, setLocalContributions] = useState<TerritoryContribution[]>([]);
  const [filter, setFilter] = useState<FilterValue>('all');
  const [userPosition, setUserPosition] = useState<{ lat: number; lng: number } | null>(null);
  const [locateTick, setLocateTick] = useState(0);
  const [presentationTick, setPresentationTick] = useState(0);
  const [locationMessage, setLocationMessage] = useState('');
  const [inventory, setInventory] = useState({ count: 0, live: false });
  const visibleTrees = useMemo(() => trees.filter((tree) => filter === 'all' || (filter === 'open' ? tree.status === 'unverified' : tree.status === 'verified')), [filter]);
  const contributions = useMemo(() => [...demoContributions, ...localContributions], [demoContributions, localContributions]);
  const onSelect = useCallback((tree: Tree) => { setDistrictSheetOpen(false); setSelected(tree); }, []);
  const onClose = useCallback(() => setSelected(null), []);
  const onSelectDistrict = useCallback((district: District) => { setSelected(null); setSelectedDistrict(district); setDistrictSheetOpen(true); }, []);
  const onCloseDistrict = useCallback(() => setDistrictSheetOpen(false), []);
  const onInventoryChange = useCallback((count: number, live: boolean) => setInventory({ count, live }), []);
  const level = levelProgress(ready ? progress.xp : 0);

  useEffect(() => {
    const refresh = () => setLocalContributions(contributionsFromObservations(loadObservations()));
    const onStorage = (event: StorageEvent) => { if (event.key === OBSERVATIONS_STORAGE_KEY) refresh(); };
    refresh();
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  const locate = () => {
    if (!navigator.geolocation) { setLocationMessage('Standort ist hier nicht verfügbar. Du kannst die Karte frei erkunden.'); return; }
    setLocationMessage('Standort wird gesucht …');
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => { setUserPosition({ lat: coords.latitude, lng: coords.longitude }); setLocateTick((tick) => tick + 1); setLocationMessage('Dein Standort wird auf der Karte angezeigt.'); },
      () => setLocationMessage('Standort nicht verfügbar. Du kannst die Karte frei erkunden.'),
      { enableHighAccuracy: true, timeout: 10000 },
    );
  };

  const openPresentationTree = () => {
    setFilter('all');
    setDistrictSheetOpen(false);
    setSelected(presentationTree);
    setPresentationTick((tick) => tick + 1);
  };

  return <main className="map-screen">
    <ExplorerMap trees={visibleTrees} densityTrees={densityTrees} selectedId={selected?.id ?? null} onSelect={onSelect} userPosition={userPosition} locateTick={locateTick} focusTree={presentationTree} focusTick={presentationTick} contributions={contributions} selectedDistrictId={districtSheetOpen ? selectedDistrict?.id ?? null : null} onSelectDistrict={onSelectDistrict} onInventoryChange={onInventoryChange} />
    <header className="map-header">
      <div className="header-main"><Brand /><span className="demo-tag">DEMO</span></div>
      <div className="header-progress"><div><span>LEVEL {level.level}</span><strong>{ready ? progress.xp : 0} XP</strong></div><div className="progress-track"><span style={{ width: `${level.percent}%` }} /></div></div>
    </header>
    <button type="button" className="map-intro map-presentation-shortcut" onClick={openPresentationTree} aria-label="Präsentationsbaum Festtanne am Hafenweg 7 anzeigen"><span className="intro-spark"><Star size={18} /></span><div><small className="intro-eyebrow">SPECIAL DROP · +25 XP</small><strong>Festtanne scannen</strong><span>Präsentationspin · Hafenweg 7</span></div><ArrowRight size={17} /></button>
    <div className="filter-bar" aria-label="Kartenfilter"><Filter size={16} aria-hidden="true" />{filters.map((item) => <button key={item.value} type="button" className={filter === item.value ? 'filter-chip active' : 'filter-chip'} onClick={() => { setFilter(item.value); setSelected(null); }} aria-pressed={filter === item.value}>{item.label}</button>)}</div>
    <div className="map-density-legend" aria-label="Grünere Flächen zeigen mehr erfasste Stadtbäume"><span className="map-density-gradient" aria-hidden="true" /><span>Baumdichte <small>· {inventory.count ? `${inventory.count.toLocaleString('de-DE')} Stadtbäume${inventory.live ? ' live' : ''}` : 'lädt …'}</small></span></div>
    <div className="map-bottom-area">
      <div className="map-count"><span className="count-icon"><Trees size={20} /></span><div><strong>{visibleTrees.length} Quest-Bäume</strong><span>zum Entdecken</span></div></div>
      <button className="district-open-button" type="button" onClick={() => { setSelected(null); setSelectedDistrict(null); setDistrictSheetOpen(true); }}><Flag size={17} /><span>{districts.length} Viertel</span></button>
      <button className="locate-button" type="button" onClick={locate} aria-label="Meinen Standort anzeigen"><Crosshair size={23} /></button>
    </div>
    {locationMessage && <div className="location-message" role="status"><MapPin size={15} />{locationMessage}<button type="button" onClick={() => setLocationMessage('')} aria-label="Hinweis schließen">×</button></div>}
    {selected && <TreeBottomSheet tree={selected} onClose={onClose} onOpenDistrict={onSelectDistrict} onScan={() => { setScanTree(selected); setSelected(null); }} userPosition={userPosition} />}
    {districtSheetOpen && <DistrictSheet district={selectedDistrict} contributions={contributions} onSelect={onSelectDistrict} onBack={() => setSelectedDistrict(null)} onClose={onCloseDistrict} />}
    {scanTree && <ScanModal tree={scanTree} onClose={() => setScanTree(null)} />}
  </main>;
}
