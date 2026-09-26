'use client';

import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Check, Copy, KeyRound, LogIn, UserPlus, X } from 'lucide-react';
import { useSession } from '@/context/SessionContext';
import { liveText } from '@/i18n/liveGame';
import { ApiError } from '@/lib/api';

export type AuthMode = 'login' | 'register';
const t = liveText.de.auth;

export function AuthDialog({ initialMode, onClose }: { initialMode: AuthMode; onClose: () => void }) {
  const { login, register } = useSession();
  const [mode, setMode] = useState<AuthMode>(initialMode);
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [codes, setCodes] = useState<string[] | null>(null);
  const [copied, setCopied] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);
  const userRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    userRef.current?.focus();
    return () => previousFocus?.focus();
  }, []);

  // Recovery codes are shown once: Escape and the backdrop must not dismiss them by accident.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape' && !codes) onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [codes, onClose]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError('');
    try {
      if (mode === 'login') {
        await login(username, password);
        onClose();
      } else {
        const response = await register(username, password);
        setPassword('');
        setCodes(response.recoveryCodes);
      }
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : liveText.de.errors.fallback);
    } finally {
      setBusy(false);
    }
  };

  const copyCodes = async () => {
    if (!codes) return;
    try { await navigator.clipboard.writeText(codes.join('\n')); setCopied(true); } catch { setCopied(false); }
  };

  const title = codes ? t.titleCodes : mode === 'login' ? t.titleLogin : t.titleRegister;

  return <div className="scan-overlay" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !codes) onClose(); }}>
    <section className="scan-modal auth-modal" role="dialog" aria-modal="true" aria-labelledby="auth-title">
      <header className="scan-header"><div><p className="eyebrow">{t.eyebrow}</p><h2 id="auth-title">{title}</h2></div>{!codes && <button ref={closeRef} type="button" className="scan-close" onClick={onClose} aria-label={t.close}><X size={21} /></button>}</header>

      {codes ? <>
        <p className="auth-intro"><KeyRound size={16} aria-hidden="true" /> {t.codesIntro}</p>
        <ol className="auth-codes" aria-label={t.titleCodes}>{codes.map((code) => <li key={code}><code>{code}</code></li>)}</ol>
        <button type="button" className="scan-secondary" onClick={copyCodes}>{copied ? <Check size={17} /> : <Copy size={17} />} {copied ? t.copied : t.copy}</button>
        <button type="button" className="scan-primary auth-done" onClick={onClose}><Check size={18} /> {t.codesDone}</button>
      </> : <>
        <div className="auth-tabs" role="tablist" aria-label={t.eyebrow}>
          <button type="button" role="tab" aria-selected={mode === 'login'} className={mode === 'login' ? 'active' : ''} onClick={() => { setMode('login'); setError(''); }}><LogIn size={15} /> {t.tabLogin}</button>
          <button type="button" role="tab" aria-selected={mode === 'register'} className={mode === 'register' ? 'active' : ''} onClick={() => { setMode('register'); setError(''); }}><UserPlus size={15} /> {t.tabRegister}</button>
        </div>
        <p className="auth-intro">{t.intro}</p>
        <form className="auth-form" onSubmit={submit}>
          <label htmlFor="auth-username">{t.username}</label>
          <input ref={userRef} id="auth-username" className="auth-input" name="username" autoComplete="username" autoCapitalize="none" spellCheck={false} required minLength={3} maxLength={32} value={username} onChange={(event) => setUsername(event.target.value)} aria-describedby={mode === 'register' ? 'auth-username-hint' : undefined} />
          {mode === 'register' && <small id="auth-username-hint">{t.usernameHint}</small>}
          <label htmlFor="auth-password">{t.password}</label>
          <input id="auth-password" className="auth-input" name="password" type="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required minLength={8} maxLength={128} value={password} onChange={(event) => setPassword(event.target.value)} aria-describedby={mode === 'register' ? 'auth-password-hint' : undefined} />
          {mode === 'register' && <small id="auth-password-hint">{t.passwordHint}</small>}
          {error && <p className="scan-error" role="alert">{error}</p>}
          <button type="submit" className="scan-primary" disabled={busy}>{mode === 'login' ? <LogIn size={18} /> : <UserPlus size={18} />} {busy ? t.busy : mode === 'login' ? t.submitLogin : t.submitRegister}</button>
        </form>
      </>}
    </section>
  </div>;
}
