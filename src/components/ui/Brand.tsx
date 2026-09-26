import Link from 'next/link';
import Image from 'next/image';

export function Brand() {
  return <Link href="/" className="brand" aria-label="OpenQuest – zur Karte">
    <Image className="brand-logo" src="/branding/openquest-logo.jpg" alt="OpenQuest" width={1280} height={428} unoptimized priority />
  </Link>;
}
