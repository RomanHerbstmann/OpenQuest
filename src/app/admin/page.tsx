import type { Metadata } from 'next';
import { AdminDashboard } from '@/components/admin/AdminDashboard';
import './admin.css';

export const metadata: Metadata = { title: 'Prüfbereich · OpenQuest', robots: { index: false, follow: false } };

export default function AdminPage() { return <AdminDashboard />; }
