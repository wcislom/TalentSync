import { RUNTIME_CONFIG_KEYS, RuntimeConfig } from './runtime-config';

// Fetched before bootstrap instead of compiled in from environment.ts: the cluster serves its own config.json from a
// ConfigMap, so one image runs everywhere (ADR 012).
export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch('config.json');
  if (!response.ok) {
    throw new Error(`config.json: ${response.status} ${response.statusText}`);
  }

  // A dev server or proxy answers a missing file with index.html and 200.
  let config: Record<string, unknown>;
  try {
    config = await response.json();
  } catch {
    throw new Error('config.json is not valid JSON (is the file missing and index.html served instead?)');
  }

  // A misspelled key would otherwise turn into requests to "undefined/...", which look like a simulator failure.
  const missing = RUNTIME_CONFIG_KEYS.filter((key) => typeof config[key] !== 'string' || config[key] === '');
  if (missing.length > 0) {
    throw new Error(`config.json is missing ${missing.join(', ')}`);
  }
  return config as unknown as RuntimeConfig;
}
