// Mercado Pago Checkout Transparente (Payment Brick) — ported from artstore-fs.
//
// Security model: the card is tokenized entirely in the browser by Mercado Pago's SDK; the PAN/CVV never
// touch the circuit or the server. Only the tenant's PUBLIC key is used here — the access token stays on
// the server, encrypted at rest.

const SDK_URL = 'https://sdk.mercadopago.com/js/v2';

// container id -> { controller, disposeHint }
const bricks = new Map();

// The SDK opens an IndexedDB named 'db' (store 'dp') and upgrades it 1 -> 2 without guarding against an
// existing store, which throws ConstraintError on repeat visits. Opening it first at v2 with a correct
// migration makes the SDK skip its own upgrade path (artstore-fs fix).
function initMpDatabase() {
    return new Promise((resolve) => {
        try {
            const request = indexedDB.open('db', 2);
            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                if (event.oldVersion < 1 && !db.objectStoreNames.contains('dp')) {
                    db.createObjectStore('dp', { autoIncrement: true });
                }
            };
            request.onsuccess = (event) => { event.target.result.close(); resolve(); };
            request.onerror = () => resolve();
        } catch {
            resolve();
        }
    });
}

function loadSdk() {
    if (window.MercadoPago) return Promise.resolve();
    let tag = document.querySelector(`script[src="${SDK_URL}"]`);
    if (tag && tag.dataset.loaded === 'true') return Promise.resolve();

    return new Promise((resolve, reject) => {
        if (!tag) {
            tag = document.createElement('script');
            tag.src = SDK_URL;
            document.head.appendChild(tag);
        }
        tag.addEventListener('load', () => { tag.dataset.loaded = 'true'; resolve(); });
        tag.addEventListener('error', () => reject(new Error('Failed to load the Mercado Pago SDK')));
    });
}

// The payer e-mail is pre-filled (the signed-in customer's), so its field and the PIX e-mail instructions
// are hidden. Mercado Pago offers no flag for this and its class names are hashed per build, so only
// stable HTML semantics are targeted: the single input[type=email] in the light DOM. Scoped to
// .ad-brick--hide-email so nothing else is touched.
const STYLE_ID = 'ad-mp-brick-style';
const HINT_ATTR = 'data-ad-pix-hint';

function injectStyleOverrides() {
    if (document.getElementById(STYLE_ID)) return;
    const guard = `:not([${HINT_ATTR}]):not(:has(input, button, select, textarea, iframe))`;
    const style = document.createElement('style');
    style.id = STYLE_ID;
    style.textContent = `
        .ad-brick--hide-email div:has(> input[type="email"]),
        .ad-brick--hide-email div:has(> div > input[type="email"]),
        .ad-brick--hide-email label[for="email" i] { display: none !important; }
        .ad-brick--hide-email div:has(> input[type="email"]) ~ div${guard},
        .ad-brick--hide-email div:has(> div > input[type="email"]) ~ div${guard},
        .ad-brick--hide-email div:has(~ div > input[type="email"])${guard},
        .ad-brick--hide-email div:has(~ div > div > input[type="email"])${guard} { display: none !important; }
        .ad-brick--hide-email div:has(input[type="email"]) { transition: none !important; animation: none !important; }
        .ad-brick [${HINT_ATTR}] {
            display: block !important; padding: 4px 0 12px; font-size: .875rem; line-height: 1.4; text-align: center;
            color: var(--mud-palette-text-secondary, #6b6b6b);
        }
        .ad-brick div:has(> input[type="radio"]), .ad-brick input[type="radio"] { box-sizing: border-box !important; }`;
    document.head.appendChild(style);
}

function isPixSelected(container) {
    const radios = Array.from(container.querySelectorAll('input[type="radio"]'));
    if (!radios.length) {
        // Single-method layout: the card form renders sized iframes (PAN/CVV), PIX does not.
        return !Array.from(container.querySelectorAll('iframe')).some((f) => {
            const r = f.getBoundingClientRect();
            return r.width > 1 && r.height > 1;
        });
    }

    const marked = (el, prefix) => new RegExp(`(^|\\s)${prefix}-`).test(el?.getAttribute('class') || '');
    const isActive = (radio) => marked(radio, 'checked') || marked(radio.parentElement, 'active');
    const pix = radios.find((r) => (r.getAttribute('aria-label') || '').toLowerCase().includes('pix'));
    if (!pix) return false;
    return radios.some(isActive) ? isActive(pix) : pix.checked;
}

