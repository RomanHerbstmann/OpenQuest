'use client';

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Loader2 } from 'lucide-react';
import { AuthDialog, type AuthMode } from '@/components/account/AuthDialog';
import { liveText } from '@/i18n/liveGame';
import { api, ApiError, clearSession, loadSession, onServerSlow, saveSession, SESSION_EVENT, type MeDto, type MyClaimDto, type RegisterResponse, type Session } from '@/lib/api';

type SessionContextValue = {
  /** `null` means demo mode. */
  session: Session | null;
  ready: boolean;
  login: (username: string, password: string) => Promise<void>;
  /** Returns the one time recovery codes; they are not stored anywhere. */
  register: (username: string, password: string) => Promise<RegisterResponse>;
  logout: () => void;
  openAuth: (mode?: AuthMode) => void;
  claims: MyClaimDto[] | null;
  /** Points and level from `GET /me`, `null` while unknown or on API versions without gamification. */
  progress: { totalPoints: number; level: NonNullable<MeDto['level']> } | null;
  claimsError: string;
  claimsLoading: boolean;
  refreshClaims: () => Promise<void>;
};

const SessionContext = createContext<SessionContextValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [ready, setReady] = useState(false);
  const [authMode, setAuthMode] = useState<AuthMode | null>(null);
  const [claims, setClaims] = useState<MyClaimDto[] | null>(null);
  const [progress, setProgress] = useState<SessionContextValue['progress']>(null);
  const [claimsError, setClaimsError] = useState('');
  const [claimsLoading, setClaimsLoading] = useState(false);
  const [serverSlow, setServerSlow] = useState(false);

  useEffect(() => {
    const sync = () => setSession(loadSession());
    const onStorage = (event: StorageEvent) => { if (event.key === null || event.key.startsWith('openquest-session')) sync(); };
    sync();
    setReady(true);
    window.addEventListener(SESSION_EVENT, sync);
    window.addEventListener('storage', onStorage);
    return () => { window.removeEventListener(SESSION_EVENT, sync); window.removeEventListener('storage', onStorage); };
  }, []);

  useEffect(() => onServerSlow(setServerSlow), []);

  const refreshClaims = useCallback(async () => {
    if (!loadSession()) { setClaims(null); setProgress(null); return; }
    setClaimsLoading(true);
    try {
      const [list, me] = await Promise.all([api.myClaims(), api.me().catch(() => null)]);
      setClaims(list);
      setProgress(me && typeof me.totalPoints === 'number' && me.level ? { totalPoints: me.totalPoints, level: me.level } : null);
      setClaimsError('');
    } catch (error) {
      setClaimsError(error instanceof ApiError ? error.message : liveText.de.claims.error);
    } finally {
      setClaimsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (session) void refreshClaims();
    else { setClaims(null); setProgress(null); setClaimsError(''); }
  }, [session, refreshClaims]);

  const login = useCallback(async (username: string, password: string) => {
    saveSession(await api.login(username.trim(), password));
  }, []);

  const register = useCallback(async (username: string, password: string) => {
    const response = await api.register(username.trim(), password);
    saveSession(response);
    return response;
  }, []);

  const logout = useCallback(() => clearSession(), []);
  const openAuth = useCallback((mode: AuthMode = 'login') => setAuthMode(mode), []);

  const value = useMemo(
    () => ({ session, ready, login, register, logout, openAuth, claims, progress, claimsError, claimsLoading, refreshClaims }),
    [session, ready, login, register, logout, openAuth, claims, progress, claimsError, claimsLoading, refreshClaims],
  );

  return <SessionContext.Provider value={value}>
    {children}
    {authMode && <AuthDialog initialMode={authMode} onClose={() => setAuthMode(null)} />}
    {serverSlow && <div className="server-wake-banner" role="status"><Loader2 size={15} className="spin" aria-hidden="true" />{liveText.de.server.waking}</div>}
  </SessionContext.Provider>;
}

export function useSession() {
  const context = useContext(SessionContext);
  if (!context) throw new Error('useSession requires SessionProvider');
  return context;
}
