'use client';

import { useEffect } from 'react';
import { ArrowLeft, ArrowRight, Clock3, Crown, Flag, Info, Leaf, MapPin, ShieldCheck, Trees, X } from 'lucide-react';
import { districts } from '@/data/districts';
import { districtStandings, TERRITORY_WINDOW_DAYS, treesInDistrict } from '@/lib/districts';
import type { District, TerritoryContribution } from '@/types/district';

type Props = {
  district: District | null;
  contributions: TerritoryContribution[];
  onSelect: (district: District) => void;
  onFocus: (district: District) => void;
  onBack: () => void;
  onClose: () => void;
};

export function DistrictSheet({ district, contributions, onSelect, onFocus, onBack, onClose }: Props) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const summary = district ? districtStandings(district, contributions) : null;
  const districtTrees = district ? treesInDistrict(district) : [];
  const openChecks = districtTrees.filter((tree) => tree.status === 'unverified').length;
  const youLead = summary?.owner?.id === 'explorer';
  const leadMargin = summary?.owner ? summary.topCount - (summary.ranking[1]?.count ?? 0) : 0;
  const goal = (summary?.topCount ?? 0) + 1;
  const progress = youLead ? 100 : Math.min(100, Math.round(((summary?.playerCount ?? 0) / goal) * 100));

  return <>
    <button className="sheet-backdrop" type="button" onClick={onClose} aria-label="Viertelansicht schließen" />
    <section className="tree-sheet district-sheet" role="dialog" aria-modal="true" aria-labelledby="district-sheet-title">
      <div className="drag-handle" aria-hidden="true" />
      <button className="sheet-close" type="button" onClick={onClose} aria-label="Schließen"><X size={20} /></button>
      {district && summary ? <>
        <button className="district-back" type="button" onClick={onBack}><ArrowLeft size={15} /> Alle Viertel</button>
        <div className="district-heading"><span className="district-heading-icon"><Flag size={27} /></span><div><p className="eyebrow">SPIELVIERTEL · DEMO</p><h2 id="district-sheet-title">{district.name}</h2><span>Wer die meisten verschiedenen Bäume bestätigt, führt dieses Viertel.</span></div></div>
        <div className={`district-summary-card ${summary.tied ? 'is-contested' : ''}`} style={{ '--district-accent': summary.owner?.color ?? (summary.tied ? '#c58b52' : '#70b28e') } as React.CSSProperties}><div className="district-summary-icon">{summary.owner ? <Crown size={23} /> : <Flag size={23} />}</div><div><span>{summary.owner ? 'AKTUELLE FÜHRUNG' : summary.tied ? 'VIERTEL UMKÄMPFT' : 'NOCH ZU EROBERN'}</span><strong>{summary.owner ? summary.owner.name : summary.tied ? 'Gleichstand' : 'Noch frei'}</strong><small>{summary.owner ? `${summary.topCount} ${summary.topCount === 1 ? 'Baum' : 'Bäume'} in der aktuellen Wertung` : summary.tied ? `Mehrere Spieler:innen haben ${summary.topCount} Bäume · keine Krone` : 'Der erste geprüfte Baum startet die Wertung.'}</small></div></div>
        <div className="district-mini-stats"><div><Trees size={18} /><strong>{districtTrees.length}</strong><span>Bäume auf der Karte</span></div><div><Leaf size={18} /><strong>{openChecks}</strong><span>Offene Checks</span></div><div><ShieldCheck size={18} /><strong>{summary.playerCount}</strong><span>Deine Wertung</span></div></div>
        <div className="district-section-title"><h3>Top 3 im Viertel</h3><span>Letzte {TERRITORY_WINDOW_DAYS} Tage</span></div>
        <div className="district-podium">{[summary.ranking[1], summary.ranking[0], summary.ranking[2]].map((player, slot) => <div className={`district-podium-player ${slot === 1 ? 'first' : ''} ${summary.owner?.id === player.id ? 'is-owner' : ''}`} key={player.id} style={{ '--player-color': player.color } as React.CSSProperties}><span className="district-podium-position">{summary.owner?.id === player.id ? <Crown size={18} aria-label="Führt das Viertel" /> : summary.topCount === 0 ? '–' : player.rank}</span><strong>{player.name}</strong><small>{player.count} {player.count === 1 ? 'Baum' : 'Bäume'}</small></div>)}</div>
        <div className="district-section-title district-all-title"><h3>Alle Plätze</h3><span>Gleichstand = gleicher Rang</span></div>
        <div className="district-ranking">{summary.ranking.map((player) => <div className={`district-rank-row ${player.id === 'explorer' ? 'you' : ''} ${summary.owner?.id === player.id ? 'is-owner' : ''}`} key={player.id}><span className="district-rank-number">{summary.owner?.id === player.id ? <Crown size={15} aria-label="Führt das Viertel" /> : summary.topCount === 0 ? '–' : player.rank}</span><span className="district-player-dot" style={{ background: player.color }} /><strong>{player.name}</strong><span>{player.count} {player.count === 1 ? 'Baum' : 'Bäume'}</span></div>)}</div>
        <div className="district-capture-progress"><div className="district-progress-copy"><strong>{youLead ? `Du führst mit ${leadMargin} ${leadMargin === 1 ? 'Baum' : 'Bäumen'} Vorsprung` : summary.topCount === 0 ? 'Der erste Baum kann das Viertel erobern' : `Noch ${summary.treesToLead} ${summary.treesToLead === 1 ? 'Baum' : 'Bäume'} bis zur Führung`}</strong><span>{youLead ? 'Verteidige deinen Platz mit neuen Funden.' : `${summary.playerCount} von ${goal} benötigten Bäumen`}</span></div><div className="district-progress-track" role="progressbar" aria-label="Fortschritt zur Führung" aria-valuemin={0} aria-valuemax={100} aria-valuenow={progress}><span style={{ width: `${progress}%` }} /></div></div>
        <button className="district-find-button" type="button" onClick={() => onFocus(district)}><MapPin size={17} /> Bäume im Viertel entdecken <ArrowRight size={16} /></button>
        <div className="district-rule"><Clock3 size={17} /><p>Jeder unterschiedliche, im Projekt geprüfte Baum zählt pro Person einmal, wenn er in den letzten {TERRITORY_WINDOW_DAYS} Tagen beobachtet wurde. Alte Beiträge fallen aus der Wertung. Ein Gleichstand hat keinen Besitzer.</p></div>
        <p className="district-disclaimer"><Info size={14} /> Schematische Demo-Grenzen und Demo-Spieler:innen. Missionen und echte Wettbewerbsbeiträge folgen später.</p>
      </> : <>
        <div className="district-heading"><span className="district-heading-icon"><Flag size={27} /></span><div><p className="eyebrow">MÜNSTER ENTDECKEN</p><h2 id="district-sheet-title">Viertel erobern</h2><span>Wähle ein Spielgebiet und sieh, wer gerade vorne liegt.</span></div></div>
        <div className="district-overview-intro"><Crown size={20} /><div><strong>Wer die meisten Bäume bestätigt, führt.</strong><span>Die Wertung erneuert sich laufend über {TERRITORY_WINDOW_DAYS} Tage.</span></div></div>
        <div className="district-overview-list">{districts.map((item) => {
          const standing = districtStandings(item, contributions);
          const count = treesInDistrict(item).length;
          return <button className="district-overview-row" type="button" key={item.id} onClick={() => onSelect(item)}>
            <span className="district-overview-icon"><Flag size={18} /></span><span className="district-overview-copy"><strong>{item.name}</strong><small>{count} Bäume · {standing.owner ? `führt: ${standing.owner.name}` : standing.tied ? 'umkämpft' : 'noch frei'}</small></span><ArrowRight size={17} />
          </button>;
        })}</div>
        <p className="district-disclaimer"><Info size={14} /> Diese Gebiete sind schematisch und keine amtlichen Stadtteilgrenzen. Die Rangliste enthält Demo-Spieler:innen.</p>
      </>}
    </section>
  </>;
}
