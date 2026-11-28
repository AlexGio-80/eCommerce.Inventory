import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { isRemoteHost } from './app/core/utils/remote-host';
import { captureCardmarketPage } from './app/features/purchasing/cardmarket-bookmarklet';

// Prima del router: la pagina Cardmarket mandata dal pulsante nei preferiti arriva dopo il #, e va tolta subito
// dall'indirizzo (anche se il login la facesse passare da un redirect).
captureCardmarketPage();

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));

// Service worker solo dal telefono (HTTPS di Tailscale): serve a Chrome per installare l'app.
if (isRemoteHost() && 'serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch((err) => console.error('Service worker non registrato', err));
}
