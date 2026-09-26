import { species } from './species';

export type SpeciesName = (typeof species)[number]['name'];
export type LexiconEntry = {
  name: SpeciesName;
  slug: string;
  group: 'Laubbaum' | 'Nadelbaum' | 'Sonderfund';
  signature: string;
  introduction: string;
  leaf: string;
  bark: string;
  fruit: string;
  season: string;
  habitat: string;
  accent: string;
};

// Editorial prototype text about species, kept separate from records for individual trees.
// The stage prop and genus-level entries deliberately avoid a precise species claim.
export const lexiconEntries: LexiconEntry[] = [
  { name: 'Stieleiche', slug: 'stieleiche', group: 'Laubbaum', signature: 'Die mit den Eicheln am langen Stiel', introduction: 'Ein mächtiger Laubbaum mit breiter Krone. Eichen bieten vielen Tieren Nahrung und Unterschlupf.', leaf: 'Rund gelappte Blätter mit kurzem Stiel.', bark: 'Im Alter graubraun und tief gefurcht.', fruit: 'Eicheln sitzen an langen Stielen.', season: 'Eicheln im Herbst', habitat: 'Parks, Alleen und lichte Wälder', accent: '#b8dc73' },
  { name: 'Rotbuche', slug: 'rotbuche', group: 'Laubbaum', signature: 'Glatte Rinde, dichtes Blätterdach', introduction: 'Die Rotbuche ist an ihrem glatten Stamm und ihrer dichten Krone gut zu erkennen.', leaf: 'Ovale Blätter mit leicht gewelltem Rand.', bark: 'Auch bei älteren Bäumen oft glatt und silbergrau.', fruit: 'Bucheckern liegen in stacheligen Fruchthüllen.', season: 'Kupferfarbenes Laub im Herbst', habitat: 'Wälder und größere Grünanlagen', accent: '#caad84' },
  { name: 'Bergahorn', slug: 'bergahorn', group: 'Laubbaum', signature: 'Große Blätter und Flügelfrüchte', introduction: 'Der Bergahorn zeigt handförmige Blätter und die typischen paarigen Ahornfrüchte.', leaf: 'Meist fünf grob gezähnte Blattlappen.', bark: 'Bei älteren Bäumen löst sie sich stellenweise in Platten.', fruit: 'Zwei geflügelte Teilfrüchte bilden ein Paar.', season: 'Flügelfrüchte im Spätsommer', habitat: 'Parks, Straßen und Wälder', accent: '#a8d5a4' },
  { name: 'Spitzahorn', slug: 'spitzahorn', group: 'Laubbaum', signature: 'Ahornblatt mit markanten Spitzen', introduction: 'Seine spitz ausgezogenen Blattlappen machen diesen Ahorn besonders einprägsam.', leaf: 'Handförmig mit deutlich spitzen Lappen.', bark: 'Graubraun und im Alter längsrissig.', fruit: 'Breit gespreizte, paarige Flügelfrüchte.', season: 'Blüten vor dem Laubaustrieb', habitat: 'Alleen, Parks und Stadtstraßen', accent: '#d5d978' },
  { name: 'Winterlinde', slug: 'winterlinde', group: 'Laubbaum', signature: 'Herzblätter und duftende Blüten', introduction: 'Die Winterlinde fällt durch kleine herzförmige Blätter und duftende Sommerblüten auf.', leaf: 'Klein und herzförmig, mit gesägtem Rand.', bark: 'Jung glatt, später längs gefurcht.', fruit: 'Kleine Nüsschen hängen an einem Flugblatt.', season: 'Blüte im Sommer', habitat: 'Plätze, Alleen und Parks', accent: '#9fe2b5' },
  { name: 'Rosskastanie', slug: 'rosskastanie', group: 'Laubbaum', signature: 'Fächerblätter und Kastanien', introduction: 'Ihre großen handförmigen Blätter und auffälligen Blütenkerzen prägen viele Stadtplätze.', leaf: 'Fünf bis sieben einzelne Blättchen bilden einen Fächer.', bark: 'Graubraun, später schuppig und rissig.', fruit: 'Braune Kastanien in stacheligen Hüllen.', season: 'Blütenkerzen im Frühjahr', habitat: 'Stadtplätze, Parks und Alleen', accent: '#deb8a4' },
  { name: 'Platane', slug: 'platane', group: 'Laubbaum', signature: 'Die Borke mit Tarnmuster', introduction: 'An der gefleckten, sich ablösenden Borke lässt sich eine Platane oft schon von Weitem erkennen.', leaf: 'Groß, handförmig und ahornähnlich.', bark: 'Schält sich in unterschiedlich gefärbten Platten ab.', fruit: 'Runde Fruchtkugeln hängen oft lange am Baum.', season: 'Fruchtkugeln bis in den Winter', habitat: 'Breite Straßen und Stadtplätze', accent: '#a8c7d2' },
  { name: 'Birke', slug: 'birke', group: 'Laubbaum', signature: 'Weißer Stamm, leichte Krone', introduction: 'Die helle Rinde und die feinen, oft hängenden Zweige geben der Birke ihre unverwechselbare Silhouette.', leaf: 'Dreieckig bis rautenförmig und gesägt.', bark: 'Weiß mit dunklen Partien und Flecken.', fruit: 'Kleine geflügelte Samen aus Kätzchen.', season: 'Kätzchen im Frühjahr', habitat: 'Offene Flächen, Parks und Waldränder', accent: '#c2e7da' },
  { name: 'Eberesche', slug: 'eberesche', group: 'Laubbaum', signature: 'Gefiederte Blätter, rote Früchte', introduction: 'Weiße Blüten und leuchtende Fruchtstände machen die Eberesche über mehrere Jahreszeiten interessant.', leaf: 'Viele kleine Fiederblättchen an einer Mittelachse.', bark: 'Glatt und grau, später leicht rissig.', fruit: 'Orange-rote Früchte in dichten Büscheln.', season: 'Früchte ab Spätsommer', habitat: 'Straßenränder, Parks und lichte Wälder', accent: '#edaa83' },
  { name: 'Ginkgo', slug: 'ginkgo', group: 'Laubbaum', signature: 'Das unverwechselbare Fächerblatt', introduction: 'Der Ginkgo ist mit keinem heimischen Laubbaum zu verwechseln: Sein Blatt ist wie ein kleiner Fächer geformt.', leaf: 'Fächerförmig, oft mit einer Einkerbung.', bark: 'Graubraun und bei alten Bäumen gefurcht.', fruit: 'Weibliche Bäume können fleischige Samen bilden.', season: 'Goldgelbe Blätter im Herbst', habitat: 'Parks, Gärten und Stadtstraßen', accent: '#e6cf79' },
  { name: 'Kiefer', slug: 'kiefer', group: 'Nadelbaum', signature: 'Nadeln und holzige Zapfen', introduction: '„Kiefer“ bezeichnet hier eine Baumgruppe; die genaue Art ist für diesen Demo-Fund noch nicht bestimmt.', leaf: 'Nadeln stehen je nach Art in Bündeln.', bark: 'Schuppig; Farbe und Struktur variieren nach Art.', fruit: 'Holzige Zapfen tragen die Samen.', season: 'Ganzjährig grün', habitat: 'Je nach Art Wälder, Parks und Gärten', accent: '#8dd7a2' },
  { name: 'Zierkirsche', slug: 'zierkirsche', group: 'Laubbaum', signature: 'Ein Blütenmoment im Frühling', introduction: 'Zierkirschen umfassen verschiedene Arten und Sorten. Der genaue botanische Name dieses Demo-Funds ist offen.', leaf: 'Ovale, meist gesägte Blätter.', bark: 'Häufig mit waagerechten Korkporen.', fruit: 'Früchte unterscheiden sich je nach Art und Sorte.', season: 'Blüte meist im Frühjahr', habitat: 'Gärten, Parks und Stadtplätze', accent: '#efb6c5' },
  { name: 'Festtanne', slug: 'festtanne', group: 'Sonderfund', signature: 'Die legendäre Bühnenkarte', introduction: 'Die Festtanne ist ein Präsentationsobjekt für die Scan-Demo. Ihre botanische Art wurde nicht bestimmt.', leaf: 'Nadeln sind im Bühnenaufbau sichtbar.', bark: 'Für dieses Objekt nicht dokumentiert.', fruit: 'Für dieses Objekt nicht dokumentiert.', season: 'Sonderfund · MS Hack 2026', habitat: 'Hafenweg 7, Münster · Bühne', accent: '#efcf83' },
];

export function lexiconForSpecies(name: string) {
  return lexiconEntries.find((entry) => entry.name === name);
}

export function lexiconForSlug(slug: string) {
  return lexiconEntries.find((entry) => entry.slug === slug);
}
