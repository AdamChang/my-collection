type RuntimeConfig = { apiBase?: string };
type RuntimeConfiguredWindow = Window & { __MYCOLLECTION_CONFIG__?: RuntimeConfig };

export function resolveApiBase(configuredValue?: string): string {
  const value = configuredValue?.trim() || '/api';
  return value.length > 1 ? value.replace(/\/+$/, '') : value;
}

const runtimeConfig = (window as RuntimeConfiguredWindow).__MYCOLLECTION_CONFIG__;
export const API_BASE = resolveApiBase(runtimeConfig?.apiBase);
