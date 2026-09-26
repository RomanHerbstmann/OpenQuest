'use client';

import Image from 'next/image';
import Link from 'next/link';
import { useEffect, useRef, useState, type PointerEvent } from 'react';
import { ChevronLeft, ChevronRight, Sparkles, X } from 'lucide-react';
import { cardArtBySpecies } from '@/data/cardArt';
import { lexiconForSpecies } from '@/data/lexicon';
import { species } from '@/data/species';

export function CardViewer({ initialIndex, cardNames, discovered, onClose }: {
  initialIndex: number;
  cardNames: string[];
  discovered: string[];
  onClose: () => void;
}) {
  const [index, setIndex] = useState(initialIndex);
  const closeRef = useRef<HTMLButtonElement>(null);
  const tiltRef = useRef<HTMLDivElement>(null);
  const cards = species.filter((candidate) => cardNames.includes(candidate.name));
  const item = cards[index];
  const art = cardArtBySpecies[item.name]!;
  const unlocked = discovered.includes(item.name);
  const lexicon = lexiconForSpecies(item.name);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeRef.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
      if (event.key === 'ArrowLeft') setIndex((current) => (current + cards.length - 1) % cards.length);
      if (event.key === 'ArrowRight') setIndex((current) => (current + 1) % cards.length);
    };
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', onKeyDown);
      previousFocus?.focus();
    };
  }, [onClose, cards.length]);

  const resetTilt = () => {
    tiltRef.current?.style.setProperty('--tilt-x', '0deg');
    tiltRef.current?.style.setProperty('--tilt-y', '0deg');
  };

  const moveTilt = (event: PointerEvent<HTMLDivElement>) => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const bounds = event.currentTarget.getBoundingClientRect();
    const x = (event.clientX - bounds.left) / bounds.width - .5;
    const y = (event.clientY - bounds.top) / bounds.height - .5;
    event.currentTarget.style.setProperty('--tilt-x', `${(-y * 12).toFixed(1)}deg`);
    event.currentTarget.style.setProperty('--tilt-y', `${(x * 12).toFixed(1)}deg`);
    event.currentTarget.style.setProperty('--shine-x', `${((x + .5) * 100).toFixed(1)}%`);
    event.currentTarget.style.setProperty('--shine-y', `${((y + .5) * 100).toFixed(1)}%`);
  };

  const step = (direction: number) => {
    resetTilt();
    setIndex((current) => (current + direction + cards.length) % cards.length);
  };

  return <div className="card-viewer-overlay" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <section className="card-viewer" role="dialog" aria-modal="true" aria-label={`Sammelkarte ${item.name} ansehen`}>
      <header className="card-viewer-header">
        <div><span className="card-viewer-kicker"><Sparkles size={14} /> DEIN BAUMBUCH</span><h2>{item.name}</h2><p>{index + 1} / {cards.length} · {unlocked ? 'Gesammelt' : 'Noch nicht entdeckt'}</p></div>
        <button ref={closeRef} className="card-viewer-close" type="button" onClick={onClose} aria-label="Kartenansicht schließen"><X size={20} /></button>
      </header>
      <div className="card-viewer-stage">
        {cards.length > 1 && <button className="card-viewer-arrow" type="button" onClick={() => step(-1)} aria-label="Vorherige Karte"><ChevronLeft size={25} /></button>}
        <div className="card-viewer-motion" key={item.name}>
          <div ref={tiltRef} className={`card-viewer-card has-art ${item.name === 'Festtanne' && unlocked ? 'legendary' : ''}`} onPointerMove={moveTilt} onPointerLeave={resetTilt}>
            <Image src={art.src} alt={`Sammelkarte ${item.name} in voller Größe`} fill sizes="(max-width: 600px) 72vw, 340px" priority />
            <div className="card-viewer-sheen" aria-hidden="true" />
            <div className="card-viewer-sparkles" aria-hidden="true"><i /><i /><i /><i /><i /><i /></div>
          </div>
        </div>
        {cards.length > 1 && <button className="card-viewer-arrow" type="button" onClick={() => step(1)} aria-label="Nächste Karte"><ChevronRight size={25} /></button>}
      </div>
      <footer className="card-viewer-footer"><span>{item.latin}</span><strong>{item.note}</strong><small>Bewege die Karte und entdecke den Holo-Effekt ✨</small>{lexicon && <Link className="card-viewer-lexicon-link" href={`/lexicon/${lexicon.slug}`}>Im Naturlexikon nachlesen <ChevronRight size={16} /></Link>}</footer>
    </section>
  </div>;
}
