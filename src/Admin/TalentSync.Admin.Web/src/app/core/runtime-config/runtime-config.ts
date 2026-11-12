import { InjectionToken } from '@angular/core';

/** The contents of public/config.json. Not to be confused with app.config.ts, which holds Angular's providers. */
export interface RuntimeConfig {
  atsSimulatorUrl: string;
  crmSimulatorUrl: string;
  matchingPlatformSimulatorUrl: string;
  /** Where traces of the hosts can be viewed (the Aspire Dashboard locally). */
  tracesUrl: string;
}

export const RUNTIME_CONFIG_KEYS: readonly (keyof RuntimeConfig)[] = [
  'atsSimulatorUrl',
  'crmSimulatorUrl',
  'matchingPlatformSimulatorUrl',
  'tracesUrl',
];

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG');
