'use client';

import Image from 'next/image';
import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { Camera, Check, ImagePlus, RotateCcw, ScanLine, X } from 'lucide-react';
import { usePlayer } from '@/context/PlayerContext';
import { cardArtBySpecies } from '@/data/cardArt';
import { species } from '@/data/species';
import { recognizeTreeForPrototype } from '@/lib/mockScan';
import type { Tree } from '@/types/tree';

type Stage = 'camera' | 'scanning' | 'revealing' | 'result' | 'saved';
type SpeciesName = (typeof species)[number]['name'];

export function ScanModal({ tree, onClose }: { tree?: Tree | null; onClose: () => void }) {
  const { progress, ready, addScannedCard } = usePlayer();
  const [stage, setStage] = useState<Stage>('camera');
  const [cameraReady, setCameraReady] = useState(false);
  const [cameraError, setCameraError] = useState('');
  const [scanError, setScanError] = useState('');
  const [photoUrl, setPhotoUrl] = useState<string | null>(null);
  const [selectedSpecies, setSelectedSpecies] = useState<SpeciesName>('Birke');
  const [earnedXp, setEarnedXp] = useState(0);
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const captureInputRef = useRef<HTMLInputElement>(null);
  const photoUrlRef = useRef<string | null>(null);
  const scanRunRef = useRef(0);
  const revealTimerRef = useRef<number | null>(null);
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
    if (stage !== 'camera') return;
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

  const processImage = async (image: Blob) => {
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
    setStage('scanning');
    try {
      const result = await recognizeTreeForPrototype(image, tree?.species);
      if (scanRunRef.current !== run) return;
      const matched = species.find((item) => item.name === result.species);
      setSelectedSpecies(matched?.name ?? 'Birke');
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
      if (blob) void processImage(blob);
      else setScanError('Die Aufnahme ist fehlgeschlagen. Bitte versuche es erneut.');
    }, 'image/jpeg', 0.85);
  };

  const onPhotoSelected = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file) void processImage(file);
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
    setStage('camera');
  };

  const saveCard = () => {
    if (!ready) return;
    const reward = tree?.presentation && selectedSpecies === tree.species ? tree.xpReward : 0;
    addScannedCard(selectedSpecies, reward);
    setEarnedXp(reward);
    setStage('saved');
  };

  const cardArt = cardArtBySpecies[selectedSpecies];
  const alreadyCollected = progress.discoveredSpecies.includes(selectedSpecies);

  return <div className="scan-overlay" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <section className="scan-modal" role="dialog" aria-modal="true" aria-labelledby="scan-title">
      <header className="scan-header"><div><p className="eyebrow">OPENQUEST / ENTDECKEN</p><h2 id="scan-title">{stage === 'saved' ? 'Karte gesammelt!' : stage === 'result' ? 'Deine Sammelkarte' : stage === 'revealing' ? 'Deine Karte entsteht' : 'Baum scannen'}</h2></div><button ref={closeRef} type="button" className="scan-close" onClick={onClose} aria-label="Scan schließen"><X size={21} /></button></header>
      <div className="scan-progress-steps" aria-label="Scan-Fortschritt"><span className="active">01 AUFNEHMEN</span><span className={stage === 'camera' ? '' : 'active'}>02 ERKENNEN</span><span className={stage === 'result' || stage === 'saved' ? 'active' : ''}>03 SAMMELN</span></div>

      {stage === 'camera' && <>
        <div className="scan-viewfinder">
          <video ref={videoRef} autoPlay muted playsInline aria-hidden="true" onLoadedMetadata={() => setCameraReady(true)} />
          <div className="scan-corners" aria-hidden="true" />
          <span className="scan-viewfinder-caption" aria-hidden="true"><ScanLine size={15} /> BAUM IM RAHMEN POSITIONIEREN</span>
          {!cameraReady && <div className="scan-camera-message"><Camera size={28} /><span>{cameraError || 'Kamera wird geöffnet …'}</span></div>}
        </div>
        <p className="scan-help">{tree ? `Fotografiere den Baum bei ${tree.area}.` : 'Richte die Kamera auf einen Baum oder wähle ein vorhandenes Foto.'}</p>
        {scanError && <p className="scan-error" role="alert">{scanError}</p>}
        <button type="button" className="scan-primary" onClick={capturePhoto} disabled={!cameraReady}><Camera size={19} /> Foto aufnehmen</button>
        {cameraError && <button type="button" className="scan-secondary" onClick={() => captureInputRef.current?.click()}><Camera size={18} /> Handykamera öffnen</button>}
        <button type="button" className="scan-secondary" onClick={() => fileInputRef.current?.click()}><ImagePlus size={18} /> Bild auswählen</button>
        <input ref={captureInputRef} className="scan-file-input" type="file" accept="image/*" capture="environment" aria-label="Baumfoto mit Handykamera aufnehmen" onChange={onPhotoSelected} />
        <input ref={fileInputRef} className="scan-file-input" type="file" accept="image/*" aria-label="Baumfoto auswählen" onChange={onPhotoSelected} />
      </>}

      {stage !== 'camera' && <>
        <div className={`scan-morph-stage ${stage}`}>
          <div className={`scan-morph-card ${stage}`}>
            {photoUrl && <img className="scan-morph-photo" src={photoUrl} alt={stage === 'scanning' ? 'Dein Baumfoto wird gescannt' : 'Dein Baumfoto verwandelt sich in eine Sammelkarte'} aria-hidden={stage === 'result' || stage === 'saved'} />}
            <div className="scan-morph-art" aria-hidden={stage === 'scanning'}>
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
          <p className="scan-processing-label" role="status"><ScanLine size={19} /> {stage === 'scanning' ? 'Foto wird gescannt …' : 'Dein Foto wird zur Sammelkarte …'}</p>
          <p className="scan-disclaimer">Das Foto wird in dieser Demo noch nicht analysiert.</p>
        </>}
      </>}

      {(stage === 'result' || stage === 'saved') && <>
        {stage === 'result' ? <>
          <p className="scan-demo-note">Testantwort: {tree ? `Für diesen Baum wird ${tree.species} vorgeschlagen.` : 'Die Demo schlägt Birke vor.'} Das Foto wurde nicht durch eine Bilderkennung geprüft.</p>
          <label className="scan-species-label" htmlFor="scan-species">Baumart prüfen oder ändern</label>
          <select id="scan-species" className="scan-species-select" value={selectedSpecies} onChange={(event) => setSelectedSpecies(event.target.value as SpeciesName)}>{species.map((item) => <option key={item.name} value={item.name}>{item.name}</option>)}</select>
          {alreadyCollected && <p className="scan-existing">Diese Karte ist bereits in deinem Baumbuch.</p>}
          <button type="button" className="scan-primary" onClick={alreadyCollected ? onClose : saveCard} disabled={!ready}>{alreadyCollected ? 'Fertig' : 'Karte zum Baumbuch hinzufügen'}</button>
          <button type="button" className="scan-secondary" onClick={retry}><RotateCcw size={17} /> Neues Foto aufnehmen</button>
        </> : <>
          <p className="scan-success"><Check size={19} /> {selectedSpecies} ist jetzt in deinem Baumbuch.</p>
          {earnedXp > 0 && <p className="scan-reward">+{earnedXp} XP für die Präsentationskarte</p>}
          <p className="scan-disclaimer">{tree?.presentation ? 'Der Bühnenbaum zählt nicht für Stadtteil-Punkte.' : 'Demo-Scan: keine XP und keine Stadtteil-Punkte.'}</p>
          <button type="button" className="scan-primary" onClick={onClose}>Fertig</button>
        </>}
      </>}
    </section>
  </div>;
}
