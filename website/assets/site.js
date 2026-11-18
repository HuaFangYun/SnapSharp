// All product information and navigation remain available without JavaScript.
document.documentElement.classList.replace('no-js', 'js');
const menu = document.querySelector('.menu-toggle');
const navigation = document.querySelector('#navigation');
menu?.addEventListener('click', () => {
  const open = menu.getAttribute('aria-expanded') !== 'true';
  menu.setAttribute('aria-expanded', String(open));
  navigation.classList.toggle('is-open', open);
});
navigation?.addEventListener('click', (event) => {
  if (event.target.closest('a')) {
    menu?.setAttribute('aria-expanded', 'false');
    navigation.classList.remove('is-open');
  }
});

// Upgrade the complete screenshot gallery to accessible, keyboard-operated tabs.
const gallery = document.querySelector('[data-gallery]');
if (gallery) {
  const tablist = gallery.querySelector('[data-tabs]');
  const tabs = [...tablist.querySelectorAll('a')];
  const panels = [...gallery.querySelectorAll('[data-panel]')];
  tablist.setAttribute('role', 'tablist');
  const activate = (selected, focus = false) => {
    tabs.forEach((tab, index) => {
      const active = index === selected;
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      panels[index].hidden = !active;
    });
    if (focus) tabs[selected].focus();
  };
  tabs.forEach((tab, index) => {
    tab.id = `tab-${index}`;
    tab.setAttribute('role', 'tab');
    tab.setAttribute('aria-controls', panels[index].id);
    panels[index].setAttribute('role', 'tabpanel');
    panels[index].setAttribute('aria-labelledby', tab.id);
    panels[index].tabIndex = 0;
    tab.addEventListener('click', (event) => { event.preventDefault(); activate(index); });
    tab.addEventListener('keydown', (event) => {
      let next;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
      if (event.key === 'Home') next = 0;
      if (event.key === 'End') next = tabs.length - 1;
      if (next !== undefined) { event.preventDefault(); activate(next, true); }
    });
  });
  activate(0);
}

const dialog = document.querySelector('.image-dialog');
let imageTrigger;
if (dialog && typeof dialog.showModal === 'function') {
  document.querySelectorAll('[data-zoom]').forEach((link) => {
    link.addEventListener('click', (event) => {
      event.preventDefault();
      imageTrigger = link;
      const source = link.querySelector('img');
      const image = dialog.querySelector('img');
      image.src = link.href;
      image.alt = source.alt;
      image.width = Number(source.getAttribute('width'));
      image.height = Number(source.getAttribute('height'));
      dialog.querySelector('p').textContent = link.dataset.caption || source.alt;
      dialog.showModal();
    });
  });
  dialog.querySelector('button').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', (event) => { if (event.target === dialog) dialog.close(); });
  dialog.addEventListener('close', () => imageTrigger?.focus());
}

// The landing page uses small, honest product illustrations to make workflows
// tangible without pretending that the browser is running the desktop models.
document.querySelectorAll('.compare-range').forEach((range) => {
  const comparison = range.closest('.comparison');
  const update = () => {
    comparison.style.setProperty('--split', `${range.value}%`);
  };
  range.addEventListener('input', update);
  update();
});

document.querySelectorAll('.annotation-toolbar button').forEach((button) => {
  button.addEventListener('click', () => {
    const surface = button.closest('.annotation-demo').querySelector('.annotation-surface');
    document.querySelectorAll('.annotation-toolbar button').forEach((item) => item.setAttribute('aria-pressed', String(item === button)));
    surface.dataset.activeTool = button.dataset.tool;
  });
});

document.querySelectorAll('[data-draggable]').forEach((card) => {
  let dragging = false;
  let origin = null;
  const initial = new DOMMatrix(getComputedStyle(card).transform);
  const rotation = Math.atan2(initial.b, initial.a) * 180 / Math.PI;
  const offset = { x: 0, y: 0 };
  const place = (x, y) => {
    const bounds = card.parentElement.getBoundingClientRect();
    const rect = card.getBoundingClientRect();
    const dx = Math.min(bounds.right - rect.right - 2, Math.max(bounds.left - rect.left + 2, x - offset.x));
    const dy = Math.min(bounds.bottom - rect.bottom - 2, Math.max(bounds.top - rect.top + 2, y - offset.y));
    offset.x += dx; offset.y += dy;
    card.style.transform = `translate(${offset.x}px, ${offset.y}px) rotate(${rotation}deg)`;
  };
  const move = (event) => {
    if (!dragging || !origin) return;
    const x = event.clientX - origin.x;
    const y = event.clientY - origin.y;
    place(x, y);
  };
  const stop = (event) => { dragging = false; origin = null; if (card.hasPointerCapture?.(event.pointerId)) card.releasePointerCapture(event.pointerId); };
  card.addEventListener('pointerdown', (event) => {
    if (event.button !== 0) return;
    dragging = true;
    origin = { x: event.clientX - offset.x, y: event.clientY - offset.y };
    card.setPointerCapture?.(event.pointerId);
  });
  card.addEventListener('pointermove', move);
  card.addEventListener('pointerup', stop);
  card.addEventListener('pointercancel', stop);
  card.addEventListener('keydown', (event) => {
    const amount = event.shiftKey ? 10 : 3;
    const x = event.key === 'ArrowRight' ? amount : event.key === 'ArrowLeft' ? -amount : 0;
    const y = event.key === 'ArrowDown' ? amount : event.key === 'ArrowUp' ? -amount : 0;
    if (x || y) {
      event.preventDefault();
      place(offset.x + x, offset.y + y);
    }
  });
});
