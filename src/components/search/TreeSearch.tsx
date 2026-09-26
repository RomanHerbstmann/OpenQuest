'use client';

import { useRef, useState, type FormEvent } from 'react';
import { ChevronDown, ChevronUp, Info, LoaderCircle, Search, X } from 'lucide-react';
import { apiUrl } from '@/lib/config';
import { treeSearchText } from '@/i18n/treeSearch';
import type { ResultItem, TreeSearchResponse } from '@/types/treeSearch';

const t = treeSearchText.de;
const pct = (confidence: number) => `${Math.round(confidence * 100)} %`;
const nf = new Intl.NumberFormat('de-DE');

type Props = {
  result: TreeSearchResponse | null;
  onResult: (result: TreeSearchResponse | null) => void;
  onFocusItem: (item: ResultItem) => void;
};

function Chips({ result }: { result: TreeSearchResponse }) {
  const i = result.interpretation;
  const chips: Array<{ key: string; label: string; value: string; confidence?: number }> = [];
  if (i.sort.value !== 'none') chips.push({ key: 'sort', label: t.labels.sort, value: t.sort[i.sort.value], confidence: i.sort.confidence });
  if (i.genus) chips.push({ key: 'genus', label: t.labels.genus, value: i.genus.value === 'unknown' ? t.unknownGenus : i.genus.value, confidence: i.genus.confidence });
  if (i.area) chips.push({ key: 'area', label: t.labels.area, value: i.area.value.replace(/^Münster-/, ''), confidence: i.area.confidence });
  if (i.street) chips.push({ key: 'street', label: t.labels.street, value: i.street });
  if (i.minHeightM !== undefined || i.maxHeightM !== undefined) {
    chips.push({ key: 'height', label: t.labels.height, value: [i.minHeightM !== undefined && `> ${i.minHeightM} m`, i.maxHeightM !== undefined && `< ${i.maxHeightM} m`].filter(Boolean).join(' ') });
  }
  if (i.intent.value === 'count') chips.push({ key: 'intent', label: t.labels.intent, value: t.count, confidence: i.intent.confidence });
  return <ul className="search-chips" aria-label={t.interpretation}>
    <li className="search-chip source">{t.source[i.source]}</li>
    {chips.map((chip) => <li key={chip.key} className="search-chip">{chip.label} <b>{chip.value}</b>{chip.confidence !== undefined && <span> {pct(chip.confidence)}</span>}</li>)}
  </ul>;
}

export function TreeSearch({ result, onResult, onFocusItem }: Props) {
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [listOpen, setListOpen] = useState(false);
  const request = useRef<AbortController | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const q = query.trim();
    if (!q || loading) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setLoading(true);
    setError('');
    try {
      const response = await fetch(apiUrl('/api/tree-search'), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ q }), signal: controller.signal });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const next = (await response.json()) as TreeSearchResponse;
      setListOpen(false);
      onResult(next);
    } catch (err) {
      if ((err as Error).name === 'AbortError') return;
      setError(t.error);
    } finally {
      if (request.current === controller) setLoading(false);
    }
  };

  const clear = () => {
    request.current?.abort();
    setLoading(false);
    setQuery('');
    setError('');
    setListOpen(false);
    onResult(null);
  };

  const notes = result?.interpretation.notes.map((note) => t.notes[note]).filter(Boolean) ?? [];
  const showClear = Boolean(query || result || error);

  return <section className="tree-search" aria-label={t.label}>
    <form className="search-bar" role="search" onSubmit={submit}>
      <Search size={18} aria-hidden="true" className="search-icon" />
      <input type="search" name="q" value={query} onChange={(event) => setQuery(event.target.value)} maxLength={200} placeholder={t.placeholder} aria-label={t.label} enterKeyHint="search" autoComplete="off" />
      {showClear && <button type="button" className="search-clear" onClick={clear} aria-label={t.clear}><X size={17} /></button>}
      <button type="submit" className="search-submit" disabled={loading || !query.trim()} aria-label={t.submit}>
        {loading ? <LoaderCircle size={18} className="spin" /> : <Search size={18} />}
      </button>
    </form>

    {(loading || error || result) && <div className="search-panel" aria-live="polite">
      {loading && <p className="search-status">{t.loading}</p>}
      {!loading && error && <p className="search-status error" role="alert">{error}</p>}
      {!loading && !error && result && <>
        <p className="search-summary">{result.summary.de}</p>
        <Chips result={result} />
        {notes.length > 0 && <p className="search-note"><Info size={14} aria-hidden="true" />{notes.join(' ')}</p>}
        {result.positionsSampled && <p className="search-meta">{t.sampled(result.positions.length, result.total)}</p>}
        {result.items.length === 0 ? <p className="search-status">{t.empty}</p> : <>
          <button type="button" className="search-toggle" onClick={() => setListOpen((open) => !open)} aria-expanded={listOpen} aria-controls="search-results">
            {listOpen ? t.hideList : t.showList(result.items.length)}{listOpen ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
          </button>
          {listOpen && <ol id="search-results" className="search-results">
            {result.items.map((item) => {
              const name = item.genusDe || item.rawGenus || t.unknownGenus;
              return <li key={item.id}>
                <button type="button" onClick={() => { onFocusItem(item); setListOpen(false); }} aria-label={t.focus(item.rank, name)}>
                  <span className="search-rank">{item.rank}</span>
                  <span className="search-item"><strong>{name}</strong><small>{item.street ?? t.unknownStreet}{item.quarter ? ` · ${item.quarter}` : ''}</small></span>
                  <span className="search-height">{item.heightM !== null ? `${nf.format(item.heightM)} m` : t.noHeight}</span>
                </button>
              </li>;
            })}
          </ol>}
        </>}
        <p className="search-source">{t.source_note}</p>
      </>}
    </div>}
  </section>;
}
