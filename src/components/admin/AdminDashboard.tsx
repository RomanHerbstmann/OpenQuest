'use client';

import dynamic from 'next/dynamic';
import Link from 'next/link';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { ArrowLeft, ArrowRight, Check, CheckCircle2, ClipboardCheck, Clock3, Download, FileJson2, Filter, Info, Leaf, MapPin, Search, ShieldAlert, SlidersHorizontal, Trees, X } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { demoObservations } from '@/data/observations';
import { trees } from '@/data/trees';
import { loadObservations, observationsToCsv, observationsToGeoJson, saveObservations } from '@/lib/observations';
import type { Observation, ObservationAction, ReviewStatus } from '@/types/observation';

const AdminMap = dynamic(() => import('./AdminMap'), { ssr: false, loading: () => <div className="admin-map map-loading">Karte wird geladen …</div> });

const statuses: Record<ReviewStatus, { label: string; className: string }> = {
  pending: { label: 'Offen', className: 'pending' },
  needs_review: { label: 'Rückfrage', className: 'needs-review' },
  verified: { label: 'Freigegeben', className: 'verified' },
  rejected: { label: 'Abgelehnt', className: 'rejected' },
};

const actions: Record<ObservationAction, string> = {
  exists: 'Baum vorhanden',
  missing: 'Baum fehlt',
  species_suggestion: 'Artvorschlag',
  new_tree: 'Neuer Baum',
};

function dateTime(value: string) {
  return new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Berlin' }).format(new Date(value));
}

