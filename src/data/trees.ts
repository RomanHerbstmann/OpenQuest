import type { Tree, TreeRarity, TreeStatus } from '@/types/tree';
import { species } from './species';

// Erfundenes Material für die Demo. Die Koordinaten liegen in Münsters Innenstadt.
const records: Array<[number, number, number, string, TreeStatus, TreeRarity, number]> = [
  [51.9621, 7.6258, 0, 'Schlossplatz', 'unverified', 'common', 2],
  [51.9634, 7.6219, 4, 'Schlossgarten', 'unverified', 'common', 1],
  [51.9650, 7.6230, 1, 'Botanischer Garten', 'verified', 'common', 5],
  [51.9641, 7.6278, 6, 'Kreuzschanze', 'unverified', 'uncommon', 0],
  [51.9616, 7.6301, 2, 'Promenade Nord', 'unverified', 'common', 3],
  [51.9592, 7.6269, 5, 'Aegidiistraße', 'verified', 'common', 4],
  [51.9580, 7.6226, 7, 'Aaseeweg', 'unverified', 'common', 1],
  [51.9561, 7.6188, 9, 'Aasee Nord', 'unverified', 'rare', 0],
  [51.9543, 7.6230, 3, 'Aasee Ost', 'unverified', 'common', 2],
  [51.9574, 7.6325, 0, 'Promenade Süd', 'verified', 'common', 6],
  [51.9606, 7.6354, 4, 'Domplatz', 'unverified', 'common', 1],
  [51.9618, 7.6392, 8, 'Hörsterplatz', 'unverified', 'uncommon', 0],
  [51.9586, 7.6411, 1, 'Hansaring', 'unverified', 'common', 2],
  [51.9560, 7.6381, 2, 'Bahnhofstraße', 'missing', 'common', 0],
  [51.9545, 7.6337, 6, 'Südpark', 'unverified', 'uncommon', 1],
  [51.9527, 7.6298, 7, 'Südpark', 'verified', 'common', 4],
  [51.9517, 7.6217, 5, 'Hafenweg', 'unverified', 'common', 1],
  [51.9664, 7.6352, 3, 'Kanalstraße', 'unverified', 'common', 0],
  [51.9670, 7.6298, 0, 'Kreuzviertel', 'unverified', 'common', 2],
  [51.9683, 7.6253, 4, 'Wienburgpark', 'verified', 'common', 3],
  [51.9598, 7.6193, 8, 'Schlossgraben', 'unverified', 'uncommon', 0],
  [51.9532, 7.6400, 9, 'Hafenpromenade', 'unverified', 'rare', 0],
  [51.9631, 7.6335, 3, 'Prinzipalmarkt', 'unverified', 'common', 1],
  [51.9551, 7.6159, 1, 'Aasee West', 'unverified', 'common', 2],
];

export const trees: Tree[] = records.map(([lat, lng, speciesIndex, area, status, rarity, verificationCount], index) => ({
  id: `ms-${String(index + 1).padStart(3, '0')}`,
  lat,
  lng,
  species: species[speciesIndex].name,
  speciesLatin: species[speciesIndex].latin,
  area,
  status,
  rarity,
  verificationCount,
  discovered: false,
  xpReward: rarity === 'rare' ? 40 : 25,
  lastChecked: status === 'verified' ? 'vor 5 Monaten' : verificationCount > 0 ? 'vor 3 Jahren' : undefined,
}));
