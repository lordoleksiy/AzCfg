// JSON schema association for the AzCfg editors. The schema is fetched same-origin (no CDN) and attached to one
// model uri at a time, so several editors can validate against their own schema.
const entries = new Map();
const schemas = new Map();

function jsonDefaults() {
    return (monaco.json ?? monaco.languages.json).jsonDefaults;
}

function apply() {
    jsonDefaults().setDiagnosticsOptions({
        validate: true,
        allowComments: false,
        enableSchemaRequest: false,
        schemas: [...entries.values()],
    });
}

async function loadSchema(url) {
    if (!schemas.has(url)) {
        const response = await fetch(url, { credentials: 'same-origin' });
        schemas.set(url, await response.json());
    }
    return schemas.get(url);
}

export async function registerSchema(schemaUrl, modelUri) {
    const schema = await loadSchema(schemaUrl);
    entries.set(modelUri, { uri: new URL(schemaUrl, document.baseURI).href, fileMatch: [modelUri], schema });
    apply();
}

export function unregisterSchema(modelUri) {
    if (entries.delete(modelUri)) {
        apply();
    }
}
