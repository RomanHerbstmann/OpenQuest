import Link from 'next/link';
import { Trees } from 'lucide-react';

export function Brand() {
  return <Link href="/" className="brand" aria-label="OpenQuest – zur Karte">
    <span className="brand-icon"><Trees size={22} strokeWidth={2.4} /></span>
    <span>open<span className="brand-accent">quest</span><span className="brand-dot">.</span></span>
  </Link>;
}
