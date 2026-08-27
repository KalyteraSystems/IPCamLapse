window.ipCamLapseFetch = function (input, init = {}) {
    const method = (init.method || 'GET').toUpperCase();
    const headers = new Headers(init.headers || {});

    if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
        const token = document.querySelector('meta[name="csrf-token"]')?.content;
        if (token) headers.set('X-CSRF-TOKEN', token);
    }

    return fetch(input, { ...init, headers });
};

(() => {
    const sidebar = document.getElementById('sidebar');
    const overlay = document.getElementById('sidebarOverlay');
    const toggles = [...document.querySelectorAll('[data-sidebar-toggle]')];
    let openTrigger = null;

    function setSidebar(open, trigger = null) {
        if (!sidebar || !overlay) return;
        sidebar.classList.toggle('show', open);
        overlay.classList.toggle('show', open);
        document.body.classList.toggle('sidebar-open', open);
        toggles.forEach(button => button.setAttribute('aria-expanded', String(open)));

        if (open) {
            openTrigger = trigger;
            sidebar.querySelector('a, button')?.focus();
        } else if (openTrigger) {
            openTrigger.focus();
            openTrigger = null;
        }
    }

    toggles.forEach(button => {
        button.addEventListener('click', () => setSidebar(!sidebar?.classList.contains('show'), button));
    });

    sidebar?.querySelectorAll('a').forEach(link => {
        link.addEventListener('click', () => {
            if (window.matchMedia('(max-width: 991.98px)').matches) setSidebar(false);
        });
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && sidebar?.classList.contains('show')) setSidebar(false);
    });

    window.matchMedia('(min-width: 992px)').addEventListener('change', event => {
        if (event.matches) setSidebar(false);
    });

    window.ipCamLapseNotify = function (message, type = 'error') {
        const region = document.getElementById('toast-region');
        if (!region) return;

        const toast = document.createElement('div');
        toast.className = `app-toast is-${type}`;
        toast.setAttribute('role', type === 'error' ? 'alert' : 'status');

        const icon = document.createElement('i');
        icon.className = `bi ${type === 'success' ? 'bi-check-circle' : 'bi-exclamation-circle'} app-toast-icon`;
        icon.setAttribute('aria-hidden', 'true');

        const text = document.createElement('span');
        text.className = 'app-toast-message';
        text.textContent = message;

        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'app-toast-close';
        close.setAttribute('aria-label', 'Dismiss message');
        close.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        close.addEventListener('click', () => toast.remove());

        toast.append(icon, text, close);
        region.appendChild(toast);
        window.setTimeout(() => toast.remove(), 6500);
    };
})();
