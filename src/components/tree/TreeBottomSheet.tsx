'use client';

import { useEffect } from 'react';
import { ArrowRight, Check, Clock3, MapPin, ShieldCheck, Sparkles, TreeDeciduous, UsersRound, X } from 'lucide-react';
import { distanceMeters, formatDistance } from '@/lib/distance';
import type { Tree } from '@/types/tree';

const statusText = { unverified: 'Mission verfügbar', verified: 'Bereits bestätigt', missing: 'Als fehlend gemeldet', new: 'Neuer Fund' };

export function TreeBottomSheet({ tree, onClose, userPosition }: { tree: Tree; onClose: () => void; userPosition: { lat: number; lng: number } | null }) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const distance = userPosition ? formatDistance(distanceMeters(userPosition, tree)) : null;
  return <>
    <button className="sheet-backdrop" type="button" onClick={onClose} aria-label="Baumdetails schließen" />
    <section className="tree-sheet" role="dialog" aria-modal="true" aria-labelledby="tree-sheet-title">
      <div className="drag-handle" aria-hidden="true" />
      <button className="sheet-close" type="button" onClick={onClose} aria-label="Schließen"><X size={20} /></button>
      <div className="sheet-hero">
        <span className="sheet-tree-icon"><TreeDeciduous size={42} strokeWidth={1.6} /></span>
        <div><p className="eyebrow">BAUM ENTDECKEN · {tree.area.toUpperCase()}</p><h2 id="tree-sheet-title">{tree.species}</h2><p className="latin">{tree.speciesLatin}</p></div>
      </div>
      <div className="sheet-pills"><span className={`pill ${tree.status === 'unverified' ? 'pill-amber' : tree.status === 'verified' ? 'pill-green' : 'pill-neutral'}`}>{tree.status === 'verified' ? <Check size={13} /> : <Sparkles size={13} />}{statusText[tree.status]}</span><span className="pill pill-neutral">{tree.rarity === 'rare' ? 'Selten' : tree.rarity === 'uncommon' ? 'Besonders' : 'Häufig'}</span></div>
      <div className="sheet-info">
        <div><MapPin size={18} /><span>{distance ? `${distance} entfernt` : tree.area}</span></div>
        <div><Clock3 size={18} /><span>{tree.lastChecked ? `Zuletzt geprüft ${tree.lastChecked}` : 'Noch kein Prüftermin'}</span></div>
        <div><UsersRound size={18} /><span>{tree.verificationCount} {tree.verificationCount === 1 ? 'Bestätigung' : 'Bestätigungen'} bisher</span></div>
      </div>
      <div className="mission-preview">
        <div className="mission-preview-top"><span className="mission-symbol"><ShieldCheck size={23} /></span><span className="pill pill-amber">+{tree.xpReward} XP</span></div>
        <h3>Steht dieser Baum noch hier?</h3>
        <p>Ein kurzer Check vor Ort hilft, die Baumkarte aktuell zu halten.</p>
        <button type="button" className="mission-disabled" disabled aria-describedby="mission-note">Mission startet in Phase 3 <ArrowRight size={18} /></button>
        <p id="mission-note" className="sheet-note">Dieser Prototyp zeigt zunächst Karte und Baumdetails. Beiträge werden noch nicht erfasst.</p>
      </div>
      <p className="sheet-footer-note">Demo-Daten · Kein amtlich geprüfter Baumstandort</p>
    </section>
  </>;
}
