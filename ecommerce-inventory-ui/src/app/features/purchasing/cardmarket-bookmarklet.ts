/**
 * Pulsante nei preferiti per mandare all'app la pagina delle offerte di un venditore Cardmarket (feature 007),
 * al posto di Ctrl+A, Ctrl+C e incolla.
 *
 * La pagina la apre l'utente nel suo browser, una alla volta: il pulsante legge solo quella che ha davanti. Nessuna
 * lettura automatica del sito, che Cardmarket vieta e Cloudflare blocca (decisione del 17/10).
 *
 * Il testo viaggia nell'indirizzo dopo il # (che non arriva a nessun server) di una nuova scheda dell'app: così non
 * dipende dalle regole di sicurezza della pagina di Cardmarket (connect-src, Cross-Origin-Opener-Policy), che
 * bloccherebbero una chiamata diretta all'API o un messaggio fra finestre. L'app lo prende prima del router, lo
 * toglie dall'indirizzo e lo legge con il token dell'utente, come un incolla.
 */

const HASH_KEY = 'cm-page=';
const STORAGE_KEY = 'budget_plan_cm_page';

/** Il codice da trascinare nei preferiti, rivolto all'app su <paramref name="origin"/>. */
export function cardmarketBookmarklet(origin: string): string {
  const target = `${origin}/layout/purchasing?tab=budget&cm=`;
  const code =
    `(()=>{` +
    `if(!/cardmarket\\.com$/.test(location.hostname)||!/\\/Users\\/[^/]+\\/Offers/.test(location.pathname)){` +
    `alert('Apri prima una pagina delle offerte di un venditore Cardmarket');return}` +
    `window.open(${JSON.stringify(target)}+Date.now()+'#${HASH_KEY}'+encodeURIComponent(document.body.innerText),'_blank')` +
    `})()`;
  return `javascript:${encodeURIComponent(code)}`;
}

/** All'avvio dell'app: se l'indirizzo porta una pagina Cardmarket, la conserva per la scheda Budget e la toglie. */
export function captureCardmarketPage(): void {
  const hash = window.location.hash;
  if (!hash.startsWith(`#${HASH_KEY}`)) return;
  try {
    sessionStorage.setItem(STORAGE_KEY, decodeURIComponent(hash.substring(HASH_KEY.length + 1)));
  } catch {
    // Testo illeggibile o troppo grande: la scheda resta com'è, si può sempre incollare a mano.
  }
  history.replaceState(history.state, '', window.location.pathname + window.location.search);
}

/** La pagina Cardmarket arrivata dal pulsante, una volta sola. */
export function takeCardmarketPage(): string | null {
  const text = sessionStorage.getItem(STORAGE_KEY);
  if (text != null) sessionStorage.removeItem(STORAGE_KEY);
  return text;
}
