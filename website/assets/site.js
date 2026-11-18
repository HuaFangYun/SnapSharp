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
