'use client';

import dynamic from 'next/dynamic';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ArrowRight, Crosshair, Filter, Flag, LogIn, MapPin, RefreshCw, Star, Trees } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { DistrictSheet } from '@/components/district/DistrictSheet';
import { ScanModal } from '@/components/scan/ScanModal';
import { TreeSearch } from '@/components/search/TreeSearch';
import { TreeBottomSheet, type QuestActions } from '@/components/tree/TreeBottomSheet';
import { usePlayer } from '@/context/PlayerContext';
import { useSession } from '@/context/SessionContext';
import { districts } from '@/data/districts';
import { presentationTree, trees } from '@/data/trees';
import { getDemoTerritoryContributions } from '@/data/territoryContributions';
import { liveText } from '@/i18n/liveGame';
import { api, ApiError } from '@/lib/api';
import { contributionsFromObservations } from '@/lib/districts';
import { levelProgress } from '@/lib/levels';
import { currentPosition, locationSupported } from '@/lib/position';
import { loadObservations, OBSERVATIONS_STORAGE_KEY } from '@/lib/observations';
import { claimPoints, mergeQuestTrees } from '@/lib/quests';
import { useNearbyQuests } from '@/lib/useNearbyQuests';
import type { District, TerritoryContribution } from '@/types/district';
import type { Tree } from '@/types/tree';
import type { ResultItem, TreeSearchResponse } from '@/types/treeSearch';
import type { SearchFocus } from './ExplorerMap';

const ExplorerMap = dynamic(() => import('./ExplorerMap'), { ssr: false, loading: () => <div className="map-loading">Karte wird geladen …</div> });
const densityTrees = trees.filter((tree) => !tree.presentation);
const lt = liveText.de;
type FilterValue = 'all' | 'open' | 'verified';
type QuestFilter = 'all' | 'photo' | 'verify';
const filters: Array<{ value: FilterValue; label: string }> = [{ value: 'all', label: 'Alle Bäume' }, { value: 'open', label: 'Offene Checks' }, { value: 'verified', label: 'Bestätigt' }];
const questFilters: Array<{ value: QuestFilter; label: string }> = [{ value: 'all', label: lt.map.filterAll }, { value: 'photo', label: lt.map.filterPhoto }, { value: 'verify', label: lt.map.filterVerify }];

