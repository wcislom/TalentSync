import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';
import { loadAppConfig } from './app/config';

loadAppConfig()
  .then((config) => bootstrapApplication(App, appConfig(config)))
  .catch((err) => console.error(err));
