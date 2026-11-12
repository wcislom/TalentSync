import { bootstrapApplication } from '@angular/platform-browser';
import { App } from './app/app';
import { appConfig } from './app/app.config';
import { loadAppConfig } from './app/config';

loadAppConfig()
  .then((config) => bootstrapApplication(App, appConfig(config)))
  .catch((error: unknown) => {
    console.error(error);
    // Angular never started, so nothing else would tell the user why the page is empty.
    const message = document.createElement('p');
    message.style.padding = '16px';
    message.textContent = `The Admin app could not start: ${error instanceof Error ? error.message : String(error)}`;
    document.body.replaceChildren(message);
  });
