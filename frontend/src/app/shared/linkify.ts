import { LINKABLE_TLDS } from './tlds';

export interface TextSegment {
  text: string;
  /** Present for links: the absolute http(s) URL to open. */
  href?: string;
}

/** Longest link label; longer ones are cut with an ellipsis (the full URL stays in the href). */
export const MAX_LINK_LABEL_LENGTH = 40;

// http(s):// or www. up to the next whitespace. Trailing punctuation is trimmed afterwards.
const SCHEME_OR_WWW = String.raw`\b(?:https?:\/\/|www\.)[^\s<>"]+`;
// A bare "name.tld[:port][/path]". The lookbehind keeps it out of e-mail addresses, longer
// URLs and dotted identifiers; the TLD is checked against LINKABLE_TLDS afterwards.
const BARE_DOMAIN = String.raw`(?<![\w@.\/:%-])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}(?![\w@-])(?::\d{1,5})?(?:[/?#][^\s<>"]*)?`;
const URL_PATTERN = new RegExp(`${SCHEME_OR_WWW}|${BARE_DOMAIN}`, 'gi');

const TRAILING_PUNCTUATION = '.,;:!?\'"';
const BRACKETS: Record<string, string> = { ')': '(', ']': '[', '}': '{' };

/**
 * Drops sentence punctuation that follows a URL ("see https://a.b/c.") and closing brackets
 * that have no opener inside the URL ("(see https://a.b/c)"), while keeping balanced ones
 * such as https://en.wikipedia.org/wiki/Foo_(bar).
 */
function trimTrailing(raw: string): string {
  let end = raw.length;
  while (end > 0) {
    const last = raw[end - 1];
    if (TRAILING_PUNCTUATION.includes(last)) {
      end--;
    } else if (last in BRACKETS) {
      const body = raw.slice(0, end);
      const opens = body.split(BRACKETS[last]).length - 1;
      const closes = body.split(last).length - 1;
      if (closes > opens) {
        end--;
      } else {
        break;
      }
    } else {
      break;
    }
  }
  return raw.slice(0, end);
}

/** For a match with no scheme: is its last host label a TLD we link (written in lowercase)? */
function hasLinkableTld(raw: string): boolean {
  const host = raw.split(/[/?#:]/, 1)[0];
  const tld = host.slice(host.lastIndexOf('.') + 1);
  return tld === tld.toLowerCase() && LINKABLE_TLDS.has(tld);
}

function toUrl(raw: string): URL | null {
  const hasScheme = /^https?:\/\//i.test(raw);
  if (!hasScheme && !/^www\./i.test(raw) && !hasLinkableTld(raw)) {
    return null;
  }
  const withScheme = hasScheme ? raw : `https://${raw}`;
  try {
    const url = new URL(withScheme);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}

/**
 * Compact label for a link: no scheme, no `www.`, no bare trailing slash, cut when long. The
 * port stays (it changes where the link goes); a default port is dropped by `URL` itself.
 */
export function shortenUrl(url: URL, max = MAX_LINK_LABEL_LENGTH): string {
  const path = url.pathname === '/' ? '' : url.pathname;
  const label = url.host.replace(/^www\./i, '') + path + url.search + url.hash;
  return label.length > max ? `${label.slice(0, max - 1)}…` : label;
}

/**
 * Splits plain text into text and link segments. Recognises http(s):// links, www. links and
 * bare domains on a common TLD. Everything becomes an http(s) link, so the result is safe to
 * render as anchors; nothing is interpreted as HTML.
 */
export function linkify(text: string): TextSegment[] {
  const segments: TextSegment[] = [];
  let cursor = 0;

  for (const match of text.matchAll(URL_PATTERN)) {
    const raw = trimTrailing(match[0]);
    const url = toUrl(raw);
    if (!url) {
      continue;
    }
    if (match.index > cursor) {
      segments.push({ text: text.slice(cursor, match.index) });
    }
    segments.push({ text: shortenUrl(url), href: url.href });
    cursor = match.index + raw.length;
  }

  if (cursor < text.length) {
    segments.push({ text: text.slice(cursor) });
  }
  return segments;
}
