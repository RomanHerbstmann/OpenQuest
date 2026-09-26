'use client';

import Image from 'next/image';
import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import Link from 'next/link';
import { AlertTriangle, Camera, Check, CircleCheck, CircleX, Clock3, ImagePlus, ListTodo, Loader2, RotateCcw, ScanLine, Send, X } from 'lucide-react';
import { usePlayer } from '@/context/PlayerContext';
import { cardArtBySpecies } from '@/data/cardArt';
import { species } from '@/data/species';
import { trees } from '@/data/trees';
import { liveText } from '@/i18n/liveGame';
import { api, ApiError, type SubmitResult } from '@/lib/api';
import { PhotoCancelled, PhotoPermissionDenied, pickPhoto, takePhoto } from '@/lib/camera';
import { loadObservations, recordScanObservation, saveObservations } from '@/lib/observations';
import { isNativeApp } from '@/lib/platform';
import { recognizeTreeForPrototype, type ScanResult } from '@/lib/treeScan';
import { formatPercent, genusLabel, reasonMessage, verdictText, type SpeciesName } from '@/lib/verificationText';
import type { Observation } from '@/types/observation';
import type { ScanEvent } from '@/types/player';
import type { Tree } from '@/types/tree';

type Stage = 'camera' | 'scanning' | 'revealing' | 'result' | 'saved';
type VerifiedScan = Extract<ScanResult, { source: 'verified' }>;
/** Submission of a live quest to the API after the photo check. */
type Submission =
  | { state: 'idle' }
  | { state: 'sending' }
  | { state: 'done'; result: SubmitResult }
  | { state: 'error'; message: string; canRetry: boolean };
const lt = liveText.de;
// Errors after which submitting the same claim again cannot succeed.
const FINAL_SUBMIT_ERRORS = new Set(['claim_expired', 'claim_not_active', 'already_submitted', 'claim_not_found', 'session_expired']);

