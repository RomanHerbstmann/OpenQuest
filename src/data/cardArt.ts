import type { species } from './species';

type SpeciesName = (typeof species)[number]['name'];

export const cardArtBySpecies: Partial<Record<SpeciesName, { src: string; motif: string }>> = {
  Stieleiche: { src: '/cards/eiche.png', motif: 'Eiche' },
  Bergahorn: { src: '/cards/ahorn.png', motif: 'Ahorn' },
  Spitzahorn: { src: '/cards/ahorn.png', motif: 'Ahorn' },
  Winterlinde: { src: '/cards/linde.png', motif: 'Linde' },
  Rosskastanie: { src: '/cards/rosskastanie.png', motif: 'Rosskastanie' },
  Birke: { src: '/cards/birke.png', motif: 'Birke' },
  Kiefer: { src: '/cards/kiefer.png', motif: 'Kiefer' },
  Zierkirsche: { src: '/cards/zierkirsche.png', motif: 'Zierkirsche' },
  Festtanne: { src: '/cards/festtanne.png', motif: 'Festtanne' },
};
