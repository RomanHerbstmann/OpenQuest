'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { BookOpen, BookOpenText, Compass, ListTodo, UserRound } from 'lucide-react';

const items = [
  { href: '/', label: 'Karte', icon: Compass },
  { href: '/missions', label: 'Missionen', icon: ListTodo },
  { href: '/collection', label: 'Baumbuch', icon: BookOpen },
  { href: '/lexicon', label: 'Lexikon', icon: BookOpenText },
  { href: '/profile', label: 'Profil', icon: UserRound },
];

export function BottomNavigation() {
  const pathname = usePathname();
  return <nav className="bottom-nav" aria-label="Hauptnavigation">
    {items.map(({ href, label, icon: Icon }) => {
      const active = pathname === href || (href === '/lexicon' && pathname.startsWith('/lexicon/'));
      return <Link className={`nav-item ${active ? 'active' : ''}`} href={href} key={href} aria-current={active ? 'page' : undefined}>
        <Icon size={21} strokeWidth={active ? 2.5 : 2} aria-hidden="true" />
        <span>{label}</span>
      </Link>;
    })}
  </nav>;
}