export function MapExplorer() {
  const { progress, ready } = usePlayer();
  const { session, openAuth, claims, progress: liveProgress, refreshClaims } = useSession();
  const live = session !== null;
  const [selected, setSelected] = useState<Tree | null>(null);
  const [scanTree, setScanTree] = useState<Tree | null>(null);
  const [districtSheetOpen, setDistrictSheetOpen] = useState(false);
  const [selectedDistrict, setSelectedDistrict] = useState<District | null>(null);
  const [demoContributions] = useState(() => getDemoTerritoryContributions());
  const [localContributions, setLocalContributions] = useState<TerritoryContribution[]>([]);
  const [filter, setFilter] = useState<FilterValue>('all');
  const [questFilter, setQuestFilter] = useState<QuestFilter>('all');
  const [userPosition, setUserPosition] = useState<{ lat: number; lng: number } | null>(null);
  const [viewCenter, setViewCenter] = useState<{ lat: number; lng: number } | null>(null);
  const [locateTick, setLocateTick] = useState(0);
  const [presentationTick, setPresentationTick] = useState(0);
  const [focusedTree, setFocusedTree] = useState<Tree | null>(null);
  const [focusedDistrict, setFocusedDistrict] = useState<District | null>(null);
  const [districtFocusTick, setDistrictFocusTick] = useState(0);
  const [locationMessage, setLocationMessage] = useState('');
  const [inventory, setInventory] = useState({ count: 0, live: false });
  const [search, setSearch] = useState<TreeSearchResponse | null>(null);
  const [searchFocus, setSearchFocus] = useState<SearchFocus | null>(null);
  const [questBusy, setQuestBusy] = useState<QuestActions['busy']>(null);
  const [questError, setQuestError] = useState('');
  const autoLocated = useRef(false);

  // Live mode: real quests around the map center (the map flies to the player once the position is known).
  const nearby = useNearbyQuests(live, viewCenter ?? userPosition);
  const questTrees = useMemo(() => (live ? mergeQuestTrees(nearby.quests, claims ?? []) : []), [live, nearby.quests, claims]);
  const visibleQuestTrees = useMemo(() => questTrees.filter((tree) => questFilter === 'all' || tree.quest?.kind === questFilter), [questTrees, questFilter]);
  const visibleDemoTrees = useMemo(() => trees.filter((tree) => filter === 'all' || (filter === 'open' ? tree.status === 'unverified' : tree.status === 'verified')), [filter]);
  const visibleTrees = live ? [...visibleQuestTrees, presentationTree] : visibleDemoTrees;
  const points = claims ? claimPoints(claims) : { confirmed: 0, pending: 0 };

  const contributions = useMemo(() => [...demoContributions, ...localContributions], [demoContributions, localContributions]);
  const onSelect = useCallback((tree: Tree) => { setDistrictSheetOpen(false); setQuestError(''); setSelected(tree); }, []);
  const onClose = useCallback(() => setSelected(null), []);
  const onSelectDistrict = useCallback((district: District) => { setSelected(null); setSelectedDistrict(district); setDistrictSheetOpen(true); }, []);
  const onCloseDistrict = useCallback(() => setDistrictSheetOpen(false), []);
  const onFocusDistrict = useCallback((district: District) => { setFocusedDistrict(district); setDistrictFocusTick((tick) => tick + 1); setDistrictSheetOpen(false); }, []);
  const onInventoryChange = useCallback((count: number, live: boolean) => setInventory({ count, live }), []);
  const onSearchResult = useCallback((result: TreeSearchResponse | null) => { setSearch(result); setSearchFocus(null); setSelected(null); setDistrictSheetOpen(false); }, []);
  const onFocusItem = useCallback((item: ResultItem) => setSearchFocus((prev) => ({ item, tick: (prev?.tick ?? 0) + 1 })), []);
  const onViewChange = useCallback((center: { lat: number; lng: number }) => setViewCenter(center), []);
  const level = levelProgress(ready ? progress.xp : 0);

  useEffect(() => {
    const refresh = () => setLocalContributions(contributionsFromObservations(loadObservations()));
    const onStorage = (event: StorageEvent) => { if (event.key === OBSERVATIONS_STORAGE_KEY) refresh(); };
    refresh();
    window.addEventListener('storage', onStorage);
    return () => window.removeEventListener('storage', onStorage);
  }, []);

  // The selected quest follows claim changes (claimed, cancelled) without closing the sheet.
  useEffect(() => {
    if (!selected?.quest) return;
    const next = questTrees.find((tree) => tree.quest?.id === selected.quest?.id);
    if (next && next.quest?.claim?.id !== selected.quest.claim?.id) setSelected(next);
  }, [questTrees, selected]);

  // Leaving live mode closes live quest dialogs.
  useEffect(() => {
    if (live) return;
    setSelected((current) => (current?.quest ? null : current));
    setScanTree((current) => (current?.quest ? null : current));
    autoLocated.current = false;
  }, [live]);

  const locate = useCallback((silent = false) => {
    if (!locationSupported()) { if (!silent) setLocationMessage('Standort ist hier nicht verfügbar. Du kannst die Karte frei erkunden.'); return; }
    if (!silent) setLocationMessage('Standort wird gesucht …');
    currentPosition()
      .then((position) => { setUserPosition(position); setLocateTick((tick) => tick + 1); if (!silent) setLocationMessage('Dein Standort wird auf der Karte angezeigt.'); })
      .catch(() => { if (!silent) setLocationMessage('Standort nicht verfügbar. Du kannst die Karte frei erkunden.'); });
  }, []);

  // Quests are about the player's surroundings: locate once when live mode starts.
  useEffect(() => {
    if (live && !autoLocated.current) { autoLocated.current = true; locate(true); }
  }, [live, locate]);

  const openPresentationTree = () => {
    setFilter('all');
    setDistrictSheetOpen(false);
    setSelected(presentationTree);
    setFocusedTree(presentationTree);
    setPresentationTick((tick) => tick + 1);
  };

  // Claimed and submitted quests drop out of /quests/nearby, so reload both lists.
  const reloadNearby = nearby.reload;
  const afterQuestChange = useCallback(async () => {
    reloadNearby();
    await refreshClaims();
  }, [reloadNearby, refreshClaims]);

  const claimSelected = async () => {
    const quest = selected?.quest;
    if (!quest) return;
    if (!live) { openAuth('login'); return; }
    setQuestBusy('claim');
    setQuestError('');
    try {
      const claim = await api.claimQuest(quest.id);
      const claimed: Tree = { ...selected, quest: { ...quest, claim: { id: claim.id, expiresAt: claim.expiresAt } } };
      setSelected(null);
      setScanTree(claimed);
      void afterQuestChange();
    } catch (error) {
      setQuestError(error instanceof ApiError ? error.message : lt.errors.fallback);
      if (error instanceof ApiError && error.code === 'already_claimed') void afterQuestChange();
    } finally {
      setQuestBusy(null);
    }
  };

  const cancelClaim = useCallback(async (tree: Tree) => {
    const claim = tree.quest?.claim;
    if (!claim) return;
    await api.cancelClaim(claim.id);
    await afterQuestChange();
  }, [afterQuestChange]);

  const cancelSelected = async () => {
    if (!selected) return;
    setQuestBusy('cancel');
    setQuestError('');
    try { await cancelClaim(selected); setSelected(null); } catch (error) { setQuestError(error instanceof ApiError ? error.message : lt.errors.fallback); } finally { setQuestBusy(null); }
  };

  const questActions: QuestActions = { busy: questBusy, error: questError, loggedIn: live, onClaim: () => void claimSelected(), onCancel: () => void cancelSelected() };
  const countTitle = live ? (nearby.loading && questTrees.length === 0 ? lt.map.loading : lt.map.nearby(visibleQuestTrees.length)) : `${visibleTrees.length} Quest-Bäume`;
  const countSub = live ? (nearby.error || (!nearby.loading && questTrees.length === 0 ? lt.map.empty : lt.map.nearbySub)) : 'zum Entdecken';

  const openSampleTree = () => {
    const sample = trees.find((tree) => tree.sampleAsset && tree.inventory?.genus === 'Fagus') ?? trees.find((tree) => tree.sampleAsset);
    if (!sample) return;
    setFilter('all');
    setDistrictSheetOpen(false);
    setSelected(sample);
    setFocusedTree(sample);
    setPresentationTick((tick) => tick + 1);
  };

  return <main className={`map-screen ${live ? 'live-mode' : 'demo-mode'} ${selected || districtSheetOpen ? 'has-sheet' : ''}`}>
    <ExplorerMap trees={visibleTrees} densityTrees={densityTrees} selectedId={selected?.id ?? null} onSelect={onSelect} userPosition={userPosition} locateTick={locateTick} focusTree={focusedTree} focusTick={presentationTick} focusDistrict={focusedDistrict} districtFocusTick={districtFocusTick} contributions={contributions} selectedDistrictId={districtSheetOpen ? selectedDistrict?.id ?? null : null} onSelectDistrict={onSelectDistrict} onInventoryChange={onInventoryChange} search={search} searchFocus={searchFocus} onViewChange={onViewChange} />
    <header className="map-header">
      <div className="header-main"><Brand />{live
        ? <span className="demo-tag live-tag" title={lt.auth.signedInAs(session.username)}>{lt.mode.live}</span>
        : <button type="button" className="demo-tag demo-login" onClick={() => openAuth('login')} title={lt.mode.demoHint} aria-label={`${lt.mode.demo}. ${lt.mode.demoHint}`}>{lt.mode.demo} <LogIn size={11} aria-hidden="true" /></button>}</div>
      {live
        ? <div className="header-progress" title={lt.claims.points(liveProgress?.totalPoints ?? points.confirmed, points.pending)}><div><span>{liveProgress ? `LEVEL ${liveProgress.level.level}` : lt.mode.points}</span><strong>{liveProgress?.totalPoints ?? points.confirmed} XP</strong></div><div className="progress-track"><span style={{ width: `${liveProgress ? liveProgress.level.percent : points.confirmed + points.pending > 0 ? Math.round((points.confirmed / (points.confirmed + points.pending)) * 100) : 0}%` }} /></div></div>
        : <div className="header-progress"><div><span>LEVEL {level.level}</span><strong>{ready ? progress.xp : 0} XP</strong></div><div className="progress-track"><span style={{ width: `${level.percent}%` }} /></div></div>}
    </header>
    <TreeSearch result={search} onResult={onSearchResult} onFocusItem={onFocusItem} />
    <button type="button" className="map-intro map-presentation-shortcut" onClick={openPresentationTree} aria-label="Präsentationsbaum Festtanne am Hafenweg 7 anzeigen"><span className="intro-spark"><Star size={18} /></span><div><small className="intro-eyebrow">SPECIAL DROP · +25 XP</small><strong>Festtanne scannen</strong><span>Präsentationspin · Hafenweg 7</span></div><ArrowRight size={17} /></button>
    <div className="filter-bar" aria-label="Kartenfilter"><Filter size={16} aria-hidden="true" />{live
      ? questFilters.map((item) => <button key={item.value} type="button" className={questFilter === item.value ? 'filter-chip active' : 'filter-chip'} onClick={() => { setQuestFilter(item.value); setSelected(null); }} aria-pressed={questFilter === item.value}>{item.label}</button>)
      : filters.map((item) => <button key={item.value} type="button" className={filter === item.value ? 'filter-chip active' : 'filter-chip'} onClick={() => { setFilter(item.value); setSelected(null); }} aria-pressed={filter === item.value}>{item.label}</button>)}</div>
    <div className="map-density-legend" aria-label="Grünere Flächen zeigen mehr erfasste Stadtbäume"><span className="map-density-gradient" aria-hidden="true" /><span>Baumdichte <small>· {inventory.count ? `${inventory.count.toLocaleString('de-DE')} Stadtbäume${inventory.live ? ' live' : ''}` : 'lädt …'}</small></span></div>
    <div className="map-bottom-area">
      {live
        ? <div className={`map-count ${nearby.error ? 'has-error' : ''}`}><span className="count-icon"><Trees size={20} /></span><div><strong>{countTitle}</strong><span>{countSub}</span></div>{nearby.error && <button type="button" className="map-count-retry" onClick={nearby.reload} aria-label={lt.map.retry}><RefreshCw size={16} /></button>}</div>
        : <button type="button" className="map-count" onClick={openSampleTree} aria-label="Kataster-Beispielbaum mit Gattung und Höhe anzeigen"><span className="count-icon"><Trees size={20} /></span><div><strong>{visibleTrees.length} Baumpunkte</strong><span>10 mit Zusatzdaten · ansehen</span></div></button>}
      <button className="district-open-button" type="button" onClick={() => { setSelected(null); setSelectedDistrict(null); setDistrictSheetOpen(true); }}><Flag size={17} /><span>{districts.length} Viertel</span></button>
      <button className="locate-button" type="button" onClick={() => locate()} aria-label="Meinen Standort anzeigen"><Crosshair size={23} /></button>
    </div>
    {locationMessage && <div className="location-message" role="status"><MapPin size={15} />{locationMessage}<button type="button" onClick={() => setLocationMessage('')} aria-label="Hinweis schließen">×</button></div>}
    {selected && <TreeBottomSheet tree={selected} onClose={onClose} onOpenDistrict={onSelectDistrict} onScan={() => { setScanTree(selected); setSelected(null); }} userPosition={userPosition} questActions={selected.quest ? questActions : undefined} />}
    {districtSheetOpen && <DistrictSheet district={selectedDistrict} contributions={contributions} onSelect={onSelectDistrict} onFocus={onFocusDistrict} onBack={() => setSelectedDistrict(null)} onClose={onCloseDistrict} />}
    {scanTree && <ScanModal tree={scanTree} onClose={() => setScanTree(null)} onSubmitted={scanTree.quest ? () => void afterQuestChange() : undefined} onCancelClaim={scanTree.quest?.claim ? () => cancelClaim(scanTree) : undefined} />}
  </main>;
}
