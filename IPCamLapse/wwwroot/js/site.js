window.ipCamLapseFetch = function (input, init = {}) {
    const method = (init.method || 'GET').toUpperCase();
    const headers = new Headers(init.headers || {});

    if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
        const token = document.querySelector('meta[name="csrf-token"]')?.content;
        if (token) headers.set('X-CSRF-TOKEN', token);
    }

    return fetch(input, { ...init, headers });
};
