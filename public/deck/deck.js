/* OpenQuest pitch deck: loads slides/*.html in order, keyboard/click/swipe navigation, DE/EN, fullscreen.
   ?auto=1 plays the deck with the per-slide durations (data-seconds on each <section class="slide">). */
(() => {
  const SLIDES = [
    '01-title', '02-problem', '03-sources', '04-analysis', '05-jev',
    '06-karte', '07-game', '08-mechanics', '09-video', '10-architecture',
    '11-impact', '12-team', '13-outro',
  ];
  const deck = document.getElementById('deck');
  const $ = (id) => document.getElementById(id);
  const store = {
    get: (k) => { try { return localStorage.getItem(k); } catch { return null; } },
    set: (k, v) => { try { localStorage.setItem(k, v); } catch { /* storage blocked */ } },
  };

  let slides = [];
  let index = 0;
  let lang = new URLSearchParams(location.search).get('lang') || store.get('oq-deck-lang') || 'de';
  let autoTimer = null;

  const LABELS = {
    de: { prev: 'Vorherige Folie', next: 'Nächste Folie', full: 'Vollbild' },
    en: { prev: 'Previous slide', next: 'Next slide', full: 'Fullscreen' },
  };

  function applyLang() {
    document.documentElement.lang = lang;
    document.documentElement.dataset.lang = lang;
    document.querySelectorAll('[data-set-lang]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.setLang === lang)));
    // Attributes: data-de-alt / data-en-alt, data-de-aria / data-en-aria
    document.querySelectorAll('[data-de-alt]').forEach((el) => el.setAttribute('alt', el.dataset[`${lang}Alt`] || ''));
    document.querySelectorAll('[data-de-aria]').forEach((el) => el.setAttribute('aria-label', el.dataset[`${lang}Aria`] || ''));
    $('deck-prev').setAttribute('aria-label', LABELS[lang].prev);
    $('deck-next').setAttribute('aria-label', LABELS[lang].next);
    $('deck-full').setAttribute('aria-label', LABELS[lang].full);
  }

  function show(i, { fromHash = false } = {}) {
    index = Math.max(0, Math.min(slides.length - 1, i));
    slides.forEach((s, k) => {
      s.classList.toggle('is-active', k === index);
      s.classList.toggle('is-past', k < index);
      s.setAttribute('aria-hidden', String(k !== index));
    });
    $('deck-current').textContent = String(index + 1);
    $('deck-progress-fill').style.width = `${((index + 1) / slides.length) * 100}%`;
    $('deck-prev').disabled = index === 0;
    $('deck-next').disabled = index === slides.length - 1;
    document.body.classList.toggle('on-dark', slides[index]?.classList.contains('slide-dark'));
    // Restart entry animations of the active slide.
    const active = slides[index];
    if (active) { active.classList.remove('is-entering'); void active.offsetWidth; active.classList.add('is-entering'); }
    // Pause videos on other slides.
    slides.forEach((s, k) => { if (k !== index) s.querySelectorAll('video').forEach((v) => v.pause()); });
    if (!fromHash) history.replaceState(null, '', `#${index + 1}`);
    if (autoTimer) scheduleAuto();
  }

  const next = () => show(index + 1);
  const prev = () => show(index - 1);

  function scheduleAuto() {
    clearTimeout(autoTimer);
    const secs = Number(slides[index]?.dataset.seconds || 10);
    if (index < slides.length - 1) autoTimer = setTimeout(next, secs * 1000);
  }

  async function load() {
    const html = await Promise.all(SLIDES.map((name) => fetch(`slides/${name}.html`, { cache: 'no-cache' })
      .then((r) => (r.ok ? r.text() : `<section class="slide"><h2 class="display">${name}</h2></section>`))
      .catch(() => `<section class="slide"><h2 class="display">${name}</h2></section>`)));
    deck.innerHTML = html.join('\n');
    // Scripts inserted via innerHTML don't run; recreate them so slide fragments can carry small behaviour.
    deck.querySelectorAll('script').forEach((old) => {
      const s = document.createElement('script');
      s.textContent = old.textContent;
      old.replaceWith(s);
    });
    slides = [...deck.querySelectorAll(':scope > .slide')];
    $('deck-total').textContent = String(slides.length);
    applyLang();
    const fromHash = Number(location.hash.slice(1));
    show(Number.isFinite(fromHash) && fromHash > 0 ? fromHash - 1 : 0, { fromHash: true });
    if (new URLSearchParams(location.search).get('auto') === '1') { autoTimer = -1; scheduleAuto(); }
    document.dispatchEvent(new CustomEvent('deck:ready'));
  }

  document.querySelectorAll('[data-set-lang]').forEach((b) => b.addEventListener('click', () => {
    lang = b.dataset.setLang; store.set('oq-deck-lang', lang); applyLang();
  }));
  $('deck-next').addEventListener('click', next);
  $('deck-prev').addEventListener('click', prev);
  $('deck-full').addEventListener('click', () => {
    if (document.fullscreenElement) document.exitFullscreen?.(); else document.documentElement.requestFullscreen?.();
  });
  document.addEventListener('keydown', (e) => {
    if (e.target.closest?.('input, textarea')) return;
    if (['ArrowRight', 'PageDown'].includes(e.key) || (e.key === ' ' && !slides[index]?.querySelector('video'))) { e.preventDefault(); next(); }
    else if (['ArrowLeft', 'PageUp'].includes(e.key)) { e.preventDefault(); prev(); }
    else if (e.key === 'Home') show(0);
    else if (e.key === 'End') show(slides.length - 1);
    else if (e.key.toLowerCase() === 'f') $('deck-full').click();
    else if (e.key === ' ' ) { const v = slides[index].querySelector('video'); if (v) { e.preventDefault(); v.paused ? v.play() : v.pause(); } }
  });
  window.addEventListener('hashchange', () => { const n = Number(location.hash.slice(1)); if (n > 0) show(n - 1, { fromHash: true }); });
  let touchX = null;
  deck.addEventListener('touchstart', (e) => { touchX = e.touches[0].clientX; }, { passive: true });
  deck.addEventListener('touchend', (e) => {
    if (touchX === null) return;
    const dx = e.changedTouches[0].clientX - touchX; touchX = null;
    if (Math.abs(dx) > 50) (dx < 0 ? next : prev)();
  });

  load();
})();
