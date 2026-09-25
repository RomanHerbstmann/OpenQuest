'use client';

import { usePathname } from 'next/navigation';
import { BottomNavigation } from './BottomNavigation';

export function AppNavigation() {
  const pathname = usePathname();
  return pathname.startsWith('/admin') ? null : <BottomNavigation />;
}
