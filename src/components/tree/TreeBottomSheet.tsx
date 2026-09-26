'use client';

import Link from 'next/link';
import { useEffect, useRef } from 'react';
import { ArrowRight, BookOpenText, Camera, Check, Clock3, Flag, Leaf, Loader2, MapPin, Search, ShieldCheck, Sparkles, Star, TreeDeciduous, TreePine, UsersRound, X } from 'lucide-react';
import { lexiconForSpecies } from '@/data/lexicon';
import { liveText } from '@/i18n/liveGame';
import { distanceMeters, formatDistance } from '@/lib/distance';
import { districtForTree } from '@/lib/districts';
import type { District } from '@/types/district';
import { genusLabel } from '@/lib/verificationText';
import type { QuestInfo, Tree } from '@/types/tree';

/** Claim state and actions for a live quest, owned by the map screen. */
export type QuestActions = { busy: 'claim' | 'cancel' | null; error: string; loggedIn: boolean; onClaim: () => void; onCancel: () => void };
const q = liveText.de.quest;

const statusText = { unverified: 'Mission verfügbar', verified: 'Bereits bestätigt', missing: 'Als fehlend gemeldet', new: 'Neuer Fund' };

export function TreeBottomSheet({ tree, onClose, onOpenDistrict, onScan, userPosition, questActions }: { tree: Tree; onClose: () => void; onOpenDistrict: (district: District) => void; onScan: () => void; userPosition: { lat: number; lng: number } | null; questActions?: QuestActions }) {
  const closeRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('keydown', onKey); previousFocus?.focus(); };
  }, [onClose]);

  const distance = userPosition ? formatDistance(distanceMeters(userPosition, tree)) : null;
  const district = districtForTree(tree);
  const lexicon = tree.sampleAsset ? undefined : lexiconForSpecies(tree.lexiconSpecies ?? tree.species);
  const quest = tree.quest;
  const inventoryUpdated = tree.inventory?.lastSeenAt?.slice(0, 10).split('-').reverse().join('.');
  return <>
    <button className="sheet-backdrop" type="button" onClick={onClose} aria-label="Baumdetails schließen" />
    <section className="tree-sheet" role="dialog" aria-modal="true" aria-labelledby="tree-sheet-title">
      <div className="sheet-atmosphere" aria-hidden="true" />
      <div className="drag-handle" aria-hidden="true" />
      <button ref={closeRef} className="sheet-close" type="button" onClick={onClose} aria-label="Schließen"><X size={20} /></button>
      <div className="sheet-topline"><span><Sparkles size={13} /> FIELD NOTE / MÜNSTER</span><span>{quest ? (quest.kind === 'photo' ? q.photo : q.verify).toUpperCase() : tree.id.toUpperCase()}</span></div>
      <div className="sheet-hero">
        <span className={`sheet-tree-icon ${tree.presentation ? 'presentation-tree-icon' : ''}`}>{tree.presentation ? <TreePine size={42} strokeWidth={1.6} /> : <TreeDeciduous size={42} strokeWidth={1.6} />}</span>
        <div><p className="eyebrow">{tree.presentation ? 'PRÄSENTATIONSBAUM · MS HACK 2026' : quest ? q.eyebrow(tree.area) : `BAUM ENTDECKEN · ${tree.area.toUpperCase()}`}</p><h2 id="tree-sheet-title">{tree.sampleAsset ? `Gattung ${tree.inventory?.genus}` : tree.species}</h2><p className="latin">{tree.presentation ? tree.area : tree.sampleAsset ? 'Genaue Art noch nicht bestimmt' : quest && !quest.genus ? quest.title : tree.speciesLatin}</p>{lexicon && <p className="sheet-hero-signature">{lexicon.signature}</p>}</div>
      </div>
      {quest ? <QuestDetails quest={quest} distance={distance} actions={questActions} onScan={onScan} /> : tree.presentation ? <>
        <div className="sheet-pills"><span className="pill pill-amber"><Star size={13} /> LEGENDARY · FULL ART</span><span className="pill pill-neutral">+{tree.xpReward} XP beim ersten Scan</span></div>
        <div className="presentation-tree-story"><strong>Deine Bühnen-Sonderkarte wartet.</strong><p>Fotografiere den kleinen Tannenbaum auf der Bühne. Die Demo zeigt danach die Festtanne als Full-Art-Karte.</p></div>
        <div className="sheet-info presentation-tree-info"><div><MapPin size={18} /><span>{tree.area}</span></div>{distance && <div><MapPin size={18} /><span>{distance} entfernt</span></div>}</div>
      </> : <>
        <div className="sheet-pills"><span className={`pill ${tree.status === 'unverified' ? 'pill-amber' : tree.status === 'verified' ? 'pill-green' : 'pill-neutral'}`}>{tree.status === 'verified' ? <Check size={13} /> : <Sparkles size={13} />}{tree.sampleAsset ? 'Kataster-Beispiel' : statusText[tree.status]}</span><span className="pill pill-neutral">{tree.sampleAsset ? 'Art offen' : tree.rarity === 'rare' ? 'Selten' : tree.rarity === 'uncommon' ? 'Besonders' : 'Häufig'}</span></div>
        <div className="sheet-info">
          <div><MapPin size={18} /><span>{distance ? `${distance} entfernt` : tree.area}</span></div>
          {tree.inventory && <div><TreeDeciduous size={18} /><span>Gattung: {tree.inventory.genus} · Art: {tree.inventory.species ?? 'noch offen'}</span></div>}
          {tree.inventory?.heightM != null && <div><TreeDeciduous size={18} /><span>Höhe im Beispieldatensatz: {tree.inventory.heightM.toLocaleString('de-DE')} m</span></div>}
          {tree.inventory?.assetStatus && <div><ShieldCheck size={18} /><span>Datensatzstatus: {tree.inventory.assetStatus === 'active' ? 'aktiv' : tree.inventory.assetStatus}</span></div>}
          {tree.inventory?.quarter && <div><MapPin size={18} /><span>{tree.inventory.quarter} · {tree.inventory.district}</span></div>}
          {tree.assetId && <div className="tree-asset-id"><ShieldCheck size={18} /><span>Datensatz-ID: {tree.assetId}</span></div>}
          {tree.inventory?.dataSource && <div><ShieldCheck size={18} /><span>Quelle: {tree.inventory.dataSource}{inventoryUpdated ? ` · Stand ${inventoryUpdated}` : ''}</span></div>}
          {tree.inventory?.qualityFlags?.length ? <div><ShieldCheck size={18} /><span>Datenhinweise: {tree.inventory.qualityFlags.join(', ')}</span></div> : null}
          {tree.sampleAsset && <div><Sparkles size={18} /><span>Scan-Testvorschlag: {tree.species} · noch nicht bestätigt</span></div>}
          <div><Clock3 size={18} /><span>{tree.lastChecked ? `Zuletzt geprüft ${tree.lastChecked}` : 'Noch kein Prüftermin'}</span></div>
          <div><UsersRound size={18} /><span>{tree.verificationCount} {tree.verificationCount === 1 ? 'Bestätigung' : 'Bestätigungen'} bisher</span></div>
          {district && <button type="button" className="tree-district-link" onClick={() => onOpenDistrict(district)}><Flag size={18} /><span>Spielgebiet: {district.name}</span><ArrowRight size={15} /></button>}
        </div>
      </>}
      {lexicon && <div className="tree-knowledge-card">
        <div className="tree-knowledge-top"><span className="tree-knowledge-icon"><BookOpenText size={21} /></span><span><small>NATURLEXIKON</small><strong>{tree.species} erkennen</strong></span></div>
        <div className="tree-knowledge-facts"><span><Leaf size={14} /> {lexicon.leaf}</span><span><Sparkles size={14} /> {lexicon.season}</span></div>
        <Link href={`/lexicon/${lexicon.slug}`} className="tree-knowledge-link">Artenporträt öffnen <ArrowRight size={17} /></Link>
      </div>}
      {!quest && <><button type="button" className="tree-scan-button" onClick={onScan}><Camera size={19} /> {tree.presentation ? 'Festtanne scannen' : 'Diesen Baum scannen'} <ArrowRight size={17} /></button>
      {tree.presentation ? <p className="presentation-tree-note">Testantwort ohne echte Bilderkennung · Der Bühnenbaum zählt nicht für Stadtteil-Punkte.</p> : <div className="mission-preview">
        <div className="mission-preview-top"><span className="mission-symbol"><ShieldCheck size={23} /></span><span className="pill pill-amber">+{tree.xpReward} XP</span></div>
        <h3>Steht dieser Baum noch hier?</h3>
        <p>Ein kurzer Check vor Ort hilft, die Baumkarte aktuell zu halten.</p>
        <button type="button" className="mission-disabled" disabled aria-describedby="mission-note">Mission startet in Phase 3 <ArrowRight size={18} /></button>
        <p id="mission-note" className="sheet-note">Scans prüfen dein Foto und speichern den Fund lokal. Die separate Vor-Ort-Mission folgt später.</p>
      </div>}</>}
      <p className="sheet-footer-note">{tree.presentation ? 'Präsentationsobjekt · Kein öffentlicher Stadtbaum' : quest ? q.footer : tree.sampleAsset ? 'Beispiel-Asset aus dem bereitgestellten Export · Art offen · kein amtlich bestätigter Scan' : 'Standort & Gattung: Stadt Münster · Art, Status und Checks: Demo'}</p>
    </section>
  </>;
}

