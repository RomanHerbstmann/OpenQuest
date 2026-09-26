'use client';

import { Award, CheckCircle2, CircleUserRound, Compass, LogIn, LogOut, Sparkles, Trees, UserPlus, Wifi } from 'lucide-react';
import { Brand } from '@/components/ui/Brand';
import { usePlayer } from '@/context/PlayerContext';
import { useSession } from '@/context/SessionContext';
import { liveText } from '@/i18n/liveGame';
import { claimPoints } from '@/lib/quests';
import { levelProgress } from '@/lib/levels';

const badges = [
  { name: 'Erster Fund', detail: 'Einen Baum bestätigt', icon: '🌱', threshold: (trees: number, _species: number) => trees >= 1 },
  { name: 'Baumfreund', detail: 'Fünf Bäume bestätigt', icon: '🌳', threshold: (trees: number, _species: number) => trees >= 5 },
  { name: 'Entdecker', detail: 'Drei Arten gefunden', icon: '🧭', threshold: (_trees: number, species: number) => species >= 3 },
];

export default function ProfilePage() {
  const { progress, ready } = usePlayer();
  const { session, openAuth, logout, claims, progress: liveProgress } = useSession();
  const lt = liveText.de;
  const points = claims ? claimPoints(claims) : null;
  const xp = ready ? progress.xp : 0;
  const level = levelProgress(xp);
  return <main className="section-page design2-page profile-page">
    <div className="page-top"><Brand /><span className="d2-top-pill"><CircleUserRound size={15} /> PROFIL</span></div>
    <div className="profile-intro"><span className="d2-kicker">DEIN ABENTEUER</span><h1>Hallo, {session?.username ?? 'Explorer'}<span>.</span></h1><p>Jeder entdeckte Baum macht deine persönliche Karte ein Stück lebendiger.</p></div>
    <section className="profile-hero surface-card"><div className="avatar"><Compass size={39} /></div><div className="profile-hero-main"><p>DEIN SPIELERPROFIL</p><h2>Explorer</h2><span>Level {level.level} · {xp} XP gesammelt</span></div><span className="level-emblem">LVL<br /><strong>{level.level}</strong></span><div className="profile-progress"><div><span>Fortschritt zu Level {level.level + 1}</span><strong>{level.current} / {level.required} XP</strong></div><div className="progress-track"><span style={{ width: `${level.percent}%` }} /></div></div></section>
    <div className="section-label"><span>{lt.profile.section}</span><span>{session ? lt.mode.live : lt.mode.demo}</span></div>
    {session ? <section className="account-card surface-card live">
      <span className="account-icon"><Wifi size={22} /></span>
      <div><strong>{lt.profile.liveTitle(session.username)}</strong><span>{lt.profile.liveText}</span>{points && <small>{liveProgress ? `${lt.profile.level(liveProgress.level.level, liveProgress.totalPoints)} · ` : ''}{lt.claims.points(liveProgress?.totalPoints ?? points.confirmed, points.pending)}</small>}</div>
      <button type="button" className="account-button" onClick={logout}><LogOut size={16} /> {lt.auth.logout}</button>
    </section> : <section className="account-card surface-card">
      <span className="account-icon"><CircleUserRound size={22} /></span>
      <div><strong>{lt.profile.demoTitle}</strong><span>{lt.profile.demoText}</span></div>
      <div className="account-actions"><button type="button" className="account-button primary" onClick={() => openAuth('login')}><LogIn size={16} /> {lt.auth.submitLogin}</button><button type="button" className="account-button" onClick={() => openAuth('register')}><UserPlus size={16} /> {lt.profile.register}</button></div>
    </section>}
    <div className="section-label"><span>DEINE STATISTIK</span></div>
    <div className="stats-grid"><div className="stat-card surface-card"><Trees size={20} /><strong>{progress.discoveredTrees.length}</strong><span>Verschiedene Bäume</span></div><div className="stat-card surface-card"><Sparkles size={20} /><strong>{progress.unlockedCards.length}</strong><span>Karten aufgedeckt</span></div><div className="stat-card surface-card"><CheckCircle2 size={20} /><strong>{progress.scanEvents.length}</strong><span>Scans gespeichert</span></div><div className="stat-card surface-card"><Award size={20} /><strong>{xp}</strong><span>XP gesammelt</span></div></div>
    <div className="section-label"><span>ABZEICHEN</span><span>{badges.filter((badge) => badge.threshold(progress.discoveredTrees.length, progress.discoveredSpecies.length)).length} / {badges.length}</span></div>
    <div className="badge-list">{badges.map((badge) => { const unlocked = badge.threshold(progress.discoveredTrees.length, progress.discoveredSpecies.length); return <div className={`badge-card surface-card ${unlocked ? '' : 'locked'}`} key={badge.name}><span>{badge.icon}</span><div><strong>{badge.name}</strong><small>{badge.detail}</small></div><em>{unlocked ? 'Erhalten' : 'Gesperrt'}</em></div>; })}</div>
    <p className="prototype-footnote">Dein Demo-Spielstand bleibt lokal in diesem Browser gespeichert.</p>
  </main>;
}
