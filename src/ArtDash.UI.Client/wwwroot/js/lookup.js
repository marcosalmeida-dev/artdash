// Address lookups that run in the browser: ViaCEP (postal codes) and OpenStreetMap Nominatim (geocoding).
// Both are free and allow CORS; requests come from the user's own connection, a few at a time.

async function getJson(url, timeoutMs = 8000) {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const response = await fetch(url, { signal: controller.signal, headers: { 'Accept': 'application/json' } });
        return response.ok ? await response.json() : null;
    } catch {
        return null;
    } finally {
        clearTimeout(timer);
    }
}

/** ViaCEP. Returns null for an unknown CEP ({ erro: true } or "true"). */
export async function cep(digits) {
    const data = await getJson(`https://viacep.com.br/ws/${encodeURIComponent(digits)}/json/`);
    if (!data || data.erro === true || data.erro === 'true') return null;
    return data;
}

/** Nominatim: free-text search first, the CEP alone as a fallback. */
export async function geocode(query, zipCode) {
    const search = async (q) => {
        const url = `https://nominatim.openstreetmap.org/search?format=json&limit=1&countrycodes=br&q=${encodeURIComponent(q)}`;
        const results = await getJson(url);
        if (Array.isArray(results) && results.length > 0) {
            return {
                latitude: parseFloat(results[0].lat),
                longitude: parseFloat(results[0].lon),
                displayName: results[0].display_name || q
            };
        }
        return null;
    };

    return (query ? await search(query) : null) ?? (zipCode ? await search(`${zipCode}, Brasil`) : null);
}
