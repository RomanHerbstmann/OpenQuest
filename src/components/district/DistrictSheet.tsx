'use client';

import { useEffect } from 'react';
import { ArrowLeft, ArrowRight, Clock3, Crown, Flag, Info, Leaf, ShieldCheck, Trees, X } from 'lucide-react';
import { districts } from '@/data/districts';
import { districtStandings, TERRITORY_WINDOW_DAYS, treesInDistrict } from '@/lib/districts';
import type { District, TerritoryContribution } from '@/types/district';

type Props = {
  district: District | null;
  contributions: TerritoryContribution[];
  onSelect: (district: District) => void;
  onBack: () => void;
  onClose: () => void;
};

export function DistrictSheet({ district, contributions, onSelect, onBack, onClose }: Props) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const summary = district ? districtStandings(district, contributions) : null;
  const districtTrees = district ? treesInDistrict(district) : [];
  const openChecks = districtTrees.filter((tree) => tree.status === 'unverified').length;

  return <>
    <button className="sheet-backdrop" type="button" onClick={onClose} aria-label="Viertelansicht schließen" />
    <section className="tree-sheet district-sheet" role="dialog" aria-modal="true" aria-labelledby="district-sheet-title">
      <div className="drag-handle" aria-hidden="true" />
      <button className="sheet-close" type="button" onClick={onClose} aria-label="Schließen"><X size={20} /></button>
      {district && summary ? <>
        <button className="district-back" type="button" onClick={onBack}><ArrowLeft size={15} /> Alle Viertel</button>
        <div className="district-heading"><span className="district-heading-icon"><Flag size={27} /></span><div><p className="eyebrow">SPIELGEBIET · DEMO</p><h2 id="district-sheet-title">{district.name}</h2><span>Eigene Bäume zählen. Das Viertel gehört der Person mit den meisten.</span></div></div>
        <div className="district-summary-card"><div className="district-summary-icon"><Crown size={23} /></div><div><span>AKTUELLE FÜHRUNG</span><strong>{summary.owner ? summary.owner.name : summary.tied ? 'Umkämpft' : 'Noch frei'}</strong><small>{summary.owner ? `${summary.topCount} ${summary.topCount === 1 ? 'Baum' : 'Bäume'} in der aktuellen Wertung` : summary.tied ? `Gleichstand bei ${summary.topCount} Bäumen` : 'Die erste bestätigte Beobachtung übernimmt die Führung.'}</small></div></div>
        <div className="district-mini-stats"><div><Trees size={18} /><strong>{districtTrees.length}</strong><span>Bäume auf der Karte</span></div><div><Leaf size={18} /><strong>{openChecks}</strong><span>Offene Checks</span></div><div><ShieldCheck size={18} /><strong>{summary.playerCount}</strong><span>Deine Wertung</span></div></div>
        <div className="district-section-title"><h3>Rangliste</h3><span>Letzte {TERRITORY_WINDOW_DAYS} Tage</span></div>
        <div className="district-ranking">{summary.ranking.map((player, index) => <div className={`district-rank-row ${player.id === 'explorer' ? 'you' : ''}`} key={player.id}><span className="district-rank-number">{index + 1}</span><span className="district-player-dot" style={{ background: player.color }} /><strong>{player.name}</strong><span>{player.count} {player.count === 1 ? 'Baum' : 'Bäume'}</span></div>)}</div>
        <div className="district-next"><Flag size={17} /><span>{summary.owner?.id === 'explorer' ? 'Du führst dieses Viertel.' : `Noch ${summary.treesToLead} ${summary.treesToLead === 1 ? 'bestätigter Baum' : 'bestätigte Bäume'} bis zur alleinigen Führung.`}</span></div>
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