function download(filename: string, content: string, mimeType: string) {
  const url = URL.createObjectURL(new Blob([content], { type: mimeType }));
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

const filterOptions: Array<{ value: ReviewStatus | 'all'; label: string }> = [
  { value: 'all', label: 'Alle' },
  { value: 'pending', label: 'Offen' },
  { value: 'needs_review', label: 'Rückfrage' },
  { value: 'verified', label: 'Freigegeben' },
  { value: 'rejected', label: 'Abgelehnt' },
];

export function AdminDashboard() {
  const [observations, setObservations] = useState<Observation[]>(demoObservations);
  const [ready, setReady] = useState(false);
  const [selectedId, setSelectedId] = useState(demoObservations[0].id);
  const [filter, setFilter] = useState<ReviewStatus | 'all'>('all');
  const [query, setQuery] = useState('');
  const [saveError, setSaveError] = useState('');
  const [exportMessage, setExportMessage] = useState('');

  useEffect(() => { setObservations(loadObservations()); setReady(true); }, []);
  useEffect(() => {
    if (!ready) return;
    try { saveObservations(observations); setSaveError(''); }
    catch { setSaveError('Lokales Speichern ist in diesem Browser nicht verfügbar.'); }
  }, [observations, ready]);

  const visible = useMemo(() => observations.filter((item) => {
    if (filter !== 'all' && item.reviewStatus !== filter) return false;
    const tree = trees.find((entry) => entry.id === item.treeId);
    const haystack = `${item.id} ${item.treeId ?? ''} ${tree?.species ?? ''} ${tree?.area ?? ''} ${item.suggestedSpecies ?? ''} ${actions[item.action]}`.toLocaleLowerCase('de-DE');
    return haystack.includes(query.toLocaleLowerCase('de-DE').trim());
  }).sort((a, b) => b.observedAt.localeCompare(a.observedAt)), [observations, filter, query]);
  const selected = visible.find((item) => item.id === selectedId) ?? visible[0] ?? null;
  const selectedTree = trees.find((tree) => tree.id === selected?.treeId);
  const select = useCallback((id: string) => setSelectedId(id), []);

  const updateSelected = (changes: Partial<Observation>) => {
    if (!selected) return;
    setObservations((current) => current.map((item) => item.id === selected.id ? { ...item, ...changes } : item));
  };

  const counts = {
    pending: observations.filter((item) => item.reviewStatus === 'pending').length,
    needs_review: observations.filter((item) => item.reviewStatus === 'needs_review').length,
    verified: observations.filter((item) => item.reviewStatus === 'verified').length,
    rejected: observations.filter((item) => item.reviewStatus === 'rejected').length,
  };

  const exportData = (format: 'csv' | 'geojson') => {
    const stamp = new Date().toISOString().slice(0, 10);
    if (format === 'csv') download(`openquest-demo-beobachtungen-${stamp}.csv`, observationsToCsv(observations), 'text/csv;charset=utf-8');
    else download(`openquest-demo-beobachtungen-${stamp}.geojson`, observationsToGeoJson(observations), 'application/geo+json;charset=utf-8');
    setExportMessage(`${format === 'csv' ? 'CSV' : 'GeoJSON'} mit ${observations.length} Demo-Meldungen erstellt.`);
  };

  return <main className="admin-shell">
    <aside className="admin-sidebar">
      <Brand />
      <div className="admin-workspace-label">PROJEKT-WORKSPACE</div>
      <div className="admin-side-link active"><ClipboardCheck size={18} /> Prüfbereich <span>{observations.length}</span></div>
      <Link className="admin-side-link" href="/"><Trees size={18} /> Zur Entdeckerkarte <ArrowRight size={16} /></Link>
      <div className="admin-sidebar-bottom"><div className="admin-demo-icon"><Leaf size={19} /></div><div><strong>Demo-Arbeitsbereich</strong><p>Nur lokale Testdaten, keine amtliche Freigabe.</p></div></div>
    </aside>

    <div className="admin-main">
      <header className="admin-topbar"><div><Link href="/" className="admin-back"><ArrowLeft size={16} /> Zur App</Link><span className="admin-breadcrumb">/</span><span>Prüfbereich</span></div><span className="admin-demo-badge"><span /> DEMO-MODUS</span></header>
      <div className="admin-content">
        <div className="admin-heading-row"><div><p className="eyebrow">OPENQUEST · DATENQUALITÄT</p><h1>Beobachtungen prüfen<span>.</span></h1><p>Beiträge sichten, Entscheidungen dokumentieren und Änderungsvorschläge exportieren.</p></div><div className="admin-export"><button type="button" onClick={() => exportData('csv')}><Download size={16} /> CSV</button><button type="button" onClick={() => exportData('geojson')}><FileJson2 size={16} /> GeoJSON</button></div></div>
        {exportMessage && <p className="admin-export-feedback" role="status">{exportMessage}</p>}
        <div className="admin-notice"><Info size={18} /><span>Diese Meldungen sind erfundene Demo-Daten. Eine Freigabe ändert nur den lokalen Projekt-Prüfstatus, keinen städtischen Datensatz.</span></div>
        {saveError && <div className="admin-save-error" role="alert">{saveError}</div>}
        <section className="admin-stats" aria-label="Übersicht">
          <div className="admin-stat"><span>GESAMT</span><strong>{observations.length}</strong><small>Beobachtungen</small><Trees size={21} /></div>
          <div className="admin-stat"><span>OFFEN</span><strong>{counts.pending}</strong><small>Warten auf Prüfung</small><Clock3 size={21} /></div>
          <div className="admin-stat"><span>RÜCKFRAGE</span><strong>{counts.needs_review}</strong><small>Genauer ansehen</small><ShieldAlert size={21} /></div>
          <div className="admin-stat"><span>FREIGEGEBEN</span><strong>{counts.verified}</strong><small>Im Projekt geprüft</small><CheckCircle2 size={21} /></div>
        </section>
        <div className="admin-panel-heading"><div><h2>Prüfwarteschlange</h2><p>{visible.length} von {observations.length} Meldungen angezeigt</p></div><SlidersHorizontal size={19} /></div>
        <div className="admin-workspace">
          <section className="admin-queue" aria-label="Beobachtungsliste">
            <label className="admin-search"><Search size={18} /><input type="search" placeholder="Baumart, Ort oder ID suchen" value={query} onChange={(event) => setQuery(event.target.value)} /><span className="sr-only">Beobachtungen durchsuchen</span></label>
            <div className="admin-filters" aria-label="Statusfilter"><Filter size={15} />{filterOptions.map((option) => <button type="button" key={option.value} className={filter === option.value ? 'active' : ''} aria-pressed={filter === option.value} onClick={() => setFilter(option.value)}>{option.label}</button>)}</div>
            <div className="admin-queue-items">{visible.length ? visible.map((item) => {
              const tree = trees.find((entry) => entry.id === item.treeId);
              return <button type="button" key={item.id} className={`admin-queue-item ${selected?.id === item.id ? 'selected' : ''}`} onClick={() => setSelectedId(item.id)} aria-current={selected?.id === item.id ? 'true' : undefined}>
                <span className={`admin-item-icon ${item.reviewStatus}`}><Trees size={19} /></span>
                <span className="admin-item-content"><span className="admin-item-title">{tree?.species ?? item.suggestedSpecies ?? 'Neuer Baum'}</span><span className="admin-item-sub"><MapPin size={12} /> {tree?.area ?? 'Neuer Standort'} · {actions[item.action]}</span><span className="admin-item-meta">{item.id} · {dateTime(item.observedAt)}</span></span>
                <span className={`admin-status ${statuses[item.reviewStatus].className}`}>{statuses[item.reviewStatus].label}</span>
              </button>;
            }) : <div className="admin-empty"><Search size={25} /><strong>Keine Meldungen gefunden</strong><span>Suche oder Filter anpassen.</span><button type="button" onClick={() => { setQuery(''); setFilter('all'); }}>Filter zurücksetzen</button></div>}</div>
          </section>

          <section className="admin-detail" aria-label="Details und Prüfung">
            {selected ? <>
              <div className="admin-detail-head"><div><span className="admin-record-id">BEOBACHTUNG {selected.id}</span><h2>{selectedTree?.species ?? selected.suggestedSpecies ?? 'Neuer Baum'}</h2><p><MapPin size={14} /> {selectedTree?.area ?? 'Neuer Standort in Münster'} · {dateTime(selected.observedAt)}</p></div><span className={`admin-status ${statuses[selected.reviewStatus].className}`}>{statuses[selected.reviewStatus].label}</span></div>
              <div className="admin-map-wrap"><AdminMap observations={visible} selected={selected} onSelect={select} /><span className="admin-map-caption"><span /> Beobachtungspunkt</span></div>
              <div className="admin-change"><div><span>REFERENZ</span><strong>{selectedTree?.species ?? 'Kein Eintrag'}</strong></div><ArrowRight size={17} /><div><span>BEOBACHTUNG / VORSCHLAG</span><strong>{selected.action === 'species_suggestion' || selected.action === 'new_tree' ? selected.suggestedSpecies ?? 'Art unbekannt' : actions[selected.action]}</strong></div></div>
              <div className="admin-detail-grid"><div><span>REFERENZEINTRAG</span><strong>{selectedTree ? `${selectedTree.species} · ${selectedTree.id}` : 'Kein Referenzbaum'}</strong></div><div><span>BEOBACHTUNG</span><strong>{actions[selected.action]}</strong></div><div><span>ARTVORSCHLAG</span><strong>{selected.suggestedSpecies ?? 'Keiner'}</strong></div><div><span>GPS-GENAUIGKEIT</span><strong>{selected.accuracyMeters == null ? 'Nicht angegeben' : `± ${selected.accuracyMeters} m`}</strong></div><div><span>KOORDINATEN</span><strong>{selected.lat.toFixed(5)}, {selected.lng.toFixed(5)}</strong></div><div><span>BELEG</span><strong>Kein Foto hinterlegt</strong></div></div>
              {(selected.accuracyMeters != null && selected.accuracyMeters > 20 || selected.action === 'missing' || selected.action === 'new_tree') && <div className="admin-review-hint"><ShieldAlert size={17} /><span>{selected.action === 'missing' ? 'Eine Fehlmeldung ist nur ein Prüfhinweis. Vor Änderungen weitere Belege einholen.' : selected.action === 'new_tree' ? 'Neuer Baumkandidat: Standort und mögliche Dublette prüfen.' : 'Die GPS-Genauigkeit ist eingeschränkt. Standort vor einer Freigabe prüfen.'}</span></div>}
              <label className="admin-note-label" htmlFor="review-note">Prüfnotiz</label><textarea id="review-note" value={selected.reviewNote} onChange={(event) => updateSelected({ reviewNote: event.target.value })} placeholder="Begründung oder offene Frage festhalten …" rows={2} />
              <div className="admin-review-actions"><button type="button" className="admin-review-button secondary" onClick={() => updateSelected({ reviewStatus: 'needs_review' })}><ShieldAlert size={16} /> Rückfrage</button><button type="button" className="admin-review-button reject" onClick={() => updateSelected({ reviewStatus: 'rejected' })}><X size={16} /> Ablehnen</button><button type="button" className="admin-review-button approve" onClick={() => updateSelected({ reviewStatus: 'verified' })}><Check size={17} /> Freigeben</button></div>
              {selected.reviewStatus !== 'pending' && <button type="button" className="admin-reset" onClick={() => updateSelected({ reviewStatus: 'pending' })}>Auf „Offen“ zurücksetzen</button>}
              <p className="admin-detail-footnote">Projekt-Prüfschritt · lokal im Browser gespeichert · keine amtliche Datenänderung</p>
            </> : <div className="admin-empty detail-empty"><Search size={28} /><strong>Keine Beobachtung ausgewählt</strong><span>Wähle links eine Meldung aus.</span></div>}
          </section>
        </div>
      </div>
    </div>
  </main>;
}
