// Small browser helpers used through BrowserUtil.

/** Copies text to the clipboard (PIX copy-and-paste code, tracking links). */
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        // Older browsers / insecure contexts: fall back to a hidden textarea.
        try {
            const area = document.createElement('textarea');
            area.value = text;
            area.setAttribute('readonly', '');
            area.style.position = 'fixed';
            area.style.opacity = '0';
            document.body.appendChild(area);
            area.select();
            const ok = document.execCommand('copy');
            area.remove();
            return ok;
        } catch {
            return false;
        }
    }
}

/** Downloads UTF-8 text with a BOM so spreadsheet applications preserve accents. */
export function downloadText(filename, content, contentType = 'text/csv;charset=utf-8') {
    let url;
    let link;
    try {
        const blob = new Blob(['\uFEFF', content], { type: contentType });
        url = URL.createObjectURL(blob);
        link = document.createElement('a');
        link.href = url;
        link.download = filename;
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        return true;
    } catch {
        return false;
    } finally {
        link?.remove();
        if (url) {
            // Allow the browser to start consuming the Blob before releasing it.
            window.setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    }
}

/** Android tints the status bar (and the installed app's title bar) with theme-color. */
export function setThemeColor(color) {
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) {
        meta.setAttribute('content', color);
    }
    return true;
}

/** Loads a Google Fonts stylesheet once (the branding editor switches fonts live). */
export function ensureFont(href) {
    if (!href) {
        return false;
    }

    const existing = Array.from(document.querySelectorAll('link[rel="stylesheet"]'))
        .some((link) => link.href === href || link.getAttribute('href') === href);

    if (!existing) {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = href;
        document.head.appendChild(link);
    }

    return true;
}

export function scrollToTop() {
    window.scrollTo({ top: 0, behavior: 'smooth' });
    return true;
}