export function ScanModal({ tree, onClose, onSubmitted, onCancelClaim }: {
  tree?: Tree | null;
  onClose: () => void;
  /** Live quest: called after the API accepted the submission. */
  onSubmitted?: () => void;
  /** Live quest: cancels the claim (offered after a rejected photo). */
  onCancelClaim?: () => Promise<void>;
}) {
  const quest = tree?.quest ?? null;
  const [submission, setSubmission] = useState<Submission>({ state: 'idle' });
  const [cancelling, setCancelling] = useState(false);
  const { progress, ready, recordScan, recordDiscovery } = usePlayer();
  // Inside the phone app the native camera replaces the browser camera (getUserMedia): no live preview in the page, the phone's own camera app takes the photo.
  const native = isNativeApp();
  const nativeOpened = useRef(false);
  const [stage, setStage] = useState<Stage>('camera');
  // Opened without a tree (Baumbuch): the player picks the tree point the find belongs to, or scans freely.
  const [selectedTreeId, setSelectedTreeId] = useState(tree?.id ?? '');
  const [observationSaved, setObservationSaved] = useState(false);
  const [cameraReady, setCameraReady] = useState(false);
  const [cameraError, setCameraError] = useState('');
  const [scanError, setScanError] = useState('');
  const [photoUrl, setPhotoUrl] = useState<string | null>(null);
  const [selectedSpecies, setSelectedSpecies] = useState<SpeciesName>('Birke');
  const [earnedXp, setEarnedXp] = useState(0);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const targetTree = tree ?? trees.find((candidate) => candidate.id === selectedTreeId) ?? null;
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const captureInputRef = useRef<HTMLInputElement>(null);
  const photoUrlRef = useRef<string | null>(null);
  const scanRunRef = useRef(0);
  const revealTimerRef = useRef<number | null>(null);
  const savedRef = useRef(false);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeRef.current?.focus();
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      previousFocus?.focus();
      document.removeEventListener('keydown', onKey);
      scanRunRef.current += 1;
      if (revealTimerRef.current !== null) window.clearTimeout(revealTimerRef.current);
      if (photoUrlRef.current) URL.revokeObjectURL(photoUrlRef.current);
    };
  }, [onClose]);

  useEffect(() => {
    if (stage !== 'camera' || native) return;
    let active = true;
    setCameraReady(false);
    setCameraError('');
    const timeout = window.setTimeout(() => {
      if (active) setCameraError('Die Kamera braucht zu lange. Du kannst ein Foto auswählen.');
    }, 10000);

    async function openCamera() {
      if (!navigator.mediaDevices?.getUserMedia) {
        window.clearTimeout(timeout);
        setCameraError('Die Kamera ist hier nicht verfügbar. Du kannst ein Foto auswählen.');
        return;
      }
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: { ideal: 'environment' } } });
        if (!active) { stream.getTracks().forEach((track) => track.stop()); return; }
        streamRef.current = stream;
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
          await videoRef.current.play();
        }
        if (active) {
          window.clearTimeout(timeout);
          setCameraError('');
          setCameraReady(Boolean(videoRef.current?.videoWidth));
        }
      } catch {
        if (active) {
          window.clearTimeout(timeout);
          streamRef.current?.getTracks().forEach((track) => track.stop());
          streamRef.current = null;
          setCameraReady(false);
          setCameraError('Kamera nicht verfügbar oder Zugriff abgelehnt. Du kannst ein Foto auswählen.');
        }
      }
    }
    void openCamera();

    return () => {
      active = false;
      window.clearTimeout(timeout);
      streamRef.current?.getTracks().forEach((track) => track.stop());
      streamRef.current = null;
      if (videoRef.current) videoRef.current.srcObject = null;
    };
  }, [stage]);

  const processImage = async (image: Blob, capturedAt?: Date) => {
    if (!image.type.startsWith('image/') || image.size === 0 || image.size > 15 * 1024 * 1024) {
      setScanError('Bitte wähle ein Bild mit höchstens 15 MB aus.');
      return;
    }
    scanRunRef.current += 1;
    if (revealTimerRef.current !== null) window.clearTimeout(revealTimerRef.current);
    const run = scanRunRef.current;
    if (photoUrlRef.current) URL.revokeObjectURL(photoUrlRef.current);
    const url = URL.createObjectURL(image);
    photoUrlRef.current = url;
    setPhotoUrl(url);
    setScanError('');
    setScanResult(null);
    setStage('scanning');
    try {
      const result = await recognizeTreeForPrototype(image, targetTree?.lexiconSpecies ?? targetTree?.species, { tree: targetTree, capturedAt });
      if (scanRunRef.current !== run) return;
      setScanResult(result);
      setSubmission({ state: 'idle' });
      // Live quest: approve and review go to the API right away; reject submits nothing and keeps the claim.
      if (quest && result.source === 'verified' && result.verification.verdict !== 'reject') void submitQuest(result);
      const matched = species.find((item) => item.name === result.species);
      const fallback = species.find((item) => item.name === targetTree?.species)?.name ?? 'Birke';
      setSelectedSpecies(matched?.name ?? fallback);
      // Nothing to reveal for a live quest (XP come after moderation), a rejected photo or a genus without card: show the verdict right away.
      if (quest || (result.source === 'verified' && (result.verification.verdict === 'reject' || (!result.species && !targetTree)))) {
        setStage('result');
        return;
      }
      setStage('revealing');
      const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      revealTimerRef.current = window.setTimeout(() => {
        if (scanRunRef.current === run) setStage('result');
        revealTimerRef.current = null;
      }, reducedMotion ? 80 : 1500);
    } catch (error) {
      if (scanRunRef.current !== run) return;
      setScanError(error instanceof Error ? error.message : 'Der Scan konnte nicht abgeschlossen werden.');
      setStage('camera');
    }
  };

  const submitQuest = async (scan: VerifiedScan) => {
    if (!quest?.claim) return;
    if (!scan.position) { setSubmission({ state: 'error', message: lt.scan.noPosition, canRetry: false }); return; }
    const value = scan.genus;
    if (quest.kind === 'verify' && !value) { setSubmission({ state: 'error', message: lt.scan.noGenus, canRetry: false }); return; }
    setSubmission({ state: 'sending' });
    try {
      const result = await api.submitClaim(quest.claim.id, {
        lat: scan.position.lat,
        lon: scan.position.lng,
        payload: quest.kind === 'verify' ? { value: value!, note: `photo check ${scan.verification.verdict}` } : {},
        photo: scan.photo,
      });
      setSubmission({ state: 'done', result });
      // Only a local card for the collection; XP come from the moderation.
      if (scan.verification.verdict === 'approve' && scan.species && ready) collectCard(scan.species, 'quest');
      setStage('saved');
      onSubmitted?.();
    } catch (error) {
      const code = error instanceof ApiError ? error.code : '';
      setSubmission({ state: 'error', message: error instanceof ApiError ? error.message : lt.errors.fallback, canRetry: !FINAL_SUBMIT_ERRORS.has(code) });
    }
  };

  const cancelClaim = async () => {
    if (!onCancelClaim) return;
    setCancelling(true);
    try { await onCancelClaim(); onClose(); } catch (error) { setSubmission({ state: 'error', message: error instanceof ApiError ? error.message : lt.errors.fallback, canRetry: false }); } finally { setCancelling(false); }
  };

  const capturePhoto = () => {
    const video = videoRef.current;
    if (!video?.videoWidth || !video.videoHeight) return;
    const canvas = document.createElement('canvas');
    const scale = Math.min(1, 1280 / video.videoWidth);
    canvas.width = Math.round(video.videoWidth * scale);
    canvas.height = Math.round(video.videoHeight * scale);
    const context = canvas.getContext('2d');
    if (!context) { setScanError('Die Aufnahme ist fehlgeschlagen. Bitte wähle ein Foto aus.'); return; }
    context.drawImage(video, 0, 0, canvas.width, canvas.height);
    canvas.toBlob((blob) => {
      if (blob) void processImage(blob, new Date());
      else setScanError('Die Aufnahme ist fehlgeschlagen. Bitte versuche es erneut.');
    }, 'image/jpeg', 0.85);
  };

  // A photo just taken counts as taken now; the library gives no file date, so the verifier cannot flag old pictures there.
  const nativePhoto = async (choose: () => Promise<Blob>, capturedAt?: Date) => {
    setScanError('');
    try {
      await processImage(await choose(), capturedAt);
    } catch (error) {
      if (error instanceof PhotoCancelled) return;
      setScanError(error instanceof PhotoPermissionDenied
        ? 'Der Zugriff auf Kamera oder Fotos ist nicht erlaubt. Bitte erlaube ihn in den Einstellungen deines Handys.'
        : 'Das Foto konnte nicht aufgenommen werden. Bitte versuche es erneut.');
    }
  };

  // Opening the scan opens the camera right away, once. After cancelling, the buttons below are still there.
  useEffect(() => {
    if (native && stage === 'camera' && !nativeOpened.current) {
      nativeOpened.current = true;
      void nativePhoto(takePhoto, new Date());
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [native, stage]);

  const onPhotoSelected = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    // Gallery pictures keep their file date, which lets the verifier flag old photos.
    if (file) void processImage(file, new Date(file.lastModified || Date.now()));
    event.target.value = '';
  };

  const retry = () => {
    scanRunRef.current += 1;
    if (revealTimerRef.current !== null) window.clearTimeout(revealTimerRef.current);
    revealTimerRef.current = null;
    if (photoUrlRef.current) URL.revokeObjectURL(photoUrlRef.current);
    photoUrlRef.current = null;
    setPhotoUrl(null);
    setScanError('');
    setScanResult(null);
    setSubmission({ state: 'idle' });
    savedRef.current = false;
    setObservationSaved(false);
    setStage('camera');
  };

  /** Adds one scan event: every find counts separately, the card image unlocks only once (see playerProgress). */
  const collectCard = (cardSpecies: string, source: ScanEvent['source'], xpReward = 0, observationId: string | null = null) => {
    recordScan({
      id: crypto.randomUUID(), treeId: targetTree?.id ?? null, assetId: targetTree?.assetId ?? null,
      species: cardSpecies, scannedAt: new Date().toISOString(), observationId, source,
    }, xpReward);
  };

  // Demo fallback (no photo check available): local find with a pending species suggestion for the admin panel.
  const saveCard = () => {
    if (!ready || savedRef.current) return;
    savedRef.current = true;
    const scannedAt = new Date().toISOString();
    const observationId = targetTree && !targetTree.presentation ? crypto.randomUUID() : null;
    if (targetTree && observationId) {
      const observation: Observation = {
        id: observationId, treeId: targetTree.id, action: 'species_suggestion', observedAt: scannedAt,
        lat: targetTree.lat, lng: targetTree.lng, accuracyMeters: null, suggestedSpecies: selectedSpecies,
        reviewStatus: 'pending', reviewNote: 'Demo-Scan: Baumposition ausgewählt, Foto nicht gespeichert und kein GPS-Abgleich.',
        source: 'local', userId: 'explorer',
      };
      try { saveObservations([...loadObservations(), observation]); setObservationSaved(true); }
      catch { setObservationSaved(false); }
    }
    const reward = targetTree?.presentation && selectedSpecies === targetTree.species && !progress.unlockedCards.includes(selectedSpecies) ? targetTree.xpReward : 0;
    collectCard(selectedSpecies, 'demo', reward, observationId);
    setEarnedXp(reward);
    setStage('saved');
  };

  // Verified scans: XP and card only on approve, review is queued, reject gives nothing.
  const observationFor = (scan: VerifiedScan, reviewStatus: 'verified' | 'needs_review') => ({
    treeId: targetTree?.id ?? null,
    lat: scan.position?.lat ?? targetTree?.lat ?? 0,
    lng: scan.position?.lng ?? targetTree?.lng ?? 0,
    accuracyMeters: scan.position?.accuracyMeters ?? null,
    suggestedSpecies: scan.species ?? scan.genus,
    reviewStatus,
    reviewNote: `Photo check: ${scan.verification.verdict} (${scan.verification.reasons.map((reason) => reason.code).join(', ') || 'no reasons'})`,
  });

  const confirmVerified = (scan: VerifiedScan) => {
    if (!ready || savedRef.current) return;
    savedRef.current = true;
    const cardSpecies = scan.species ?? (targetTree ? selectedSpecies : null);
    let reward = 0;
    if (targetTree) {
      reward = progress.completedMissions.includes(targetTree.id) ? 0 : targetTree.xpReward;
      recordDiscovery(targetTree.id, cardSpecies ?? targetTree.species, targetTree.xpReward);
      recordScanObservation(observationFor(scan, 'verified'));
    }
    if (cardSpecies) { collectCard(cardSpecies, 'verified'); setSelectedSpecies(cardSpecies); }
    setEarnedXp(reward);
    setStage('saved');
  };

  const queueForReview = (scan: VerifiedScan) => {
    recordScanObservation(observationFor(scan, 'needs_review'));
    setEarnedXp(0);
    setStage('saved');
  };

  const cardArt = cardArtBySpecies[selectedSpecies];
  const alreadyCollected = progress.unlockedCards.includes(selectedSpecies);
  const verified = scanResult?.source === 'verified' ? scanResult : null;
  const verdict = verified?.verification.verdict ?? null;
  const photoOnly = Boolean(quest) || (verified !== null && (verdict === 'reject' || (!verified.species && !targetTree)));
  const demoOnly = Boolean(targetTree?.presentation);
  const title = quest && stage === 'saved' ? lt.scan.submittedTitle : quest && submission.state === 'error' ? lt.scan.submitFailed : stage === 'saved'
    ? (verdict === 'review' ? 'Fund vorgemerkt' : cardArt ? 'Karte gesammelt!' : 'Fund gespeichert!')
    : stage === 'result'
      ? (verdict === 'reject' ? 'Foto abgelehnt' : verdict === 'review' ? 'Fund wird geprüft' : cardArt ? 'Deine Sammelkarte' : 'Dein Baumfund')
      : stage === 'revealing' ? 'Deine Karte entsteht' : 'Baum scannen';

  return <div className="scan-overlay" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <section className="scan-modal" role="dialog" aria-modal="true" aria-labelledby="scan-title">
      <header className="scan-header"><div><p className="eyebrow">OPENQUEST / ENTDECKEN</p><h2 id="scan-title">{title}</h2></div><button ref={closeRef} type="button" className="scan-close" onClick={onClose} aria-label="Scan schließen"><X size={21} /></button></header>
      <div className="scan-progress-steps" aria-label="Scan-Fortschritt"><span className="active">01 AUFNEHMEN</span><span className={stage === 'camera' ? '' : 'active'}>02 ERKENNEN</span><span className={stage === 'result' || stage === 'saved' ? 'active' : ''}>03 SAMMELN</span></div>

      {stage === 'camera' && native && <>
        <div className="scan-viewfinder">
          <div className="scan-corners" aria-hidden="true" />
          <div className="scan-camera-message"><Camera size={28} /><span>Die Kamera deines Handys öffnet sich.</span></div>
        </div>
        {!tree && <label className="scan-target-label" htmlFor="scan-target">Welchen Baumpunkt scannst du?</label>}
        {!tree && <select id="scan-target" className="scan-species-select scan-target-select" value={selectedTreeId} onChange={(event) => setSelectedTreeId(event.target.value)}><option value="">Freier Scan ohne Baumpunkt</option><optgroup label="Kataster-Beispieldaten">{trees.filter((candidate) => candidate.sampleAsset).map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.inventory?.genus} · {candidate.area}</option>)}</optgroup><optgroup label="Quest-Bäume">{trees.filter((candidate) => !candidate.sampleAsset && !candidate.presentation).map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.species} · {candidate.area}</option>)}</optgroup></select>}
        <p className="scan-help">{targetTree ? `Fotografiere den Baum bei ${targetTree.area}. ${targetTree.assetId ? 'Dieser Scan wird mit der Datensatz-ID verknüpft.' : 'Dieser Quest-Punkt hat noch keine Backend-Datensatz-ID.'}` : 'Fotografiere einen Baum oder wähle ein vorhandenes Foto. Mit einem Baumpunkt wird dein Fund dem richtigen Standort zugeordnet.'}</p>
        {scanError && <p className="scan-error" role="alert">{scanError}</p>}
        <button type="button" className="scan-primary" onClick={() => void nativePhoto(takePhoto, new Date())}><Camera size={19} /> Foto aufnehmen</button>
        <button type="button" className="scan-secondary" onClick={() => void nativePhoto(pickPhoto)}><ImagePlus size={18} /> Bild auswählen</button>
      </>}

      {stage === 'camera' && !native && <>
        <div className="scan-viewfinder">
          <video ref={videoRef} autoPlay muted playsInline aria-hidden="true" onLoadedMetadata={() => setCameraReady(true)} />
          <div className="scan-corners" aria-hidden="true" />
          <span className="scan-viewfinder-caption" aria-hidden="true"><ScanLine size={15} /> BAUM IM RAHMEN POSITIONIEREN</span>
          {!cameraReady && <div className="scan-camera-message"><Camera size={28} /><span>{cameraError || 'Kamera wird geöffnet …'}</span></div>}
        </div>
        {!tree && <label className="scan-target-label" htmlFor="scan-target">Welchen Baumpunkt scannst du?</label>}
        {!tree && <select id="scan-target" className="scan-species-select scan-target-select" value={selectedTreeId} onChange={(event) => setSelectedTreeId(event.target.value)}><option value="">Freier Scan ohne Baumpunkt</option><optgroup label="Kataster-Beispieldaten">{trees.filter((candidate) => candidate.sampleAsset).map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.inventory?.genus} · {candidate.area}</option>)}</optgroup><optgroup label="Quest-Bäume">{trees.filter((candidate) => !candidate.sampleAsset && !candidate.presentation).map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.species} · {candidate.area}</option>)}</optgroup></select>}
        <p className="scan-help">{quest ? `${quest.title}: ${quest.kind === 'photo' ? lt.quest.photoGoal : lt.quest.verifyGoal}` : targetTree ? `Fotografiere den Baum bei ${targetTree.area}. ${targetTree.assetId ? 'Dieser Scan wird mit der Datensatz-ID verknüpft.' : 'Dieser Quest-Punkt hat noch keine Backend-Datensatz-ID.'}` : 'Richte die Kamera auf einen Baum oder wähle ein vorhandenes Foto. Mit einem Baumpunkt wird dein Fund dem richtigen Standort zugeordnet.'}</p>
        {scanError && <p className="scan-error" role="alert">{scanError}</p>}
        <button type="button" className="scan-primary" onClick={capturePhoto} disabled={!cameraReady}><Camera size={19} /> Foto aufnehmen</button>
        {cameraError && <button type="button" className="scan-secondary" onClick={() => captureInputRef.current?.click()}><Camera size={18} /> Handykamera öffnen</button>}
        <button type="button" className="scan-secondary" onClick={() => fileInputRef.current?.click()}><ImagePlus size={18} /> Bild auswählen</button>
        <input ref={captureInputRef} className="scan-file-input" type="file" accept="image/*" capture="environment" aria-label="Baumfoto mit Handykamera aufnehmen" onChange={onPhotoSelected} />
        <input ref={fileInputRef} className="scan-file-input" type="file" accept="image/*" aria-label="Baumfoto auswählen" onChange={onPhotoSelected} />
      </>}

      {stage !== 'camera' && <>
        <div className={`scan-morph-stage ${stage}`}>
          <div className={`scan-morph-card ${photoOnly ? 'photo-only' : stage}`}>
            {photoUrl && <img className="scan-morph-photo" src={photoUrl} alt={stage === 'scanning' ? 'Dein Baumfoto wird gescannt' : photoOnly ? 'Dein Baumfoto' : 'Dein Baumfoto verwandelt sich in eine Sammelkarte'} aria-hidden={!photoOnly && (stage === 'result' || stage === 'saved')} />}
            <div className="scan-morph-art" aria-hidden={photoOnly || stage === 'scanning'}>
              {cardArt ? <Image src={cardArt.src} alt={`Sammelkarte ${selectedSpecies}`} fill sizes="220px" /> : <div className="scan-result-placeholder"><span>{species.find((item) => item.name === selectedSpecies)?.emoji}</span><strong>{selectedSpecies}</strong><small>OPENQUEST · BAUMFUND</small></div>}
            </div>
            <div className="scan-morph-grid" aria-hidden="true" />
            <div className="scan-morph-beam" aria-hidden="true" />
            <div className="scan-morph-shine" aria-hidden="true" />
            <div className="scan-morph-sparkles" aria-hidden="true"><i /><i /><i /><i /></div>
            <span className="scan-morph-photo-label">DEIN FOTO</span>
          </div>
        </div>
        {(stage === 'scanning' || stage === 'revealing') && <>
          <p className="scan-processing-label" role="status"><ScanLine size={19} /> {stage === 'scanning' ? (demoOnly ? 'Foto wird gescannt …' : 'Foto wird geprüft …') : 'Dein Foto wird zur Sammelkarte …'}</p>
          <p className="scan-disclaimer">{demoOnly || scanResult?.source === 'demo' ? 'Das Foto wird in dieser Demo noch nicht analysiert.' : stage === 'scanning' ? 'Die Bilderkennung prüft Baum, Gattung und Standort. Das dauert etwa 5 bis 10 Sekunden.' : verdictText[verdict ?? 'approve'].label}</p>
        </>}
      </>}

      {quest && (stage === 'result' || stage === 'saved') && <QuestResult
        scan={verified}
        demoNotice={scanResult?.source === 'demo'}
        stage={stage}
        rewardPoints={quest.rewardPoints}
        submission={submission}
        cancelling={cancelling}
        onResubmit={() => { if (verified) void submitQuest(verified); }}
        onRetry={retry}
        onCancelClaim={onCancelClaim ? cancelClaim : undefined}
        onClose={onClose}
      />}

      {!quest && verified && (stage === 'result' || stage === 'saved') && <VerifiedResult
        scan={verified}
        stage={stage}
        tree={targetTree}
        ready={ready}
        earnedXp={earnedXp}
        alreadyCollected={alreadyCollected}
        cardSpecies={verified.species ?? (targetTree ? selectedSpecies : null)}
        onConfirm={() => confirmVerified(verified)}
        onQueue={() => queueForReview(verified)}
        onRetry={retry}
        onClose={onClose}
      />}

      {!quest && !verified && (stage === 'result' || stage === 'saved') && <>
        {stage === 'result' ? <>
          {scanResult?.source === 'demo' && scanResult.notice && <p className="scan-demo-note">{scanResult.notice}</p>}
          <p className="scan-demo-note">Testantwort: {targetTree ? `Für den ausgewählten Baum wird ${targetTree.species} vorgeschlagen.` : 'Die Demo schlägt Birke vor.'} {targetTree?.inventory && `Im Datensatz steht die Gattung ${targetTree.inventory.genus}; die genaue Art ist ${targetTree.inventory.species ? 'hinterlegt' : 'noch offen'}.`} Das Foto wurde nicht durch eine Bilderkennung geprüft.</p>
          <label className="scan-species-label" htmlFor="scan-species">Baumart prüfen oder ändern</label>
          <select id="scan-species" className="scan-species-select" value={selectedSpecies} onChange={(event) => setSelectedSpecies(event.target.value as SpeciesName)}>{species.filter((item) => targetTree?.presentation ? item.name === 'Festtanne' : item.name !== 'Festtanne').map((item) => <option key={item.name} value={item.name}>{item.name}</option>)}</select>
          {alreadyCollected && <p className="scan-existing">Diese Karte ist schon aufgedeckt. Der neue Fund wird trotzdem einzeln gezählt.</p>}
          {!cardArt && <p className="scan-existing">Für diese Art gibt es noch kein Kartenmotiv. Der Baumfund wird trotzdem gespeichert.</p>}
          <button type="button" className="scan-primary" onClick={saveCard} disabled={!ready}>{!cardArt ? 'Fund zur Prüfung speichern' : alreadyCollected ? 'Weiteren Fund speichern' : 'Karte aufdecken & Fund speichern'}</button>
          <button type="button" className="scan-secondary" onClick={retry}><RotateCcw size={17} /> Neues Foto aufnehmen</button>
        </> : <>
          <p className="scan-success"><Check size={19} /> {targetTree ? `${selectedSpecies}: Fund am gewählten Baumpunkt gespeichert.` : `${selectedSpecies}: Fund gespeichert.`}</p>
          {earnedXp > 0 && <p className="scan-reward">+{earnedXp} XP für die Präsentationskarte</p>}
          <p className="scan-disclaimer">{targetTree?.presentation ? 'Der Bühnenbaum zählt nicht für Stadtteil-Punkte.' : observationSaved ? 'Artvorschlag wartet im Admin-Panel auf Prüfung. Wiederholte Scans geben keine zusätzlichen XP oder Stadtteil-Punkte.' : targetTree ? 'Demo-Fund gespeichert. Ein Prüfeintrag konnte lokal nicht angelegt werden.' : 'Demo-Scan ohne Baumpunkt: keine XP und keine Stadtteil-Punkte.'}</p>
          <button type="button" className="scan-primary" onClick={onClose}>Fertig</button>
        </>}
      </>}
    </section>
  </div>;
}

