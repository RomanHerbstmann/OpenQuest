// OpenQuest admin panel: login, navigation, and the two areas (districts, moderation).

import { h, clear, toast } from './dom.js';
import { login, logout, restore, session } from './api.js';
import { mountDistricts } from './districts.js';
import { mountModeration } from './moderation.js';
import { t } from './de.js';

const loginEl = document.getElementById('login');
const appEl = document.getElementById('app');

function showLogin(message) {
  appEl.hidden = true;
  clear(appEl);
  clear(loginEl);
  const error = h('p', { class: 'problem', hidden: !message }, message ?? '');
  const form = h('form', { class: 'card login-card' },
    h('h1', null, t.title),
    h('h2', null, t.login.heading),
    h('label', null, t.login.username, h('input', { name: 'username', autocomplete: 'username', required: true })),
    h('label', null, t.login.password, h('input', { name: 'password', type: 'password', autocomplete: 'current-password', required: true })),
    error,
    h('button', { type: 'submit', class: 'primary' }, t.login.submit));
  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const data = new FormData(form);
    try {
      await login(data.get('username'), data.get('password'));
      showApp();
    } catch (e) {
      logout();
      error.textContent = e.status === 401 || e.status === 400 ? t.login.failed : e.message;
      error.hidden = false;
    }
  });
  loginEl.append(form);
  loginEl.hidden = false;
  form.elements.username.focus();
}

function showApp() {
  const role = session.me?.role;
  if (role !== 'admin' && role !== 'moderator') {
    logout();
    showLogin(t.login.needAdmin);
    return;
  }
  loginEl.hidden = true;
  clear(appEl);
  const areas = [];
  if (role === 'admin') areas.push({ id: 'districts', label: t.nav.districts, mount: mountDistricts, layout: 'split' });
  areas.push({ id: 'moderation', label: t.nav.moderation, mount: mountModeration, layout: 'page' });

  const nav = h('nav', { class: 'tabs' });
  const main = h('main', { class: 'content' });
  for (const area of areas) {
    const pane = h('div', { class: `pane ${area.layout}`, hidden: true });
    main.append(pane);
    area.pane = pane;
    area.api = area.mount(pane);
    area.button = h('button', { type: 'button', class: 'tab', onclick: () => activate(area) }, area.label);
    nav.append(area.button);
  }
  function activate(area) {
    for (const a of areas) { a.pane.hidden = a !== area; a.button.classList.toggle('active', a === area); }
    area.api.activate?.();
    location.hash = area.id;
  }

  appEl.append(
    h('header', { class: 'topbar' },
      h('strong', null, t.title), nav,
      h('span', { class: 'grow' }),
      h('span', { class: 'muted' }, `${session.me.username} · ${t.nav.role[role] ?? role}`),
      h('button', { type: 'button', onclick: () => { logout(); showLogin(); } }, t.nav.logout)),
    main);
  appEl.hidden = false;
  activate(areas.find((a) => `#${a.id}` === location.hash) ?? areas[0]);
}

window.addEventListener('session-expired', () => { logout(); showLogin(t.login.failed); });

document.title = t.title;
restore().then((me) => (me ? showApp() : showLogin())).catch(() => showLogin());
