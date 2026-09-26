import type { Metadata } from 'next';
import './globals.css';
import './design2.css';
import './tree-search.css';
import './live-game.css';
import 'leaflet/dist/leaflet.css';
import './districts.css';
import './branding.css';
import { PlayerProvider } from '@/context/PlayerContext';
import { SessionProvider } from '@/context/SessionContext';
import { AppNavigation } from '@/components/navigation/AppNavigation';

export const metadata: Metadata = {
  title: 'OpenQuest · Entdecke Münsters Bäume',
  description: 'Entdecke Stadtbäume in Münster und hilf mit, öffentliche Baumdaten aktuell zu halten.',
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="de"><body><PlayerProvider><SessionProvider>{children}<AppNavigation /></SessionProvider></PlayerProvider></body></html>;
}
