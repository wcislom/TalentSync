import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MAT_ICON_DEFAULT_OPTIONS } from '@angular/material/icon';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { RUNTIME_CONFIG, RuntimeConfig } from './core/runtime-config/runtime-config';

export function appConfig(runtimeConfig: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideRouter(routes),
      { provide: RUNTIME_CONFIG, useValue: runtimeConfig },
      // index.html loads the Material Symbols font, not the older Material Icons one mat-icon expects by default.
      { provide: MAT_ICON_DEFAULT_OPTIONS, useValue: { fontSet: 'material-symbols-outlined' } },
    ],
  };
}
