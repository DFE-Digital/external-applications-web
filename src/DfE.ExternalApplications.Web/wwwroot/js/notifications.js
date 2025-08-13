const API_BASE = document.querySelector('meta[name="api-base"]')?.content ?? "";

const container = () => document.getElementById('notification-container');

function mapTypeToCss(type) {
    const t = (type || '').toLowerCase();
    if (t === 'error') return { banner: 'error-summary', css: 'govuk-error-summary', title: 'There is a problem' };
    if (t === 'warning') return { banner: 'notification-banner', css: 'govuk-notification-banner--warning', title: 'Warning' };
    if (t === 'success') return { banner: 'notification-banner', css: 'govuk-notification-banner--success', title: 'Success' };
    return { banner: 'notification-banner', css: 'govuk-notification-banner', title: 'Information' };
}

function ensureElement(id) {
    let el = document.getElementById(id);
    if (!el) {
        el = document.createElement('div');
        el.id = id;
        container()?.appendChild(el);
    }
    return el;
}

window.renderOrUpdate = function (n) {
    if (!n || !n.id) return;
    const map = mapTypeToCss(n.type);
    const id = `notification-${n.id}`;
    const wrapper = ensureElement(id);
    wrapper.className = `govuk-${map.banner} ${map.css} notification-item`;
    wrapper.setAttribute('role', 'alert');
    wrapper.setAttribute('data-module', `govuk-${map.banner}`);
    wrapper.setAttribute('data-notification-id', n.id);
    wrapper.setAttribute('data-auto-dismiss', (n.autoDismiss ? 'true' : 'false'));
    wrapper.setAttribute('data-auto-dismiss-seconds', (n.autoDismissSeconds ?? 0));

    if (map.banner === 'error-summary') {
        wrapper.innerHTML = `
            <div role="alert">
                <h2 class="govuk-error-summary__title">${map.title}</h2>
                <div class="govuk-error-summary__body">
                    <p class="govuk-body">${n.message ?? ''}</p>
                </div>
            </div>
            <a href="#" class="notification-close-btn" aria-label="Close notification" title="Close notification">×</a>
        `;
    } else {
        wrapper.innerHTML = `
            <div class="govuk-notification-banner__header">
                <h2 class="govuk-notification-banner__title">${map.title}</h2>
            </div>
            <div class="govuk-notification-banner__content">
                <p class="govuk-notification-banner__heading">${n.message ?? ''}</p>
            </div>
            <a href="#" class="notification-close-btn" aria-label="Close notification" title="Close notification">×</a>
        `;
    }

    const closeBtn = wrapper.querySelector('.notification-close-btn');
    if (closeBtn) {
        closeBtn.onclick = async (e) => {
            e.preventDefault();
            await dismiss(n.id);
        };
    }

    // Mark as read immediately on render
    void markAsRead(n.id);

    // Auto-dismiss if configured
    if (n.autoDismiss) {
        const secs = Number(n.autoDismissSeconds ?? 5);
        setTimeout(() => dismiss(n.id), secs * 1000);
    }
};

window.removeFromUi = function (id) {
    const el = document.getElementById(`notification-${id}`);
    if (!el) return;
    el.style.transition = 'opacity 0.3s ease-out';
    el.style.opacity = '0';
    setTimeout(() => el.remove(), 300);
};

window.clearUi = function () {
    const cont = container();
    if (!cont) return;
    cont.innerHTML = '';
};

async function ensureHubCookie() {
    const res = await fetch("/internal/hub-ticket", { credentials: "include" });
    if (!res.ok) return;
    const { url } = await res.json();

    await fetch(url, { credentials: "include" });
}

async function startHub() {
    try {
        await ensureHubCookie();

        const connection = new signalR.HubConnectionBuilder()
            .withUrl(`${API_BASE}/hubs/notifications`, { withCredentials: true })
            .withAutomaticReconnect()
            .build();

        connection.on("notification.upserted", n => window.renderOrUpdate(n));
        connection.on("notification.dismissed", ({ id }) => window.removeFromUi(id));
        connection.on("notification.cleared", () => window.clearUi());

        connection.onreconnecting(async () => { await ensureHubCookie(); });
        connection.onclose(async () => {
            try {
                await ensureHubCookie();
                await connection.start();
            } catch { /* optional backoff */ }
        });

        // Renew hub cookie proactively (10min cookie >>> renew every ~8 min)
        setInterval(ensureHubCookie, 8 * 60 * 1000);

        await connection.start();

        // Bootstrap unread items via front controller
        const unread = await fetch(`/notifications/unread`, { credentials: "include" })
            .then(r => r.ok ? r.json() : [])
            .catch(() => []);
        unread.forEach(n => window.renderOrUpdate(n));
    } catch {
     
    }
}

// Start on every page load
document.addEventListener("DOMContentLoaded", startHub);

async function markAsRead(id) {
    try { await fetch(`/notifications/read/${encodeURIComponent(id)}`, { method: 'POST', credentials: 'include' }); } catch { }
}

async function dismiss(id) {
    try {
        const ok = await fetch(`/notifications/remove/${encodeURIComponent(id)}`, { method: 'POST', credentials: 'include' });
        if (ok?.ok) window.removeFromUi(id);
    } catch { }
}

window.NotificationsApi = {
    markAllRead: async () => { try { await fetch('/notifications/read-all', { method: 'POST', credentials: 'include' }); } catch { } },
    clearAll: async () => { try { await fetch('/notifications/clear', { method: 'POST', credentials: 'include' }); } catch { } }
};
