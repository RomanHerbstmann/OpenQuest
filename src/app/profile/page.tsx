'use client';

import { Award, CheckCircle2, CircleUserRound, Compass, Sparkles, Trees } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { usePlayer } from '@/context/PlayerContext';
import { levelProgress } from '@/lib/levels';

const badges = [
  { name: 'Erster Fund', detail: 'Einen Baum bestätigt', icon: '🌱', threshold: (trees: number, _species: number) => trees >= 1 },
  { name: 'Baumfreund', detail: 'Fünf Bäume bestätigt', icon: '🌳', threshold: (trees: number, _species: number) => trees >= 5 },
  { name: 'Entdecker', detail: 'Drei Arten gefunden', icon: '🧭', threshold: (_trees: number, species: number) => species >= 3 },
];

export default function ProfilePage() {
  const { progress, ready } = usePlayer();
  const xp = ready ? progress.xp : 0;
  const level = levelProgress(xp);
  return <main className="section-page">
    <div className="page-top"><Brand /><span className="pill pill-green"><CircleUserRound size={14} /> PROFIL</span></div>
    <p className="eyebrow page-eyebrow">DEIN ABENTEUER</p><h1 className="page-heading">Hallo, Explorer<span className="brand-dot">.</span></h1><p className="page-description">Jeder entdeckte Baum macht deine persönliche Karte ein Stück lebendiger.</p>
    <section className="profile-hero surface-card"><div className="avatar"><Compass size={39} /></div><div className="profile-hero-main"><p>DEIN SPIELERPROFIL</p><h2>Explorer</h2><span>Level {level.level} · {xp} XP gesammelt</span></div><span className="level-emblem">LVL<br /><strong>{level.level}</strong></span><div className="profile-progress"><div><span>Fortschritt zu Level {level.level + 1}</span><strong>{level.current} / {level.required} XP</strong></div><div className="progress-track"><span style={{ width: `${level.percent}%` }} /></div></div></section>
    <div className="section-label"><span>DEINE STATISTIK</span></div>
    <div className="stats-grid"><div className="stat-card surface-card"><Trees size={20} /><strong>{progress.discoveredTrees.length}</strong><span>Bäume entdeckt</span></div><div className="stat-card surface-card"><Sparkles size={20} /><strong>{progress.discoveredSpecies.length}</strong><span>Arten gesammelt</span></div><div className="stat-card surface-card"><CheckCircle2 size={20} /><strong>{progress.completedMissions.length}</strong><span>Missionen</span></div><div className="stat-card surface-card"><Award size={20} /><strong>{xp}</strong><span>XP gesammelt</span></div></div>
    <div className="section-label"><span>ABZEICHEN</span><span>{badges.filter((badge) => badge.threshold(progress.discoveredTrees.length, progress.discoveredSpecies.length)).length} / {badges.length}</span></div>
    <div className="badge-list">{badges.map((badge) => { const unlocked = badge.threshold(progress.discoveredTrees.length, progress.discoveredSpecies.length); return <div className={`badge-card surface-card ${unlocked ? '' : 'locked'}`} key={badge.name}><span>{badge.icon}</span><div><strong>{badge.name}</strong><small>{badge.detail}</small></div><em>{unlocked ? 'Erhalten' : 'Gesperrt'}</em></div>; })}</div>
    <p className="prototype-footnote">Dein Demo-Spielstand bleibt lokal in diesem Browser gespeichert.</p>
  </main>;
}