function findHintAnchor(container) {
    for (const email of container.querySelectorAll('input[type="email"]')) {
        let hidden = null;
        for (let node = email.parentElement; node && node !== container; node = node.parentElement) {
            if (getComputedStyle(node).display === 'none') hidden = node;
        }
        const host = hidden?.parentElement;
        if (host?.getClientRects().length) return hidden;
    }
    return null;
}

/** Puts a short hint where the hidden e-mail field was, so the region PIX expands is not empty. */
function syncPixHint(container, hintText) {
    const anchor = hintText && isPixSelected(container) ? findHintAnchor(container) : null;
    const hints = Array.from(container.querySelectorAll(`[${HINT_ATTR}]`));
    const placed = anchor ? hints.find((h) => h.previousElementSibling === anchor) : null;
    hints.filter((h) => h !== placed).forEach((h) => h.remove());
    if (!anchor || placed) return;

    const hint = document.createElement('div');
    hint.setAttribute(HINT_ATTR, '');
    hint.textContent = hintText;
    anchor.parentElement.insertBefore(hint, anchor.nextSibling);
}

function watchPixHint(containerId, hintText) {
    const container = document.getElementById(containerId);
    if (!container || !hintText) return null;

    let frame = 0;
    let trailing = 0;
    const sync = () => syncPixHint(container, hintText);
    const schedule = () => {
        if (!frame) frame = requestAnimationFrame(() => { frame = 0; sync(); });
        clearTimeout(trailing);
        trailing = setTimeout(sync, 350);
    };

    const observer = new MutationObserver(schedule);
    observer.observe(container, { childList: true, subtree: true, attributes: true, attributeFilter: ['class', 'checked'] });
    container.addEventListener('click', schedule, true);
    schedule();

    return () => {
        observer.disconnect();
        container.removeEventListener('click', schedule, true);
        cancelAnimationFrame(frame);
        clearTimeout(trailing);
    };
}

/**
 * Mounts the Payment Brick into `containerId`.
 * @param {object} options { amount, publicKey, payerEmail, credit, debit, pix, maxInstallments, pixHint, locale }
 * @param {object} dotNet  DotNetObjectReference exposing OnBrickReady / OnBrickError / OnBrickSubmit
 */
export async function initPaymentBrick(containerId, options, dotNet) {
    await initMpDatabase();
    await loadSdk();
    injectStyleOverrides();

    const container = document.getElementById(containerId);
    if (!container) return false;
    container.classList.add('ad-brick');
    if (options.payerEmail) container.classList.add('ad-brick--hide-email');

    const mp = new window.MercadoPago(options.publicKey, { locale: options.locale || 'pt-BR' });
    const paymentMethods = { maxInstallments: Math.max(1, options.maxInstallments || 1) };
    if (options.credit) paymentMethods.creditCard = 'all';
    if (options.debit) paymentMethods.debitCard = 'all';
    if (options.pix) paymentMethods.bankTransfer = ['pix'];

    const initialization = { amount: Number(options.amount) };
    if (options.payerEmail) initialization.payer = { email: options.payerEmail };

    const controller = await mp.bricks().create('payment', containerId, {
        initialization,
        customization: {
            visual: { style: { theme: options.dark ? 'dark' : 'default' }, hideFormTitle: true },
            paymentMethods
        },
        callbacks: {
            onReady: () => dotNet.invokeMethodAsync('OnBrickReady'),
            onError: (error) => dotNet.invokeMethodAsync('OnBrickError', JSON.stringify(error || {})),
            onSubmit: ({ selectedPaymentMethod, formData }) =>
                dotNet.invokeMethodAsync('OnBrickSubmit', JSON.stringify({ selectedPaymentMethod, formData }))
                    .then((outcome) => (outcome === 'approved' || outcome === 'pending') ? undefined : Promise.reject())
                    // Rejecting re-enables the Brick for another attempt.
                    .catch(() => Promise.reject())
        }
    });

    bricks.set(containerId, { controller, disposeHint: watchPixHint(containerId, options.pixHint) });
    return true;
}

export async function destroy(containerId) {
    const entry = bricks.get(containerId);
    if (entry) {
        entry.disposeHint?.();
        try { await entry.controller.unmount(); } catch { /* already gone */ }
        bricks.delete(containerId);
    }
    const element = document.getElementById(containerId);
    if (element) element.innerHTML = '';
}
