// Browser-side transport for the authentication endpoints (ported from artstore-fs).
//
// These calls cannot go through the circuit's server-side HttpClient:
//  1. The refresh token is an HttpOnly cookie. Only the browser can receive it from Set-Cookie and send it
//     back — a request issued by the server would strand the cookie on the server.
//  2. Sign-in is rate-limited by client IP; a server-issued request carries the server's own address.
//
// The C# side does all JSON serialization; this module only moves strings.

const REQUEST_TIMEOUT_MS = 20000;

/**
 * Sends a same-origin request.
 * @param {string} path origin-relative path, starting with "/"
 * @param {string} method HTTP method
 * @param {string|null} jsonBody pre-serialized body, or null
 * @returns {Promise<{status: number, body: string}>} status 0 = the request never completed
 */
export async function send(path, method, jsonBody) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    const init = {
        method,
        // Stores and sends the HttpOnly refresh cookie; same-origin keeps it off any cross-origin redirect.
        credentials: 'same-origin',
        headers: { 'Accept': 'application/json' },
        cache: 'no-store',
        signal: controller.signal
    };

    if (jsonBody !== null && jsonBody !== undefined) {
        init.headers['Content-Type'] = 'application/json';
        init.body = jsonBody;
    }

    try {
        const response = await fetch(path, init);
        const body = await response.text();
        return { status: response.status, body };
    } catch {
        // Offline, aborted or blocked. Status 0 must not be mistaken for "rejected" (that would sign the user out).
        return { status: 0, body: '' };
    } finally {
        clearTimeout(timeout);
    }
}
