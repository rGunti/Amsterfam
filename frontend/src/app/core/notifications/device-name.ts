/**
 * A readable default name for this device, like "Chrome on Android", from the user agent.
 * Members can rename it; this only has to be good enough to tell devices apart.
 */
export function deviceName(userAgent: string): string {
  const browser = browserOf(userAgent);
  const os = osOf(userAgent);
  if (browser && os) {
    return `${browser} on ${os}`;
  }
  return browser ?? os ?? 'Unknown device';
}

function browserOf(ua: string): string | null {
  // Order matters: Edge and Opera also say "Chrome", Chrome also says "Safari".
  if (/Edg(e|A|iOS)?\//.test(ua)) return 'Edge';
  if (/OPR\/|Opera/.test(ua)) return 'Opera';
  if (/SamsungBrowser\//.test(ua)) return 'Samsung Internet';
  if (/Firefox\/|FxiOS\//.test(ua)) return 'Firefox';
  if (/Chrome\/|CriOS\//.test(ua)) return 'Chrome';
  if (/Safari\//.test(ua)) return 'Safari';
  return null;
}

function osOf(ua: string): string | null {
  if (/iPad/.test(ua)) return 'iPad';
  if (/iPhone|iPod/.test(ua)) return 'iPhone';
  if (/Android/.test(ua)) return 'Android';
  if (/Windows/.test(ua)) return 'Windows';
  // iPadOS asks for desktop sites and says "Macintosh"; only it has a touch screen.
  if (/Macintosh/.test(ua)) {
    return typeof navigator !== 'undefined' && navigator.maxTouchPoints > 1 ? 'iPad' : 'Mac';
  }
  if (/CrOS/.test(ua)) return 'ChromeOS';
  if (/Linux/.test(ua)) return 'Linux';
  return null;
}
