// Leaflet + OpenStreetMap maps for LeafletMap.razor. Several maps can live on one page: each is keyed by
// its container id. Leaflet itself is loaded from a CDN in App.razor; on a slow network the circuit can
// call in before it arrives, so every entry point waits for the global `L` first (artstore-fs lesson).

const maps = new Map(); // id -> { map, markers: Map<key, marker>, trail: polyline|null }

function whenLeafletReady(timeoutMs = 8000) {
    return new Promise((resolve) => {
        if (typeof L !== 'undefined') { resolve(true); return; }
        const start = Date.now();
        const timer = setInterval(() => {
            if (typeof L !== 'undefined') { clearInterval(timer); resolve(true); }
            else if (Date.now() - start > timeoutMs) { clearInterval(timer); resolve(false); }
        }, 100);
    });
}

function cssVar(name, fallback) {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text ?? '';
    return div.innerHTML;
}

const VEHICLE_SVG = '<svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor" aria-hidden="true">'
    + '<path d="M20 8h-3V4H3c-1.1 0-2 .9-2 2v11h2c0 1.66 1.34 3 3 3s3-1.34 3-3h6c0 1.66 1.34 3 3 3s3-1.34 3-3h2v-5l-3-4zM6 18.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm13.5-9 1.96 2.5H17V9.5h2.5zM18 18.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/></svg>';

const STORE_SVG = '<svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true">'
    + '<path d="M20 4H4v2h16V4zm1 10v-2l-1-5H4l-1 5v2h1v6h10v-6h4v6h2v-6h1zm-9 4H6v-4h6v4z"/></svg>';

/** Teardrop pin with a centred label (stop number, "A"). */
function pin(color, label) {
    return L.divIcon({
        className: 'ad-map-pin',
        html: `<div class="ad-map-pin__drop" style="background:${color}"></div>`
            + `<span class="ad-map-pin__label">${escapeHtml(label ?? '')}</span>`,
        iconSize: [30, 42],
        iconAnchor: [15, 30],
        popupAnchor: [0, -28]
    });
}

function iconFor(marker) {
    const primary = cssVar('--ad-primary', '#2F6FED');
    switch (marker.kind) {
        case 'vehicle':
            return L.divIcon({
                className: 'ad-map-vehicle',
                html: `<div class="ad-map-vehicle__dot" style="background:${primary}">${VEHICLE_SVG}</div>`
                    + (marker.label ? `<span class="ad-map-vehicle__tag">${escapeHtml(marker.label)}</span>` : ''),
                iconSize: [36, 36],
                iconAnchor: [18, 18],
                popupAnchor: [0, -18]
            });
        case 'me':
            return L.divIcon({
                className: 'ad-map-me',
                html: '<div class="ad-map-me__dot"></div>',
                iconSize: [18, 18],
                iconAnchor: [9, 9]
            });
        case 'store':
            return L.divIcon({
                className: 'ad-map-store',
                html: `<div class="ad-map-store__dot" style="background:${cssVar('--ad-secondary', '#0F172A')}">${STORE_SVG}</div>`,
                iconSize: [30, 30],
                iconAnchor: [15, 15],
                popupAnchor: [0, -15]
            });
        case 'stop-done':
            return pin('#2E7D32', marker.label);
        case 'stop-failed':
            return pin('#D32F2F', marker.label);
        case 'stop':
            return pin(primary, marker.label);
        default: // destination
            return pin(cssVar('--ad-accent', '#F59E0B'), marker.label ?? '');
    }
}

function addMarker(entry, marker) {
    const leafletMarker = L.marker([marker.latitude, marker.longitude], {
        icon: iconFor(marker),
        zIndexOffset: marker.kind === 'vehicle' ? 1000 : 0,
        keyboard: false
    }).addTo(entry.map);

    if (marker.popup) {
        leafletMarker.bindPopup(escapeHtml(marker.popup).replace(/\n/g, '<br>'));
    }

    leafletMarker._adKind = marker.kind;
    leafletMarker._adLabel = marker.label;
    entry.markers.set(marker.key, leafletMarker);
    return leafletMarker;
}

function fitEntry(entry) {
    const points = [];
    entry.markers.forEach((m) => points.push(m.getLatLng()));
    if (entry.trail) {
        entry.trail.getLatLngs().forEach((p) => points.push(p));
    }

    if (points.length === 1) {
        entry.map.setView(points[0], Math.max(entry.map.getZoom(), 15));
    } else if (points.length > 1) {
        entry.map.fitBounds(L.latLngBounds(points), { padding: [40, 40], maxZoom: 16 });
    }
}

export async function create(id, latitude, longitude, zoom) {
    if (!(await whenLeafletReady())) return false;

    const element = document.getElementById(id);
    if (!element) return false;

    destroy(id);

    const map = L.map(element, { zoomControl: true, attributionControl: true })
        .setView([latitude, longitude], zoom || 13);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap',
        maxZoom: 19
    }).addTo(map);

    maps.set(id, { map, markers: new Map(), trail: null });

    // A container that was just rendered (dialogs, tabs) can be measured before it has its final size.
    setTimeout(() => map.invalidateSize(), 200);
    return true;
}

/** Replaces all markers. */
export function setMarkers(id, markers, fit) {
    const entry = maps.get(id);
    if (!entry) return;

    entry.markers.forEach((m) => m.remove());
    entry.markers.clear();
    (markers || []).forEach((marker) => addMarker(entry, marker));

    if (fit) fitEntry(entry);
}

/** Adds or moves one marker (live GPS updates). */
export function upsertMarker(id, marker, follow) {
    const entry = maps.get(id);
    if (!entry) return;

    const existing = entry.markers.get(marker.key);
    if (existing && existing._adKind === marker.kind && existing._adLabel === marker.label) {
        existing.setLatLng([marker.latitude, marker.longitude]);
        if (marker.popup) existing.setPopupContent(escapeHtml(marker.popup).replace(/\n/g, '<br>'));
    } else {
        existing?.remove();
        addMarker(entry, marker);
    }

    if (follow) entry.map.panTo([marker.latitude, marker.longitude]);
}

export function setTrail(id, points, color) {
    const entry = maps.get(id);
    if (!entry) return;

    entry.trail?.remove();
    entry.trail = null;

    if (points && points.length > 1) {
        entry.trail = L.polyline(points.map((p) => [p.latitude, p.longitude]), {
            color: color || cssVar('--ad-primary', '#2F6FED'),
            weight: 4,
            opacity: 0.75
        }).addTo(entry.map);
    }
}

export function appendTrail(id, point, color) {
    const entry = maps.get(id);
    if (!entry) return;

    if (!entry.trail) {
        entry.trail = L.polyline([], { color: color || cssVar('--ad-primary', '#2F6FED'), weight: 4, opacity: 0.75 }).addTo(entry.map);
    }
    entry.trail.addLatLng([point.latitude, point.longitude]);
}

export function fit(id) {
    const entry = maps.get(id);
    if (entry) fitEntry(entry);
}

export function destroy(id) {
    const entry = maps.get(id);
    if (entry) {
        entry.map.remove();
        maps.delete(id);
    }
}
