'use client';

import Image from 'next/image';
import Link from 'next/link';
import { useState } from 'react';
import { ArrowRight, BookOpen, BookOpenText, Camera, Eye, LockKeyhole, Sparkles } from 'lucide-react';
import { CardViewer } from '@/components/collection/CardViewer';
import { ScanModal } from '@/components/scan/ScanModal';
import { Brand } from '@/components/ui/Brand';
import { usePlayer } from '@/context/PlayerContext';
import { cardArtBySpecies, cardBackSrc } from '@/data/cardArt';
import { species } from '@/data/species';

export default function CollectionPage() {
  const { progress, ready } = usePlayer();
  const [scanOpen, setScanOpen] = useState(false);
  const [viewingIndex, setViewingIndex] = useState<number | null>(null);
  const [filter, setFilter] = useState<'all' | 'collected'>('all');
  const unlockedCards = ready ? progress.unlockedCards : [];
  // Show only motifs with finished artwork. Maple species share one card image.
  const motifs = new Map<string, (typeof species)[number]>();
  species.forEach((item) => {
    const art = cardArtBySpecies[item.name];
    if (!art) return;
    const previous = motifs.get(art.src);
    if (!previous || (!unlockedCards.includes(previous.name) && unlockedCards.includes(item.name))) motifs.set(art.src, item);
  });
  const availableCards = [...motifs.values()];
  const collectedCardCount = availableCards.filter((item) => unlockedCards.includes(item.name)).length;
  const visibleCards = filter === 'collected' ? availableCards.filter((item) => unlockedCards.includes(item.name)) : availableCards;
  const heroArt = cardArtBySpecies[availableCards.find((item) => unlockedCards.includes(item.name))?.name ?? 'Stieleiche'];

  return <main className="section-page collection-page design2-page">
    <div className="page-top"><Brand /><span className="d2-top-pill"><BookOpen size={15} /> DEIN BAUMBUCH</span></div>
    <section className="collection-hero">
      <div className="collection-hero-grain" aria-hidden="true" />
      <div className="collection-hero-copy"><span className="d2-hero-kicker"><Sparkles size={15} /> DEIN ATLAS WÄCHST</span><h1>Jeder Fund<br /><em>erzählt mehr.</em></h1><p>Scanne Bäume, sammle ihre Karten und lerne die Arten hinter deinen Funden kennen.</p>
        <div className="collection-hero-actions"><button type="button" className="collection-scan-button" onClick={() => setScanOpen(true)}><Camera size={18} /> Baum scannen <ArrowRight size={17} /></button><Link href="/lexicon" className="collection-lexicon-button"><BookOpenText size={18} /> Naturlexikon</Link></div>
        <div className="collection-hero-progress"><div><strong>{collectedCardCount} <span>/ {availableCards.length}</span></strong><small>Karten aufgedeckt</small></div><div className="collection-progress-track"><span style={{ width: `${availableCards.length ? collectedCardCount / availableCards.length * 100 : 0}%` }} /></div></div>
      </div>
      {heroArt && <div className="collection-hero-cards" aria-hidden="true"><div className="collection-hero-card back"><Image src={cardBackSrc} alt="" fill sizes="180px" /></div><div className="collection-hero-card front"><Image src={heroArt.src} alt="" fill sizes="180px" /></div><span className="collection-hero-spark">✦</span></div>}
    </section>
    <div className="d2-section-heading collection-section-heading"><div><span className="d2-kicker">KARTEN / {String(availableCards.length).padStart(2, '0')}</span><h2>Deine Karten</h2></div><span>{collectedCardCount} gesammelt</span></div>
    <div className="collection-filter-bar" role="group" aria-label="Karten filtern"><button type="button" className={filter === 'all' ? 'active' : ''} onClick={() => setFilter('all')} aria-pressed={filter === 'all'}>Alle Motive</button><button type="button" className={filter === 'collected' ? 'active' : ''} onClick={() => setFilter('collected')} aria-pressed={filter === 'collected'}>Gesammelt</button></div>
    {!ready ? <div className="collection-empty" role="status">Baumbuch wird geladen …</div> : visibleCards.length ? <div className={`species-grid ${visibleCards.length === 1 ? 'single-card' : ''}`}>{visibleCards.map((item, visibleIndex) => {
      const index = species.findIndex((candidate) => candidate.name === item.name);
      const cardArt = cardArtBySpecies[item.name]!;
      const unlocked = unlockedCards.includes(item.name);
      const representedSpecies = cardArt.src === '/cards/ahorn.png' ? ['Bergahorn', 'Spitzahorn'] : [item.name];
      const count = progress.scanEvents.filter((event) => representedSpecies.includes(event.species)).length;
      return <article className={`species-card surface-card ${unlocked ? 'unlocked' : 'locked'} ${item.name === 'Festtanne' ? 'presentation-card' : ''}`} key={cardArt.src}>
        <button className="species-card-open" type="button" onClick={() => setViewingIndex(visibleIndex)} aria-label={`Sammelkarte ${item.name} ansehen`}><Eye size={16} /><span>Ansehen</span></button>
        <div className="species-art has-card-image"><Image src={unlocked ? cardArt.src : cardBackSrc} alt={unlocked ? `OpenQuest-Sammelkarte mit ${cardArt.motif}-Motiv` : 'Verdeckte OpenQuest-Sammelkarte'} fill sizes="(max-width: 699px) 45vw, 18vw" /></div>
        <div className="species-copy"><div className="species-copy-heading"><small>ART / {String(index + 1).padStart(2, '0')}</small><span className={`species-status ${unlocked ? 'unlocked' : ''}`} aria-label={unlocked ? 'Aufgedeckt' : 'Noch verdeckt'}>{unlocked ? <Sparkles size={15} /> : <LockKeyhole size={15} />}</span></div><h2>{item.name}</h2><p>{unlocked ? item.latin : 'Noch verdeckt'}</p><small className="species-note">{unlocked ? cardArt.src === '/cards/ahorn.png' && unlockedCards.includes('Bergahorn') && unlockedCards.includes('Spitzahorn') ? 'Bergahorn & Spitzahorn gesammelt' : item.note : 'Scanne einen passenden Baum, um die Karte aufzudecken.'}</small><span>{unlocked ? count > 0 ? `${count} ${count === 1 ? 'Fund' : 'Funde'}` : 'Bereits aufgedeckt' : 'Noch nicht entdeckt'}</span></div>
      </article>;
    })}</div> : <div className="collection-empty"><Sparkles size={25} /><strong>{filter === 'collected' ? 'Noch keine Bildkarten gesammelt.' : 'Noch keine Kartenmotive hinterlegt.'}</strong><span>Scanne einen Baum, um dein Baumbuch zu füllen.</span><button type="button" className="collection-empty-action" onClick={() => setScanOpen(true)}><Camera size={16} /> Baum scannen</button></div>}
    <p className="prototype-footnote">Demo-Prototyp · Die Festtanne wird am Präsentationspunkt Hafenweg 7 aufgedeckt. Zierkirsche ist bereits im Baumbuch; die zehn importierten Beispielbäume zeigen ergänzende Katasterdaten.</p>
    {scanOpen && <ScanModal onClose={() => setScanOpen(false)} />}
    {viewingIndex !== null && visibleCards[viewingIndex] && <CardViewer initialIndex={viewingIndex} cardNames={visibleCards.map((item) => item.name)} unlockedCards={unlockedCards} scanEvents={progress.scanEvents} onClose={() => setViewingIndex(null)} />}
  </main>;
}
