import Link from 'next/link';
import { notFound } from 'next/navigation';
import { ArrowLeft, ArrowRight, BookOpenText, Flower2, Leaf, MapPin, ScanEye, TreeDeciduous } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { LexiconArt } from '@/components/collection/LexiconArt';
import { lexiconEntries, lexiconForSlug } from '@/data/lexicon';
import { species } from '@/data/species';

export function generateStaticParams() { return lexiconEntries.map((entry) => ({ slug: entry.slug })); }

export default async function LexiconEntryPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const entry = lexiconForSlug(slug);
  if (!entry) notFound();
  const speciesEntry = species.find((item) => item.name === entry.name);
  const currentIndex = lexiconEntries.findIndex((item) => item.slug === slug);
  const next = lexiconEntries[(currentIndex + 1) % lexiconEntries.length];

  return <main className="section-page lexicon-detail-page design2-page" style={{ '--entry-accent': entry.accent } as React.CSSProperties}>
    <div className="page-top"><Brand /><span className="d2-top-pill"><BookOpenText size={15} /> NATURLEXIKON</span></div>
    <Link href="/lexicon" className="lexicon-back"><ArrowLeft size={18} /> Alle Arten</Link>
    <section className="lexicon-detail-hero">
      <div className="lexicon-detail-copy"><span className="d2-kicker">ARTENPORTRÄT / {String(currentIndex + 1).padStart(2, '0')}</span><h1>{entry.name}</h1><em>{speciesEntry?.latin}</em><p>{entry.introduction}</p><span className="lexicon-detail-group"><Leaf size={16} /> {entry.group}</span></div>
      <LexiconArt name={entry.name} emoji={speciesEntry?.emoji} />
    </section>
    <div className="d2-section-heading"><div><span className="d2-kicker">DRAUSSEN ERKENNEN</span><h2>Drei gute Hinweise</h2></div></div>
    <div className="lexicon-facts">
      <article><span><Leaf size={21} /></span><small>01 / BLATT ODER NADEL</small><h3>Blattbild</h3><p>{entry.leaf}</p></article>
      <article><span><TreeDeciduous size={21} /></span><small>02 / STAMM</small><h3>Rinde</h3><p>{entry.bark}</p></article>
      <article><span><Flower2 size={21} /></span><small>03 / FRUCHT</small><h3>Frucht & Samen</h3><p>{entry.fruit}</p></article>
    </div>
    <div className="lexicon-detail-lower">
      <section className="lexicon-insight"><div><span><ScanEye size={20} /></span><small>MERK DIR DAS</small></div><h2>{entry.signature}</h2><p>Schau dir mehrere Merkmale an. Einzelne Bäume können je nach Alter, Standort und Jahreszeit anders aussehen.</p></section>
      <section className="lexicon-context"><div><span>JAHRESZEIT</span><strong>{entry.season}</strong></div><div><span>WO DU SIE FINDEST</span><strong>{entry.habitat}</strong></div><div><span>DATENHINWEIS</span><strong>Allgemeines Artenwissen, kein Messwert eines einzelnen Baums.</strong></div></section>
    </div>
    <div className="lexicon-next-row"><Link href="/" className="lexicon-map-link"><MapPin size={18} /> Bäume auf der Karte entdecken</Link><Link href={`/lexicon/${next.slug}`} className="lexicon-next-link">Nächste Art: {next.name} <ArrowRight size={17} /></Link></div>
    <p className="lexicon-editorial-note">Redaktioneller Prototyp · Einzelne Baumstandorte, Alter, Höhe und Zustand müssen gesondert erfasst und geprüft werden.</p>
  </main>;
}
