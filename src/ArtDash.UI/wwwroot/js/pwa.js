// PWA plumbing: service-worker registration and the install prompt.
//
// Loaded as a module from <head> (App.razor): the beforeinstallprompt listener has to be attached before
// Chrome fires the event, and that event is the only handle to the install prompt the page will ever get.
// The driver app is the main reason to install: a home-screen icon, full screen, and GPS that keeps
// working with the screen on (geo.js holds a wake lock).

let deferredPrompt = null;

if ("serviceWorker" in navigator) {
    window.addEventListener("load", async () => {
        try {
            await navigator.serviceWorker.register("/service-worker.js", { scope: "/" });
        } catch {
            // No service worker (insecure context, private mode): the app works the same, just not installable.
        }
    });
}

window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferredPrompt = event;
    window.dispatchEvent(new CustomEvent("artdash:install-available"));
});

window.addEventListener("appinstalled", () => {
    deferredPrompt = null;
    window.dispatchEvent(new CustomEvent("artdash:installed"));
});

/** Whether the page runs as an installed app (any display mode the manifest can resolve to, or iOS). */
export function isInstalled() {
    const modes = ["standalone", "fullscreen", "minimal-ui", "window-controls-overlay"];
    return modes.some((mode) => window.matchMedia(`(display-mode: ${mode})`).matches)
        || window.navigator.standalone === true;
}

/** iOS has no install prompt: installation is Share → "Adicionar à Tela de Início". */
export function isIos() {
    const ua = window.navigator.userAgent || "";
    return /iPad|iPhone|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
}

/** Everything a page needs to decide whether to offer installation, in one round trip. */
export function getInstallState() {
    return {
        installed: isInstalled(),
        promptAvailable: deferredPrompt !== null,
        ios: isIos()
    };
}

/** Shows the browser's install prompt. A prompt can only be used once, so it is cleared before awaiting. */
export async function promptInstall() {
    if (!deferredPrompt) {
        return { success: false, outcome: "no-prompt" };
    }

    const prompt = deferredPrompt;
    deferredPrompt = null;
    prompt.prompt();
    const { outcome } = await prompt.userChoice;
    return { success: outcome === "accepted", outcome };
}
