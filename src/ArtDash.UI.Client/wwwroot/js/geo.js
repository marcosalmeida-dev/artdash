// Driver GPS: watchPosition + a screen Wake Lock, throttled before anything crosses the circuit.
//
// A fix is forwarded to .NET when either enough time has passed or the phone moved far enough — a parked
// truck does not flood the server, a moving one is followed closely. The Wake Lock keeps the screen (and
// with it the page and its GPS) alive while the run screen is open; it is re-acquired when the page
// comes back to the foreground, since browsers release it on every visibility change.

let watchId = null;
let dotNet = null;
let wakeLock = null;
let lastSent = null; // { lat, lng, at }
let minIntervalMs = 10000;
let minDistanceM = 25;

function distanceMeters(a, b) {
    const R = 6371000;
    const toRad = (d) => d * Math.PI / 180;
    const dLat = toRad(b.lat - a.lat);
    const dLng = toRad(b.lng - a.lng);
    const h = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) * Math.sin(dLng / 2) ** 2;
    return 2 * R * Math.asin(Math.sqrt(h));
}

async function acquireWakeLock() {
    try {
        if ('wakeLock' in navigator && document.visibilityState === 'visible') {
            wakeLock = await navigator.wakeLock.request('screen');
        }
    } catch {
        wakeLock = null; // Not supported or refused (battery saver): GPS still works while the screen is on.
    }
}

function onVisibilityChange() {
    if (watchId !== null && document.visibilityState === 'visible') {
        acquireWakeLock();
    }
}

function onPosition(position) {
    if (!dotNet) return;

    const { latitude, longitude, accuracy, speed, heading } = position.coords;
    const now = position.timestamp || Date.now();
    const current = { lat: latitude, lng: longitude, at: now };

    const due = !lastSent
        || now - lastSent.at >= minIntervalMs
        || distanceMeters(lastSent, current) >= minDistanceM;

    if (!due) return;

    lastSent = current;
    dotNet.invokeMethodAsync('OnPosition', latitude, longitude,
        Number.isFinite(accuracy) ? accuracy : null,
        Number.isFinite(speed) ? speed : null,
        Number.isFinite(heading) ? heading : null,
        now).catch(() => { /* circuit gone */ });
}

function onError(error) {
    dotNet?.invokeMethodAsync('OnError', error?.code ?? 2).catch(() => { });
}

export function start(reference, intervalMs, distanceM) {
    if (!('geolocation' in navigator)) return false;

    stop();
    dotNet = reference;
    minIntervalMs = intervalMs || 10000;
    minDistanceM = distanceM || 25;
    lastSent = null;

    watchId = navigator.geolocation.watchPosition(onPosition, onError, {
        enableHighAccuracy: true,
        maximumAge: 5000,
        timeout: 30000
    });

    acquireWakeLock();
    document.addEventListener('visibilitychange', onVisibilityChange);
    return true;
}

export function stop() {
    if (watchId !== null) {
        navigator.geolocation.clearWatch(watchId);
        watchId = null;
    }

    document.removeEventListener('visibilitychange', onVisibilityChange);
    wakeLock?.release().catch(() => { });
    wakeLock = null;
    dotNet = null;
}
