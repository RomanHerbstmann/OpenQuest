'use client';

import { BookOpen, LockKeyhole, Sparkles } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { usePlayer } from '@/context/PlayerContext';
import { species } from '@/data/species';
import { trees } from '@/data/trees';

export default function CollectionPage() {
  const { progress, ready } = usePlayer();
  const discovered = ready ? progress.discoveredSpecies : [];
  return <main className="section-page">
    <div className="page-top"><Brand /><span className="pill pill-green"><BookOpen size={14} /> BAUMBUCH</span></div>
    <div className="collection-header"><div><p className="eyebrow">DEINE SAMMLUNG</p><h1 className="page-heading">Münsters Bäume<br /><span>kennenlernen.</span></h1><p className="page-description">Jede Art erzählt eine Geschichte. Erkunde die Karte und entdecke, welche Bäume in deiner Nähe wachsen.</p></div><div className="collection-progress"><strong>{discovered.length}<span> / {species.length}</span></strong><small>Arten entdeckt</small><div className="progress-track"><span style={{ width: `${discovered.length * 10}%` }} /></div></div></div>
    <div className="section-label"><span>ALLE ARTEN</span><span>{species.length} Einträge</span></div>
    <div className="species-grid">{species.map((item, index) => {
      const unlocked = discovered.includes(item.name);
      const count = progress.discoveredTrees.filter((id) => trees.find((tree) => tree.id === id)?.species === item.name).length;
      return <article className={`species-card surface-card ${unlocked ? '' : 'locked'}`} key={item.name}>
        <div className="species-art"><span>{unlocked ? item.emoji : '✦'}</span><small>{String(index + 1).padStart(2, '0')}</small></div>
        <div className="species-copy"><h2>{unlocked ? item.name : '???'}</h2><p>{unlocked ? item.latin : 'Noch nicht entdeckt'}</p><span>{unlocked ? `${count} ${count === 1 ? 'Fund' : 'Funde'}` : item.note}</span></div>
        <span className={`species-status ${unlocked ? 'unlocked' : ''}`}>{unlocked ? <Sparkles size={15} /> : <LockKeyhole size={15} />}</span>
      </article>;
    })}</div>
    <p className="prototype-footnote">Demo-Prototyp · Arten werden ab Phase 3 durch abgeschlossene Missionen freigeschaltet.</p>
  </main>;
}