function QuestDetails({ quest, distance, actions, onScan }: { quest: QuestInfo; distance: string | null; actions?: QuestActions; onScan: () => void }) {
  const expires = quest.claim ? new Date(quest.claim.expiresAt).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' }) : null;
  return <>
    <div className="sheet-pills">
      <span className="pill pill-amber">{quest.kind === 'photo' ? <Camera size={13} /> : <Search size={13} />}{quest.kind === 'photo' ? q.photo : q.verify}</span>
      <span className="pill pill-neutral"><Sparkles size={13} /> {q.xp(quest.rewardPoints)}</span>
      {quest.claim ? <span className="pill pill-green"><Check size={13} /> {q.claimed}</span> : <span className="pill pill-neutral"><UsersRound size={13} /> {q.freeSlots(quest.freeSlots)}</span>}
    </div>
    <div className="sheet-info">
      <div><MapPin size={18} /><span>{distance ? `${distance} entfernt` : q.area}</span></div>
      <div><TreeDeciduous size={18} /><span>{quest.genus ? q.catalogGenus(genusLabel(quest.genus)) : q.catalogMissing(quest.genusRaw)}</span></div>
      <div><Camera size={18} /><span>{q.geofence(quest.geofenceRadiusM)}</span></div>
      {expires ? <div><Clock3 size={18} /><span>{q.expires(expires)}</span></div> : <div><ShieldCheck size={18} /><span>{q.xpAfterReview(quest.rewardPoints)}</span></div>}
    </div>
    <div className="mission-preview quest-preview">
      <div className="mission-preview-top"><span className="mission-symbol">{quest.kind === 'photo' ? <Camera size={21} /> : <Search size={21} />}</span><span className="pill pill-amber">{q.xp(quest.rewardPoints)}</span></div>
      <h3>{quest.title}</h3>
      <p>{quest.description ?? (quest.kind === 'photo' ? q.photoGoal : q.verifyGoal)}</p>
      {actions?.error && <p className="quest-error" role="alert">{actions.error}</p>}
      {quest.claim ? <>
        <button type="button" className="tree-scan-button" onClick={onScan}><Camera size={19} /> {q.scan} <ArrowRight size={17} /></button>
        <button type="button" className="quest-secondary" onClick={actions?.onCancel} disabled={actions?.busy !== null}>{actions?.busy === 'cancel' ? <><Loader2 size={16} className="spin" /> {q.cancelling}</> : <><X size={16} /> {q.cancel}</>}</button>
      </> : <button type="button" className="tree-scan-button" onClick={actions?.onClaim} disabled={!actions || actions.busy !== null}>{actions?.busy === 'claim' ? <><Loader2 size={19} className="spin" /> {q.claiming}</> : <><Sparkles size={19} /> {actions?.loggedIn === false ? q.loginToPlay : q.claim} <ArrowRight size={17} /></>}</button>}
    </div>
  </>;
}
