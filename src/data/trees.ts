import type { Tree, TreeRarity, TreeStatus } from '@/types/tree';
import { species } from './species';

// Standorte und Gattungen: Stadt Münster, Digitales Baumkataster (WFS, 26.09.2026), dl-de/by-2.0.
// Je ein eindeutiger Katasterpunkt derselben Gattung nahe der früheren Demo-Position.
// Straßennamen via str_schl aus dem Münsteraner Straßen-WFS. Art, Status, Seltenheit und Checks bleiben Demo-Inhalte.
const records: Array<[number, number, number, string, TreeStatus, TreeRarity, number, string, string]> = [
  [51.964195599488356, 7.627542790270506, 0, 'Neubrückenstraße', 'unverified', 'common', 2, 'Quercus', '04905'],
  [51.963940548417604, 7.622272633650477, 4, 'Überwasserkirchplatz', 'unverified', 'common', 1, 'Tilia', '06690'],
  [51.96703893952324, 7.623893399685958, 1, 'Promenade', 'verified', 'common', 5, 'Fagus', '05460'],
  [51.96421284634805, 7.627307621901264, 6, 'Neubrückenstraße', 'unverified', 'uncommon', 0, 'Platanus', '04905'],
  [51.96249022076807, 7.630985933873969, 2, 'Julius-Voos-Gasse', 'unverified', 'common', 3, 'Acer', '03574'],
  [51.95885426328407, 7.625211619455495, 5, 'Königsstraße', 'verified', 'common', 4, 'Aesculus', '03920'],
  [51.95697073452936, 7.623269127171328, 7, 'Promenade', 'unverified', 'common', 1, 'Betula', '05460'],
  [51.95788875439352, 7.62418212961897, 9, 'Schützenstraße', 'unverified', 'rare', 0, 'Ginkgo', '06070'],
  [51.95356160804182, 7.622985717270325, 3, 'Hermannstraße', 'unverified', 'common', 2, 'Acer', '02965'],
  [51.959392679217984, 7.633455285150785, 0, 'Von-Vincke-Straße', 'verified', 'common', 6, 'Quercus', '06955'],
  [51.96057459807039, 7.635710314319631, 4, 'Friedrichstraße', 'unverified', 'common', 1, 'Tilia', '02190'],
  [51.964217983523106, 7.640413554851073, 8, 'Stolbergstraße', 'unverified', 'uncommon', 0, 'Sorbus', '06425'],
  [51.95910123431089, 7.641658178772776, 1, 'Zumsandeplatz', 'unverified', 'common', 2, 'Fagus', '07450'],
  [51.95640066206808, 7.638535923220529, 2, 'Bremer Platz', 'missing', 'common', 0, 'Acer', '01130'],
  [51.95388320864603, 7.632440351754711, 6, 'Hafenstraße', 'unverified', 'uncommon', 1, 'Platanus', '02665'],
  [51.95132801501259, 7.631513965458322, 7, 'Theißingstraße', 'verified', 'common', 4, 'Betula', '06565'],
  [51.952113360578835, 7.621470023037618, 5, 'Vogel-von-Falkenstein-Straße', 'unverified', 'common', 1, 'Aesculus', '06840'],
  [51.966213772302204, 7.634784428133277, 3, 'Hörsterplatz', 'unverified', 'common', 0, 'Acer', '03075'],
  [51.967499346627626, 7.62963849323038, 0, 'Coerdeplatz', 'unverified', 'common', 2, 'Quercus', '01355'],
  [51.96727587883843, 7.625788891446239, 4, 'Promenade', 'verified', 'common', 3, 'Tilia', '05460'],
  [51.961934955891465, 7.621535298592076, 8, 'Bispinghof', 'unverified', 'uncommon', 0, 'Sorbus', '00990'],
  [51.95255941788509, 7.638379732026096, 9, 'Soester Straße', 'unverified', 'rare', 0, 'Ginkgo', '06216'],
  [51.963222161855285, 7.633325990549412, 3, 'Wevelinghofergasse', 'unverified', 'common', 1, 'Acer', '07195'],
  [51.95474636587724, 7.614770762709126, 1, 'Bismarckallee', 'unverified', 'common', 2, 'Fagus', '00985'],
];

const demoTrees: Tree[] = records.map(([lat, lng, speciesIndex, area, status, rarity, verificationCount, genus, streetKey], index) => ({
  id: `ms-${String(index + 1).padStart(3, '0')}`,
  lat,
  lng,
  species: species[speciesIndex].name,
  speciesLatin: species[speciesIndex].latin,
  area,
  inventory: { genus, streetKey },
  status,
  rarity,
  verificationCount,
  discovered: false,
  xpReward: rarity === 'rare' ? 40 : 25,
  lastChecked: status === 'verified' ? 'vor 5 Monaten' : verificationCount > 0 ? 'vor 3 Jahren' : undefined,
}));

// Bühnenobjekt für die Präsentation; kein Eintrag aus einem Stadtbaumkataster.
// Kartenposition: Gebäude Hafenweg 7 gemäß OpenStreetMap (way 299383160).
export const presentationTree: Tree = {
  id: 'presentation-festtanne',
  lat: 51.952264,
  lng: 7.639237,
  species: 'Festtanne',
  speciesLatin: 'Art nicht bestimmt',
  area: 'Hafenweg 7, 48155 Münster',
  status: 'new',
  rarity: 'rare',
  verificationCount: 0,
  discovered: false,
  xpReward: 25,
  presentation: true,
};

export const trees: Tree[] = [...demoTrees, presentationTree];
