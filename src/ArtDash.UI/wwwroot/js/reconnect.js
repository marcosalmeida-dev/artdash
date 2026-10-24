// Circuit reconnection and self-healing after a server restart (ported from artstore-fs).
//
// The render mode is InteractiveServer with prerender:false, so everything on screen belongs to a
// SignalR circuit living in the server process. A deployment kills that process and the page the user
// is looking at stops responding. Blazor's built-in handling stops at "reconnection failed" and waits
// for a human to press reload — inside an installed PWA (the driver app!) that means force-closing it.
//
// What this file adds:
//   * a visible overlay while reconnecting (#components-reconnect-modal in App.razor),
//   * a retry schedule long enough to outlast a container restart (~106 s of attempts),
//   * automatic recovery: once the circuit is unrecoverable, poll /healthz until the new instance
//     answers and then reload by itself — gated on /healthz, so a reload never lands on the
//     browser's network-error page.

(() => {
    "use strict";

    const HEALTH_PATH = "/healthz";
    const POLL_INTERVAL_MS = 2000;
    const RETRY_SCHEDULE_MS = [0, 1000, 2000, 3000, 5000, 5000, 10000, 10000, 15000, 15000, 20000, 20000];

    // A reload that lands on a server which rejects the circuit again would loop forever: three
    // inside a minute and the user gets a button instead.
    const RELOAD_BUDGET = 3;
    const RELOAD_WINDOW_MS = 60000;
    const RELOAD_STATE_KEY = "artdash.reconnect.reloads";

    const strings = (navigator.language || "pt").toLowerCase().startsWith("en")
        ? {
            reconnecting: "Reconnecting to the server...",
            waiting: "The server is being updated.",
            waitingDetail: "This page will reload by itself as soon as it is back.",
            manual: "Could not reconnect.",
            manualDetail: "Tap to reload the page.",
            retry: "Reload"
        }
        : {
            reconnecting: "Reconectando ao servidor...",
            waiting: "O servidor está sendo atualizado.",
            waitingDetail: "Esta página será recarregada sozinha assim que ele voltar.",
            manual: "Não foi possível reconectar.",
            manualDetail: "Toque para recarregar a página.",
            retry: "Recarregar"
        };

    const modal = document.getElementById("components-reconnect-modal");
    const titleEl = document.getElementById("reconnect-title");
    const detailEl = document.getElementById("reconnect-detail");
    const retryButton = document.getElementById("reconnect-retry");

    let recovering = false;
    let lastState = "";

    function setOverlay(mode) {
        if (!modal) {
            return;
        }

        modal.classList.toggle("artdash-reconnect-waiting", mode === "waiting" || mode === "manual");
        modal.classList.toggle("artdash-reconnect-manual", mode === "manual");

        if (titleEl) {
            titleEl.textContent = mode === "manual" ? strings.manual
                : mode === "waiting" ? strings.waiting
                    : strings.reconnecting;
        }

        if (detailEl) {
            detailEl.textContent = mode === "manual" ? strings.manualDetail
                : mode === "waiting" ? strings.waitingDetail
                    : "";
        }

        if (retryButton) {
            retryButton.textContent = strings.retry;
        }
    }

    function readReloadState() {
        try {
            const raw = sessionStorage.getItem(RELOAD_STATE_KEY);
            const parsed = raw ? JSON.parse(raw) : null;
            if (parsed && typeof parsed.count === "number" && typeof parsed.first === "number") {
                return parsed;
            }
        } catch {
            // Private mode / storage disabled: losing the budget is safer than refusing to recover.
        }
        return null;
    }

    function clearReloadBudget() {
        try {
            sessionStorage.removeItem(RELOAD_STATE_KEY);
        } catch { /* ignore */ }
    }

    function reloadWithinBudget() {
        const now = Date.now();
        let state = readReloadState();

        if (!state || now - state.first > RELOAD_WINDOW_MS) {
            state = { count: 0, first: now };
        }

        if (state.count >= RELOAD_BUDGET) {
            setOverlay("manual");
            return;
        }

        state.count += 1;
        try {
            sessionStorage.setItem(RELOAD_STATE_KEY, JSON.stringify(state));
        } catch { /* ignore */ }

        location.reload();
    }

    const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    async function serverIsUp() {
        try {
            // credentials:"omit" keeps the refresh cookie out of a request that repeats every couple of
            // seconds; the cache-buster defeats any intermediary keeping a 200 from the dying instance.
            const response = await fetch(`${HEALTH_PATH}?_=${Date.now()}`, {
                cache: "no-store",
                credentials: "omit",
                redirect: "manual"
            });
            return response.ok;
        } catch {
            return false;
        }
    }

    async function recoverWhenServerReturns() {
        if (recovering) {
            return;
        }

        recovering = true;
        setOverlay("waiting");

        for (; ;) {
            // Polling a backgrounded phone drains the battery; visibilitychange kicks the loop on return.
            if (!document.hidden && await serverIsUp()) {
                reloadWithinBudget();
                return;
            }

            await delay(POLL_INTERVAL_MS);
        }
    }

    function handleState(state) {
        if (!state || state === lastState) {
            return;
        }
        lastState = state;

        switch (state) {
            case "show":
            case "retrying":
                setOverlay("reconnecting");
                break;

            case "hide":
                recovering = false;
                clearReloadBudget();
                setOverlay("reconnecting");
                break;

            case "rejected":
                // The server answered and refused the circuit: it restarted while we were connected.
                reloadWithinBudget();
                break;

            case "failed":
                void recoverWhenServerReturns();
                break;

            case "paused":
                break;
        }
    }

    if (modal) {
        modal.addEventListener("components-reconnect-state-changed", (event) => {
            handleState(event?.detail?.state);
        });

        // Fallback for the class-toggling mechanism; handleState ignores repeats, so both can fire.
        const stateClasses = ["show", "hide", "failed", "rejected", "retrying", "paused"];
        new MutationObserver(() => {
            const found = stateClasses.find((s) => modal.classList.contains(`components-reconnect-${s}`));
            if (found) {
                handleState(found);
            }
        }).observe(modal, { attributes: true, attributeFilter: ["class"] });
    }

    retryButton?.addEventListener("click", () => {
        clearReloadBudget();
        location.reload();
    });

    // A phone locked through a whole deployment burns every retry while hidden and wakes up on a dead
    // page: coming back to the foreground re-checks at once.
    document.addEventListener("visibilitychange", () => {
        if (document.hidden) {
            return;
        }

        if (recovering) {
            void serverIsUp().then((up) => up && reloadWithinBudget());
        } else if (lastState === "failed" || lastState === "rejected") {
            void recoverWhenServerReturns();
        }
    });

    window.addEventListener("online", () => {
        if (recovering) {
            void serverIsUp().then((up) => up && reloadWithinBudget());
        }
    });

    // autostart="false" on blazor.web.js: nothing renders until this runs.
    if (typeof Blazor === "undefined") {
        console.error("blazor.web.js did not load - the app cannot start. Waiting for the server.");
        void recoverWhenServerReturns();
        return;
    }

    try {
        Blazor.start({
            circuit: {
                reconnectionOptions: {
                    maxRetries: RETRY_SCHEDULE_MS.length,
                    retryIntervalMilliseconds: (previousAttempts, maxRetries) =>
                        previousAttempts >= maxRetries
                            ? null
                            : RETRY_SCHEDULE_MS[Math.min(previousAttempts, RETRY_SCHEDULE_MS.length - 1)]
                }
            }
        });
    } catch (error) {
        console.error("Blazor.start with custom reconnection options failed; using defaults.", error);
        try {
            Blazor.start();
        } catch (fallbackError) {
            console.error("Blazor.start failed outright.", fallbackError);
        }
    }
})();