function VerifiedResult({ scan, stage, tree, ready, earnedXp, alreadyCollected, cardSpecies, onConfirm, onQueue, onRetry, onClose }: {
  scan: VerifiedScan;
  stage: 'result' | 'saved';
  tree?: Tree | null;
  ready: boolean;
  earnedXp: number;
  alreadyCollected: boolean;
  cardSpecies: string | null;
  onConfirm: () => void;
  onQueue: () => void;
  onRetry: () => void;
  onClose: () => void;
}) {
  const { verification: result } = scan;
  const verdict = result.verdict;

  if (stage === 'saved') {
    return verdict === 'review' ? <>
      <p className="scan-success"><Clock3 size={19} /> Dein Fund ist vorgemerkt.</p>
      <p className="scan-disclaimer">Er wird geprüft. XP und Karte gibt es, sobald er bestätigt ist.</p>
      <button type="button" className="scan-primary" onClick={onClose}>Fertig</button>
    </> : <>
      <p className="scan-success"><Check size={19} /> {cardSpecies ? `${cardSpecies} ist jetzt in deinem Baumbuch.` : 'Fund bestätigt.'}</p>
      {earnedXp > 0 && <p className="scan-reward">+{earnedXp} XP für diesen Baum</p>}
      <p className="scan-disclaimer">{tree ? 'Bestätigt durch die Foto-Prüfung.' : 'Ohne Quest-Baum gibt es für diesen Scan keine XP.'}</p>
      <button type="button" className="scan-primary" onClick={onClose}>Fertig</button>
    </>;
  }

  return <>
    <VerdictDetails scan={scan} />
    {verdict === 'approve' && <>
      {!cardSpecies && <p className="scan-existing">Für diese Gattung gibt es noch keine Sammelkarte.</p>}
      {cardSpecies && alreadyCollected && <p className="scan-existing">Diese Karte ist schon aufgedeckt. Der neue Fund wird trotzdem einzeln gezählt.</p>}
      <button type="button" className="scan-primary" onClick={cardSpecies || tree ? onConfirm : onClose} disabled={!ready}>{cardSpecies || tree ? (alreadyCollected ? 'Weiteren Fund speichern' : 'Karte aufdecken & Fund speichern') : 'Fertig'}</button>
    </>}
    {verdict === 'review' && <button type="button" className="scan-primary" onClick={onQueue}>Zur Prüfung vormerken</button>}
    <button type="button" className={verdict === 'reject' ? 'scan-primary' : 'scan-secondary'} onClick={onRetry}><RotateCcw size={17} /> Neues Foto aufnehmen</button>
    {verdict === 'reject' && <button type="button" className="scan-secondary" onClick={onClose}>Schließen</button>}
    <p className="scan-verify-meta">FOTO-PRÜFUNG · {(result.latencyMs / 1000).toFixed(1).replace('.', ',')} S</p>
  </>;
}

