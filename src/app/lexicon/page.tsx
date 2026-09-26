'use client';

import Link from 'next/link';
import { useMemo, useState } from 'react';
import { ArrowUpRight, BookOpenText, Leaf, Search, Sparkles } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { lexiconEntries } from '@/data/lexicon';
import { species } from '@/data/species';

type Group = 'Alle' | 'Laubbaum' | 'Nadelbaum' | 'Sonderfund';
const groups: Group[] = ['Alle', 'Laubbaum', 'Nadelbaum', 'Sonderfund'];

export default function LexiconPage() {
  const [query, setQuery] = useState('');
  const [group, setGroup] = useState<Group>('Alle');
  const matches = useMemo(() => lexiconEntries.filter((entry) => {
    const speciesEntry = species.find((item) => item.name === entry.name);
    const target = `${entry.name} ${speciesEntry?.latin ?? ''} ${entry.signature}`.toLocaleLowerCase('de-DE');
    return (group === 'Alle' || entry.group === group) && target.includes(query.trim().toLocaleLowerCase('de-DE'));
  }), [query, group]);

  return <main className="section-page lexicon-page design2-page">
    <div className="page-top"><Brand /><span className="d2-top-pill"><BookOpenText size={15} /> NATURLEXIKON</span></div>
    <section className="lexicon-hero">
      <div className="lexicon-orbit lexicon-orbit-one" aria-hidden="true" /><div className="lexicon-orbit lexicon-orbit-two" aria-hidden="true" />
      <span className="lexicon-hero-kicker"><Sparkles size={14} /> DEIN WISSEN WÄCHST</span>
      <h1>Die Stadt ist<br /><em>voller Leben.</em></h1>
      <p>Entdecke, wie du Münsters Bäume erkennst – vom ersten Blatt bis zur markanten Rinde.</p>
      <div className="lexicon-hero-footer"><span><strong>{lexiconEntries.length}</strong> Arten & Gruppen</span><span><Leaf size={17} /> Immer offen zum Nachschlagen</span></div>
    </section>
    <div className="lexicon-toolbar">
      <label className="lexicon-search"><Search size={19} aria-hidden="true" /><span className="sr-only">Arten suchen</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Baumart oder Merkmal suchen" /></label>
      <div className="lexicon-groups" role="group" aria-label="Arten filtern">{groups.map((item) => <button key={item} type="button" className={group === item ? 'active' : ''} onClick={() => setGroup(item)} aria-pressed={group === item}>{item === 'Laubbaum' ? 'Laubbäume' : item === 'Nadelbaum' ? 'Nadelbäume' : item}</button>)}</div>
    </div>
    <div className="d2-section-heading"><div><span className="d2-kicker">FELDFÜHRER / 01</span><h2>Arten entdecken</h2></div><span>{matches.length} {matches.length === 1 ? "Eintrag" : "Einträge"}</span></div>
    {matches.length ? <div className="lexicon-grid">{matches.map((entry, index) => {
      const speciesEntry = species.find((item) => item.name === entry.name);
      return <Link href={`/lexicon/${entry.slug}`} className="lexicon-card" key={entry.slug} style={{ '--entry-accent': entry.accent, '--entry-index': index } as React.CSSProperties}>
        <span className="lexicon-card-art" aria-hidden="true"><span>{speciesEntry?.emoji ?? '🌿'}</span><i /></span>
        <span className="lexicon-card-copy"><small>{entry.group} · NR. {String(index + 1).padStart(2, '0')}</small><strong>{entry.name}</strong><em>{speciesEntry?.latin}</em><span>{entry.signature}</span></span>
        <span className="lexicon-card-arrow"><ArrowUpRight size={19} /></span>
      </Link>;
    })}</div> : <div className="lexicon-empty"><Search size={27} /><strong>Keine passende Art gefunden</strong><span>Versuch einen anderen Namen oder ein Merkmal.</span></div>}
    <p className="lexicon-editorial-note">Artenwissen im Prototyp · Diese allgemeinen Merkmale ersetzen keine Bestimmung oder Prüfung eines einzelnen Stadtbaums.</p>
  </main>;
}
