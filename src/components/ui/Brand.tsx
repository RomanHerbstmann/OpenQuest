import Link from 'next/link';
import Image from 'next/image';

export function Brand() {
  return <Link href="/" className="brand" aria-label="OpenQuest – zur Karte">
    <Image className="brand-logo" src="/branding/openquest-logo.png" alt="OpenQuest" width={1400} height={401} unoptimized priority />
  </Link>;
}
