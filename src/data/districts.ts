import type { District } from '@/types/district';

// Schematische, aneinandergrenzende Spielgebiete für den Prototyp.
// Diese Polygone sind keine amtlichen Stadtteilgrenzen.
export const districts: District[] = [
  {
    id: 'schloss', name: 'Schloss & Garten', shortName: 'Schloss', center: [51.9664, 7.6174],
    polygon: [[51.9597, 7.6130], [51.9705, 7.6130], [51.9705, 7.6275], [51.9597, 7.6275]],
  },
  {
    id: 'kreuzviertel', name: 'Kreuzviertel', shortName: 'Kreuzviertel', center: [51.9692, 7.6334],
    polygon: [[51.9635, 7.6275], [51.9705, 7.6275], [51.9705, 7.6375], [51.9635, 7.6375]],
  },
  {
    id: 'altstadt', name: 'Altstadt', shortName: 'Altstadt', center: [51.9627, 7.6359],
    polygon: [[51.9578, 7.6275], [51.9635, 7.6275], [51.9635, 7.6375], [51.9578, 7.6375]],
  },
  {
    id: 'aasee', name: 'Aasee', shortName: 'Aasee', center: [51.9535, 7.6170],
    polygon: [[51.9500, 7.6130], [51.9597, 7.6130], [51.9597, 7.6275], [51.9500, 7.6275]],
  },
  {
    id: 'suedviertel', name: 'Südviertel', shortName: 'Südviertel', center: [51.9515, 7.6335],
    polygon: [[51.9500, 7.6275], [51.9578, 7.6275], [51.9578, 7.6375], [51.9500, 7.6375]],
  },
  {
    id: 'hoerster', name: 'Hörster Viertel', shortName: 'Hörster', center: [51.9650, 7.6425],
    polygon: [[51.9578, 7.6375], [51.9705, 7.6375], [51.9705, 7.6450], [51.9578, 7.6450]],
  },
  {
    id: 'hafen', name: 'Hafen', shortName: 'Hafen', center: [51.9512, 7.6422],
    polygon: [[51.9500, 7.6375], [51.9578, 7.6375], [51.9578, 7.6450], [51.9500, 7.6450]],
  },
];

export const territoryPlayers = [
  { id: 'explorer', name: 'Du', color: '#276b4b' },
  { id: 'mara', name: 'Mara', color: '#37976c' },
  { id: 'jonas', name: 'Jonas', color: '#4d88b4' },
  { id: 'aylin', name: 'Aylin', color: '#cf9450' },
] as const;
