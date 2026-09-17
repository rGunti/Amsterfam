export interface RuntimeConfig {
  oidcIssuer: string;
}

// Fetched at app startup instead of being baked into the bundle at build
// time, so the same image can be deployed to any environment. Served as a
// static file in dev (public/config.json) and rendered from the container's
// env at startup in Docker (see docker/render-config.sh).
export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch('/config.json');
  return response.json();
}
