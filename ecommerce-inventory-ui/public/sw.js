// Service worker minimo, perché Chrome possa installare l'app sul telefono (feature 006).
// Non mette in cache niente, né file né dati: i prezzi devono essere sempre quelli di adesso, e
// a ogni pubblicazione il telefono deve vedere la versione nuova (IIS manda già "no-cache").
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => {
  // Nessuna risposta dal service worker: ogni richiesta va in rete come senza.
});
