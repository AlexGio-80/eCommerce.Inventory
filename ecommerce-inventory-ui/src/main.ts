import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { isRemoteHost } from './app/core/utils/remote-host';

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));

// Service worker solo dal telefono (HTTPS di Tailscale): serve a Chrome per installare l'app.
if (isRemoteHost() && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch((err) => console.error('Service worker non registrato', err));
}