/** Verdict, detected genus, catalog mismatch and the reasons of a photo check. */
function VerdictDetails({ scan }: { scan: VerifiedScan }) {
  const { verification: result, genus, expectedGenus } = scan;
  const verdict = result.verdict;
  const VerdictIcon = verdict === 'approve' ? CircleCheck : verdict === 'review' ? Clock3 : CircleX;
  const mismatch = Boolean(expectedGenus && genus && genus !== expectedGenus);
  // One line per distinct message, problems first; info reasons only confirm a good result.
  const order = { hard: 0, soft: 1, info: 2 } as const;
  // A hard precheck failure skips the models on purpose, so "vision unavailable" would only confuse.
  const skippedModels = result.reasons.some((reason) => reason.severity === 'hard' && reason.code !== 'no_tree' && reason.code !== 'not_a_live_photo');
  const reasons = [...result.reasons]
    .filter((reason) => verdict === 'approve' || reason.severity !== 'info')
    .filter((reason) => !(skippedModels && reason.code === 'vision_unavailable'))
    .sort((a, b) => order[a.severity] - order[b.severity])
    .map((reason) => ({ severity: reason.severity, text: reasonMessage(reason, result) }))
    .filter((reason, index, all) => all.findIndex((other) => other.text === reason.text) === index);
  return <>
    <div className={`scan-verdict ${verdict}`} role="status"><VerdictIcon size={20} /><div><strong>{verdictText[verdict].label}</strong>{verdictText[verdict].summary}</div></div>
    <p className="scan-genus"><small>ERKANNT</small><span>{genus ? genusLabel(genus) : 'Keine Gattung sicher erkannt'}</span>{genus && <strong>{formatPercent(scan.genusProbability)}</strong>}</p>
    {mismatch && <p className="scan-mismatch">Im Kataster steht hier {genusLabel(expectedGenus!)}. Das Foto sieht nach {genusLabel(genus!)} aus. Prüfe, ob du den richtigen Baum fotografiert hast.</p>}
    {reasons.length > 0 && <ul className="scan-reasons">{reasons.map((reason) => <li key={reason.text} className={reason.severity}>{reason.severity === 'hard' ? <CircleX size={14} /> : reason.severity === 'soft' ? <AlertTriangle size={14} /> : <Check size={14} />}<span>{reason.text}</span></li>)}</ul>}
  </>;
}

