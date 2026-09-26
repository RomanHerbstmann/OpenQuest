'use client';

import Link from 'next/link';
import { useState } from 'react';
import { ArrowRight, Camera, CheckCircle2, Clock3, Loader2, RefreshCw, Search, Send, X, XCircle } from 'lucide-react';
import { useSession } from '@/context/SessionContext';
import { liveText } from '@/i18n/liveGame';
import { api, ApiError, type MyClaimDto } from '@/lib/api';
import { claimPoints, claimState, questKind } from '@/lib/quests';
import { genusName } from '@/lib/verificationText';

const t = liveText.de.claims;
const stateIcon = { active: Clock3, pending: Send, approved: CheckCircle2, rejected: XCircle, expired: Clock3, cancelled: X } as const;
const statePill = { active: 'pill-amber', pending: 'pill-amber', approved: 'pill-green', rejected: 'pill-neutral', expired: 'pill-neutral', cancelled: 'pill-neutral' } as const;

/** "Meine Quests" from GET /me/claims, newest first. */
export function MyQuests() {
  const { claims, claimsError, claimsLoading, refreshClaims } = useSession();
  const [cancelling, setCancelling] = useState<string | null>(null);
  const [error, setError] = useState('');
  const list = [...(claims ?? [])].sort((a, b) => Date.parse(b.claimedAt) - Date.parse(a.claimedAt));
  const points = claimPoints(list);

  const cancel = async (claim: MyClaimDto) => {
    setCancelling(claim.id);
    setError('');
    try { await api.cancelClaim(claim.id); await refreshClaims(); } catch (cause) { setError(cause instanceof ApiError ? cause.message : liveText.de.errors.fallback); } finally { setCancelling(null); }
  };

  return <>
    <div className="section-label"><span>{t.section}</span><span>{claims ? t.count(list.length) : ''}</span></div>
    <p className="my-quests-points">{t.points(points.confirmed, points.pending)}<button type="button" onClick={() => void refreshClaims()} disabled={claimsLoading} aria-label={t.reload}>{claimsLoading ? <Loader2 size={15} className="spin" /> : <RefreshCw size={15} />}</button></p>
    {(claimsError || error) && <p className="quest-error" role="alert">{error || claimsError}</p>}
    {!claims && claimsLoading && <div className="empty-card"><Loader2 size={24} className="spin" /><span>{t.loading}</span></div>}
    {claims && list.length === 0 && <div className="empty-card"><Camera size={28} /><strong>{t.empty}</strong><span>{t.emptyHint}</span><Link href="/" className="my-quests-map-link">{t.toMap} <ArrowRight size={15} /></Link></div>}
    {list.length > 0 && <div className="mission-list">{list.map((claim) => {
      const state = claimState(claim);
      const Icon = stateIcon[state];
      const kind = questKind(claim.quest.taskType);
      const genus = claim.quest.asset.attributes?.genus;
      const when = new Date(claim.submission?.submittedAt ?? claim.claimedAt).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
      return <article className={`mission-list-card surface-card my-quest ${state}`} key={claim.id}>
        <span className="mission-list-icon">{kind === 'verify' ? <Search size={20} /> : <Camera size={20} />}</span>
        <div>
          <span className="eyebrow">{(kind === 'verify' ? liveText.de.quest.verify : liveText.de.quest.photo).toUpperCase()} · {when}</span>
          <h2>{claim.quest.title}</h2>
          <p><Icon size={13} /> {t.status[state]}{genus ? ` · ${genusName(genus)}` : ''}</p>
          {claim.submission?.rejectionReason && <p className="my-quest-reason">{t.reason(claim.submission.rejectionReason)}</p>}
        </div>
        <div className="my-quest-side">
          <span className={`pill ${statePill[state]}`}>{state === 'approved' ? '+' : ''}{claim.quest.rewardPoints} XP</span>
          {state === 'active' && <button type="button" className="my-quest-cancel" onClick={() => void cancel(claim)} disabled={cancelling !== null}>{cancelling === claim.id ? <Loader2 size={13} className="spin" /> : <X size={13} />} {t.cancel}</button>}
        </div>
      </article>;
    })}</div>}
  </>;
}
