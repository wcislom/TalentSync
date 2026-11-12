import { InjectionToken } from '@angular/core';

export interface AppConfig {
  atsSimulatorUrl: string;
  crmSimulatorUrl: string;
  matchingPlatformSimulatorUrl: string;
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');

// Fetched before bootstrap instead of compiled in from environment.ts: the cluster serves its own config.json from a
// ConfigMap, so one image runs everywhere (ADR 012).
export async function loadAppConfig(): Promise<AppConfig> {
  const response = await fetch('config.json');
  if (!response.ok) {
    throw new Error(`config.json: ${response.status} ${response.statusText}`);
  }
  return response.json();
}
