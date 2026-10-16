/**
 * True se la pagina arriva da Tailscale Serve (https://<pc>.<tailnet>.ts.net), cioè dal telefono
 * fuori casa: lì si entra nella sezione per il telefono e si installa l'app. Su inventory.local no.
 */
export function isRemoteHost(): boolean {
  return window.location.hostname.endsWith('.ts.net');
}
