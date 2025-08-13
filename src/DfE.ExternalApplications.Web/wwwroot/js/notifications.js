const API_BASE = document.querySelector('meta[name="api-base"]')?.content ?? "";

window.renderOrUpdate ??= function () { };
window.removeFromUi ??= function () { };
window.clearUi ??= function () { };

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

        // Bootstrap unread items
        const unread = await fetch(`${API_BASE}/api/notifications`, { credentials: "include" })
            .then(r => r.ok ? r.json() : []);
        unread.forEach(n => window.renderOrUpdate(n));
    } catch {
     
    }
}

// Start on every page load
document.addEventListener("DOMContentLoaded", startHub);
