'use client';

import Image from 'next/image';
import { usePlayer } from '@/context/PlayerContext';
import { cardArtBySpecies, cardBackSrc } from '@/data/cardArt';

export function LexiconArt({ name, emoji }: { name: string; emoji?: string }) {
  const { progress } = usePlayer();
  const art = cardArtBySpecies[name as keyof typeof cardArtBySpecies];
  const unlocked = progress.unlockedCards.includes(name);
  return <div className="lexicon-detail-visual" aria-label={art ? unlocked ? `Künstlerisches Kartenmotiv ${name}` : `Verdeckte Sammelkarte ${name}` : `Symbol ${name}`}>
    <span className="lexicon-detail-halo" aria-hidden="true" />
    {art ? <Image src={unlocked ? art.src : cardBackSrc} alt={unlocked ? `Künstlerisches OpenQuest-Kartenmotiv: ${name}` : 'Verdeckte OpenQuest-Sammelkarte'} fill sizes="(max-width: 700px) 42vw, 260px" /> : <span className="lexicon-detail-emoji" aria-hidden="true">{emoji}</span>}
    <small>{art ? unlocked ? 'KARTENMOTIV' : 'KARTE VERDECKT' : 'ARTENSYMBOL'}</small>
  </div>;
}
