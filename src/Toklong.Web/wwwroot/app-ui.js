(() => {
  const selector = '[data-custom-select]';
  let openSelect = null;
  let menu = null;
  let activeIndex = -1;

  function hydrateSelect(root) {
    const select = root?.querySelector('.native-select');
    const value = root?.querySelector('[data-select-value]');
    if (!select || !value) return;
    value.textContent = select.options[select.selectedIndex]?.text ?? 'กรุณาเลือก';
  }

  function hydrateAll() {
    document.querySelectorAll(selector).forEach(hydrateSelect);
  }

  function closeSelect() {
    openSelect?.querySelector('[data-select-trigger]')?.setAttribute('aria-expanded', 'false');
    menu?.remove();
    menu = null;
    openSelect = null;
    activeIndex = -1;
  }

  function positionMenu(trigger) {
    if (!menu) return;
    const rect = trigger.getBoundingClientRect();
    menu.style.left = `${rect.left}px`;
    menu.style.top = `${rect.bottom + 7}px`;
    menu.style.width = `${rect.width}px`;
    menu.style.maxHeight = `${Math.max(160, window.innerHeight - rect.bottom - 20)}px`;
  }

  function markActive(options) {
    options.forEach((option, index) => {
      option.classList.toggle('active', index === activeIndex);
      option.setAttribute('aria-selected', index === activeIndex ? 'true' : 'false');
    });
    options[activeIndex]?.scrollIntoView({ block: 'nearest' });
  }

  function choose(root, index) {
    const select = root.querySelector('.native-select');
    if (!select || index < 0 || index >= select.options.length) return;
    select.selectedIndex = index;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    hydrateSelect(root);
    closeSelect();
    root.querySelector('[data-select-trigger]')?.focus();
  }

  function open(root) {
    if (openSelect === root) {
      closeSelect();
      return;
    }
    closeSelect();
    const select = root.querySelector('.native-select');
    const trigger = root.querySelector('[data-select-trigger]');
    if (!select || !trigger) return;

    openSelect = root;
    activeIndex = Math.max(0, select.selectedIndex);
    trigger.setAttribute('aria-expanded', 'true');
    menu = document.createElement('div');
    menu.className = 'custom-select-menu';
    menu.id = trigger.getAttribute('aria-controls') || '';
    menu.setAttribute('role', 'listbox');

    [...select.options].forEach((source, index) => {
      const option = document.createElement('button');
      option.type = 'button';
      option.className = 'custom-select-option';
      option.setAttribute('role', 'option');
      option.dataset.index = `${index}`;
      option.innerHTML = `<span>${source.textContent}</span><svg width="17" height="17" viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="m6 12 4 4 8-9" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
      option.addEventListener('click', () => choose(root, index));
      menu.appendChild(option);
    });

    document.body.appendChild(menu);
    positionMenu(trigger);
    markActive([...menu.children]);
  }

  document.addEventListener('click', event => {
    const trigger = event.target.closest('[data-select-trigger]');
    if (trigger) {
      event.preventDefault();
      open(trigger.closest(selector));
      return;
    }
    if (!event.target.closest('.custom-select-menu')) closeSelect();
  });

  document.addEventListener('keydown', event => {
    const root = event.target.closest(selector);
    if (!root) {
      if (event.key === 'Escape') closeSelect();
      return;
    }
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (!openSelect) open(root);
      else choose(root, activeIndex);
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!openSelect) open(root);
      const count = menu?.children.length ?? 0;
      if (event.key === 'ArrowDown') activeIndex = (activeIndex + 1) % count;
      if (event.key === 'ArrowUp') activeIndex = (activeIndex - 1 + count) % count;
      markActive([...(menu?.children ?? [])]);
    } else if (event.key === 'Escape') {
      closeSelect();
    } else if (event.key === 'Tab') {
      closeSelect();
    }
  });

  document.addEventListener('change', event => {
    if (event.target.matches('.native-select')) hydrateSelect(event.target.closest(selector));
  });
  window.addEventListener('resize', () => {
    if (openSelect) positionMenu(openSelect.querySelector('[data-select-trigger]'));
  });
  document.addEventListener('DOMContentLoaded', hydrateAll);
  document.addEventListener('blazor:enhancedload', hydrateAll);
  setTimeout(hydrateAll, 0);

  window.toklongGuide = {
    shouldShow(key) {
      try { return localStorage.getItem(key) !== 'done'; }
      catch { return true; }
    },
    complete(key) {
      try { localStorage.setItem(key, 'done'); } catch {}
    },
    focusStep(targetId) {
      requestAnimationFrame(() => {
        const byId = document.getElementById(targetId);
        const target = byId?.offsetParent !== null
          ? byId
          : document.querySelector(`[data-tour-target="${CSS.escape(targetId)}"]`);
        if (!target) return;
        target.scrollIntoView({ behavior: 'smooth', block: 'start', inline: 'nearest' });
      });
    }
  };

  window.toklongMotion = {
    prefersReducedMotion() {
      return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }
  };

})();
