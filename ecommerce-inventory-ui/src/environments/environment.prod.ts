// Sul PC di casa la pagina arriva da http://inventory.local e l'API è sulla stessa macchina, porta 5152.
// Dal telefono, fuori casa, la pagina arriva da Tailscale Serve (https://<pc>.<tailnet>.ts.net) e l'API
// è sullo stesso nome, porta 8443: "localhost" lì sarebbe il telefono. Vedi DEPLOY.md, "Accesso dal telefono".
const tailscaleApiPort = 8443;

function resolveApiBaseUrl(): string {
  const { protocol, hostname } = window.location;
  if (hostname.endsWith('.ts.net')) {
    return `${protocol}//${hostname}:${tailscaleApiPort}`;
  }
  return 'http://localhost:5152';
}

const apiBaseUrl = resolveApiBaseUrl();

export const environment = {
  production: true,
  apiUrl: apiBaseUrl,
  api: {
    baseUrl: apiBaseUrl
  }
};
