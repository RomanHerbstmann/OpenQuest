// Review queue: look at what players handed in and approve or reject it. Approving pays the points.

import { h, clear, toast } from './dom.js';
import { get, post, imageUrl, ApiError } from './api.js';
import { t } from './de.js';

export function mountModeration(root) {
  const state = { status: 'pending' };
  const list = h('div', { class: 'submissions' });
  const statusSelect = h('select', { 'aria-label': t.moderation.heading, onchange: () => { state.status = statusSelect.value; load(); } },
    ['pending', 'approved', 'rejected'].map((s) => h('option', { value: s }, t.moderation[s])));
  root.append(h('section', { class: 'page' },
    h('div', { class: 'row between' }, h('h2', null, t.moderation.heading),
      h('div', { class: 'row' }, statusSelect, h('button', { type: 'button', onclick: load }, t.moderation.reloadHint))),
    list));

  async function load() {
    clear(list);
    try {
      const items = await get(`/admin/submissions?status=${state.status}&limit=50`);
      if (!items.length) list.append(h('p', { class: 'muted' }, t.moderation.empty));
      for (const item of items) list.append(card(item));
    } catch (error) { toast(error instanceof ApiError ? error.message : t.errors.network, 'error'); }
  }

  function card(item) {
    const photo = h('div', { class: 'photo' }, item.mediaId ? h('span', { class: 'muted' }, '…') : h('span', { class: 'muted' }, t.moderation.noPhoto));
    if (item.mediaId) {
      imageUrl(`/admin/media/${item.mediaId}`).then((url) => { clear(photo); photo.append(h('img', { src: url, alt: item.questTitle })); })
        .catch(() => { clear(photo); photo.append(h('span', { class: 'muted' }, t.moderation.noPhoto)); });
    }
    const reason = h('input', { type: 'text', placeholder: t.moderation.reason, maxlength: 500, hidden: true });
    const confirm = h('button', { type: 'button', class: 'danger', hidden: true, onclick: () => review(item, false, reason.value.trim(), el) }, t.moderation.confirmReject);
    const value = item.payload?.value ?? item.payload?.condition;
    const el = h('article', { class: 'submission' },
      photo,
      h('div', { class: 'body' },
        h('h3', null, item.questTitle),
        h('p', { class: 'muted' }, `${t.moderation.by} ${item.username} · ${new Date(item.submittedAt).toLocaleString('de-DE')}`),
        h('p', null, `${t.moderation.task}: ${item.taskType}`, item.taskConfig?.attribute ? ` (${item.taskConfig.attribute})` : ''),
        value !== undefined && h('p', null, `${t.moderation.value}: `, h('strong', null, String(value))),
        h('p', { class: 'muted small' }, `${item.asset?.attributes?.genus ?? '—'} · ${item.asset?.lat?.toFixed(5)}, ${item.asset?.lon?.toFixed(5)} · ${t.moderation.distance}: ${Math.round(item.distanceMeters)} m`),
        item.rejectionReason && h('p', { class: 'problem' }, item.rejectionReason),
        item.status === 'pending' && h('div', { class: 'row' },
          h('button', { type: 'button', class: 'primary', onclick: () => review(item, true, null, el) }, t.moderation.approve),
          h('button', { type: 'button', onclick: () => { reason.hidden = false; confirm.hidden = false; reason.focus(); } }, t.moderation.reject),
          reason, confirm)));
    return el;
  }

  async function review(item, approved, reason, el) {
    if (!approved && !reason) { toast(t.moderation.reason, 'error'); return; }
    try {
      await post(`/admin/submissions/${item.id}/review`, { approved, reason: approved ? undefined : reason });
      toast(approved ? t.moderation.approvedDone : t.moderation.rejectedDone);
      el.remove();
    } catch (error) { toast(error instanceof ApiError ? error.message : t.errors.network, 'error'); }
  }

  return { activate: load };
}