/** Result of a live quest scan: photo check, then the submission to the API. XP only come after moderation. */
function QuestResult({ scan, demoNotice, stage, rewardPoints, submission, cancelling, onResubmit, onRetry, onCancelClaim, onClose }: {
  scan: VerifiedScan | null;
  demoNotice: boolean;
  stage: 'result' | 'saved';
  rewardPoints: number;
  submission: Submission;
  cancelling: boolean;
  onResubmit: () => void;
  onRetry: () => void;
  onCancelClaim?: () => void;
  onClose: () => void;
}) {
  if (stage === 'saved' && submission.state === 'done') {
    return <>
      <p className="scan-success"><Send size={19} /> {lt.scan.submitted}</p>
      <p className="scan-disclaimer">{lt.scan.submittedDetail(rewardPoints)}</p>
      {scan?.genus && <p className="scan-genus"><small>ERKANNT</small><span>{genusLabel(scan.genus)}</span><strong>{formatPercent(scan.genusProbability)}</strong></p>}
      <Link href="/missions" className="scan-secondary quest-link" onClick={onClose}><ListTodo size={17} /> {lt.scan.myQuests}</Link>
      <button type="button" className="scan-primary quest-done" onClick={onClose}>Fertig</button>
      <p className="scan-verify-meta">EINREICHUNG {submission.result.submissionId.slice(0, 8).toUpperCase()} · {Math.round(submission.result.distanceMeters)} M VOM BAUM</p>
    </>;
  }

  const rejected = scan?.verification.verdict === 'reject';
  const cancelButton = onCancelClaim && <button type="button" className="scan-secondary" onClick={onCancelClaim} disabled={cancelling}>{cancelling ? <Loader2 size={17} className="spin" /> : <X size={17} />} {cancelling ? lt.quest.cancelling : lt.quest.cancel}</button>;
  return <>
    {scan ? <VerdictDetails scan={scan} /> : demoNotice && <p className="scan-demo-note">{lt.scan.noVerification}</p>}
    {submission.state === 'sending' && <p className="scan-processing-label" role="status"><Loader2 size={18} className="spin" /> {lt.scan.submitting}</p>}
    {submission.state === 'error' && <p className="scan-error" role="alert">{submission.message}</p>}
    {rejected && <p className="scan-disclaimer quest-keeps-claim">{lt.scan.rejectedKeepsClaim}</p>}
    {submission.state === 'error' && submission.canRetry && <button type="button" className="scan-primary" onClick={onResubmit}><Send size={17} /> {lt.scan.retrySubmit}</button>}
    {submission.state !== 'sending' && <>
      <button type="button" className={rejected || !scan ? 'scan-primary' : 'scan-secondary'} onClick={onRetry}><RotateCcw size={17} /> Neues Foto aufnehmen</button>
      {(rejected || submission.state === 'error' || !scan) && cancelButton}
      <button type="button" className="scan-secondary" onClick={onClose}>{lt.scan.later}</button>
    </>}
    {scan && <p className="scan-verify-meta">FOTO-PRÜFUNG · {(scan.verification.latencyMs / 1000).toFixed(1).replace('.', ',')} S</p>}
  </>;
}
