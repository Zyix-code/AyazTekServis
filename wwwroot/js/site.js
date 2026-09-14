(() => {
    const html = document.documentElement;
    const themeButton = document.getElementById('themeToggle');
    const themeForm = document.getElementById('themeToggleForm');
    const themeInput = document.getElementById('themeTogglePreference');

    const resolveTheme = preference => {
        const globalPreference = html.dataset.defaultTheme || 'system';
        const selected = preference === 'system' ? globalPreference : preference;
        return selected === 'system'
            ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
            : selected;
    };

    const applyTheme = (preference, animate = false) => {
        const resolved = resolveTheme(preference);
        html.dataset.userTheme = preference;
        html.dataset.resolvedTheme = resolved;
        html.setAttribute('data-bs-theme', resolved);
        if (themeButton) {
            themeButton.classList.toggle('is-dark', resolved === 'dark');
            themeButton.classList.toggle('is-light', resolved === 'light');
            themeButton.classList.toggle('theme-animate', animate);
            themeButton.setAttribute('aria-pressed', resolved === 'dark' ? 'true' : 'false');
            themeButton.title = resolved === 'dark' ? 'Açık temaya geç' : 'Koyu temaya geç';
            if (animate) window.setTimeout(() => themeButton.classList.remove('theme-animate'), 450);
        }
    };

    applyTheme(html.dataset.userTheme || 'system');

    if (themeButton && themeForm && themeInput) {
        themeForm.addEventListener('submit', async event => {
            event.preventDefault();
            const currentResolved = html.getAttribute('data-bs-theme') || resolveTheme(html.dataset.userTheme || 'system');
            const nextPreference = currentResolved === 'dark' ? 'light' : 'dark';
            const previousPreference = html.dataset.userTheme || 'system';
            themeInput.value = nextPreference;
            applyTheme(nextPreference, true);

            try {
                const formData = new FormData(themeForm);
                const response = await fetch(themeForm.action, {
                    method: 'POST',
                    body: formData,
                    headers: { 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin'
                });
                if (!response.ok) throw new Error('Tema kaydedilemedi');
            } catch {
                themeInput.value = previousPreference;
                applyTheme(previousPreference, true);
            }
        });
    }

    const systemTheme = window.matchMedia('(prefers-color-scheme: dark)');
    systemTheme.addEventListener?.('change', () => {
        if ((html.dataset.userTheme || 'system') === 'system') applyTheme('system', true);
    });


    const uppercaseFields = new Set(['Sender','Brand','Model','ProductType','SerialNumber','SentToService','FaultReason','Accessories','Notes','ControlNotes','ServiceResult','serviceResult','notes']);
    const needsUppercase = element => {
        if (!(element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)) return false;
        if (uppercaseFields.has(element.name)) return true;
        if (element.dataset?.field && ['PartName','SerialNumber','Notes'].includes(element.dataset.field)) return true;
        return /^Parts\[\d+\]\.(PartName|SerialNumber|Notes)$/.test(element.name || '');
    };
    const uppercaseValue = element => {
        if (!needsUppercase(element) || !element.value) return;
        const start = element.selectionStart, end = element.selectionEnd;
        const upper = element.value.toLocaleUpperCase('tr-TR');
        if (upper === element.value) return;
        element.value = upper;
        try { if (start !== null && end !== null) element.setSelectionRange(start, end); } catch {}
    };
    document.querySelectorAll('input,textarea').forEach(uppercaseValue);
    document.addEventListener('input', event => uppercaseValue(event.target));
    document.addEventListener('change', event => uppercaseValue(event.target));

    document.querySelectorAll('.app-alert.alert-success, .app-alert.alert-warning').forEach(alert => {
        const delay = alert.classList.contains('alert-warning') ? 8000 : 5000;
        window.setTimeout(() => {
            alert.style.transition = 'opacity .2s ease, transform .2s ease';
            alert.style.opacity = '0';
            alert.style.transform = 'translateY(-4px)';
            window.setTimeout(() => alert.remove(), 220);
        }, delay);
    });

    const sidebar = document.getElementById('sidebar');
    const toggle = document.getElementById('sidebarToggle');
    if (!sidebar || !toggle) return;

    const closeSidebar = () => {
        sidebar.classList.remove('open');
        document.body.classList.remove('sidebar-open');
        toggle.setAttribute('aria-expanded', 'false');
    };

    toggle.setAttribute('aria-controls', 'sidebar');
    toggle.setAttribute('aria-expanded', 'false');
    toggle.addEventListener('click', () => {
        const open = !sidebar.classList.contains('open');
        sidebar.classList.toggle('open', open);
        document.body.classList.toggle('sidebar-open', open);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });
    document.addEventListener('click', event => {
        if (document.body.classList.contains('sidebar-open') && !sidebar.contains(event.target) && !toggle.contains(event.target)) closeSidebar();
    });
    sidebar.addEventListener('click', event => {
        if (window.innerWidth < 992 && event.target.closest('a')) closeSidebar();
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') closeSidebar(); });
    window.addEventListener('resize', () => { if (window.innerWidth >= 992) closeSidebar(); }, { passive: true });
})();
