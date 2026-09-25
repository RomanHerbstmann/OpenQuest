'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2, Compass, ListTodo, MapPin, Sparkles } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { usePlayer } from '@/context/PlayerContext';
import { trees } from '@/data/trees';

export default function MissionsPage() {
  const { progress } = usePlayer();
  const open = trees.filter((tree) => tree.status === 'unverified').slice(0, 5);
  const done = trees.filter((tree) => progress.completedMissions.includes(tree.id));
  return <main className="section-page">
    <div className="page-top"><Brand /><span className="pill pill-amber"><ListTodo size={14} /> MISSIONEN</span></div>
    <p className="eyebrow page-eyebrow">DEIN NÄCHSTES ABENTEUER</p><h1 className="page-heading">Kleine Checks.<br /><span>Große Wirkung.</span></h1><p className="page-description">Finde einen Baum auf der Karte. Jeder Check hilft später, offene Daten zu verbessern.</p>
    <div className="mission-banner"><span><Compass size={24} /></span><div><strong>Starte auf der Karte</strong><p>Wähle einen Baum und sieh dir seine Mission an.</p></div><Link href="/" aria-label="Zur Karte"><ArrowRight size={21} /></Link></div>
    <div className="section-label"><span>IN MÜNSTER</span><span>{trees.filter((tree) => tree.status === 'unverified').length} offene Checks</span></div>
    <div className="mission-list">{open.map((tree) => <article className="mission-list-card surface-card" key={tree.id}><span className="mission-list-icon">✦</span><div><span className="eyebrow">BAUM-CHECK · {tree.area.toUpperCase()}</span><h2>{tree.species} überprüfen</h2><p><MapPin size={13} /> {tree.area}</p></div><span className="pill pill-amber"><Sparkles size={13} /> +{tree.xpReward} XP</span></article>)}</div>
    <div className="section-label"><span>ABGESCHLOSSEN</span><span>{done.length} Missionen</span></div>
    {done.length ? <div className="mission-list">{done.map((tree) => <article className="mission-list-card surface-card" key={tree.id}><CheckCircle2 color="var(--primary)" /><div><h2>{tree.species} bestätigt</h2><p>{tree.area}</p></div></article>)}</div> : <div className="empty-card"><CheckCircle2 size={28} /><strong>Hier ist noch Platz für deine Funde.</strong><span>Missionen werden im nächsten Ausbauschritt aktiv.</span></div>}
    <p className="prototype-footnote">Demo-Daten · Der Missionsablauf folgt in Phase 3.</p>
  </main>;
}
