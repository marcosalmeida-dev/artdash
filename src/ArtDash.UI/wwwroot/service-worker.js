// ArtDash service worker — network-first, no offline cache (the app is a live server circuit).
//
// The one thing it does handle is a failed *navigation*: launching the installed app while the server is
// restarting would show the browser's network-error page, which in standalone mode has no address bar to
// retry from. A self-contained holding page waits for /healthz and then loads the app for real.
// Every other request is left to the browser untouched.

self.addEventListener("install", () => {
    self.skipWaiting();
});

self.addEventListener("activate", (event) => {
    event.waitUntil((async () => {
        if (self.registration.navigationPreload) {
            try {
                await self.registration.navigationPreload.enable();
            } catch {
                // Not supported (Safari): navigations fall through to fetch() below.
            }
        }

        await self.clients.claim();
    })());
});

// Statuses that mean "the app cannot render right now, but it will". Anything else (404 above all) is a
// real answer and passes straight through.
const RECOVERABLE_STATUSES = new Set([408, 425, 429, 500, 502, 503, 504]);

self.addEventListener("fetch", (event) => {
    if (event.request.mode !== "navigate") {
        return;
    }

    event.respondWith((async () => {
        try {
            const response = (await event.preloadResponse) || (await fetch(event.request));
            return RECOVERABLE_STATUSES.has(response.status) ? holdingResponse(event.request.url) : response;
        } catch {
            return holdingResponse(event.request.url);
        }
    })());
});

function holdingResponse(url) {
    return new Response(holdingPage(url), {
        status: 503,
        headers: { "Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store" }
    });
}

// Self-contained: rendered precisely when nothing can be fetched, so no stylesheet, script or image links.
function holdingPage(target) {
    const en = (self.navigator.language || "pt").toLowerCase().startsWith("en");
    const copy = en
        ? {
            title: "Reconnecting...",
            serverMessage: "The server is being updated.",
            serverDetail: "This page will open by itself as soon as it is back.",
            offlineMessage: "You are offline.",
            offlineDetail: "This page will open by itself as soon as the connection is back.",
            stuckMessage: "The app is not responding.",
            stuckDetail: "Please try again in a moment.",
            retry: "Try again"
        }
        : {
            title: "Reconectando...",
            serverMessage: "O servidor está sendo atualizado.",
            serverDetail: "Esta página abrirá sozinha assim que ele voltar.",
            offlineMessage: "Você está sem conexão.",
            offlineDetail: "Esta página abrirá sozinha assim que a internet voltar.",
            stuckMessage: "O aplicativo não está respondendo.",
            stuckDetail: "Tente novamente em alguns instantes.",
            retry: "Tentar novamente"
        };

    return `<!DOCTYPE html>
<html lang="${en ? "en" : "pt-BR"}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>${copy.title}</title>
<style>
  body { margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center;
         background:#111827; color:#fff; font-family:system-ui,"Segoe UI",Roboto,sans-serif;
         text-align:center; padding:1.5rem; }
  .s { width:2.25rem; height:2.25rem; margin:0 auto 1rem; border-radius:50%;
       border:3px solid rgba(255,255,255,.25); border-top-color:#fff; animation:spin .9s linear infinite; }
  p { margin:.25rem 0; }
  .d { font-size:.875rem; color:#cbd5e1; }
  button { margin-top:1rem; padding:.6rem 1.5rem; border:0; border-radius:2rem; font:inherit; font-weight:600; cursor:pointer; }
  @keyframes spin { to { transform:rotate(360deg); } }
</style>
</head>
<body>
  <div>
    <div class="s" id="sp"></div>
    <p id="m" role="status" aria-live="polite"></p>
    <p class="d" id="d"></p>
    <button id="r" type="button" hidden>${copy.retry}</button>
  </div>
  <script>
    const target = ${JSON.stringify(target)};
    const copy = ${JSON.stringify(copy)};
    const BUDGET = 3, WINDOW_MS = 120000, KEY = "artdash.holding.reloads";
    let delay = 1500, timer = null;

    function render() {
      const offline = navigator.onLine === false;
      document.getElementById("m").textContent = offline ? copy.offlineMessage : copy.serverMessage;
      document.getElementById("d").textContent = offline ? copy.offlineDetail : copy.serverDetail;
    }

    // /healthz answering does not guarantee the navigation renders; without a budget that pair could loop.
    function spend() {
      const now = Date.now();
      let s = null;
      try { s = JSON.parse(sessionStorage.getItem(KEY)); } catch (e) {}
      if (!s || typeof s.n !== "number" || now - s.t > WINDOW_MS) { s = { n: 0, t: now }; }
      if (s.n >= BUDGET) { return false; }
      s.n++;
      try { sessionStorage.setItem(KEY, JSON.stringify(s)); } catch (e) {}
      return true;
    }

    function giveUp() {
      clearTimeout(timer);
      document.getElementById("sp").hidden = true;
      document.getElementById("m").textContent = copy.stuckMessage;
      document.getElementById("d").textContent = copy.stuckDetail;
      document.getElementById("r").hidden = false;
    }

    async function attempt() {
      clearTimeout(timer);
      render();
      if (navigator.onLine !== false) {
        try {
          const r = await fetch("/healthz?_=" + Date.now(), { cache: "no-store", credentials: "omit" });
          if (r.ok) {
            if (spend()) { location.replace(target); return; }
            giveUp();
            return;
          }
        } catch (e) { /* still down */ }
      }
      delay = Math.min(Math.round(delay * 1.35), 10000);
      timer = setTimeout(attempt, delay);
    }

    function attemptNow() { delay = 1500; attempt(); }

    document.getElementById("r").addEventListener("click", () => {
      try { sessionStorage.removeItem(KEY); } catch (e) {}
      location.replace(target);
    });
    window.addEventListener("online", attemptNow);
    window.addEventListener("offline", render);
    document.addEventListener("visibilitychange", () => { if (!document.hidden) attemptNow(); });
    attempt();
  <\/script>
</body>
</html>`;
}
