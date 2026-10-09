import { Marked, Tokens } from 'marked';

const escapeHtml = (text: string) =>
  text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

const isSafeHref = (href: string) => /^(https?:|mailto:)/i.test(href.trim());

/** An external link, marked like `<app-linkified-text>`'s: new tab, icon after the text. */
const externalLink = (href: string, inner: string) =>
  `<a href="${escapeHtml(href)}" title="${escapeHtml(href)}" target="_blank" ` +
  `rel="noopener noreferrer nofollow">${inner}` +
  `<span class="material-icons md-link-icon" aria-hidden="true">open_in_new</span></a>`;

/**
 * News bodies are written by organisers but read by everyone, so only a small, safe subset
 * of Markdown turns into markup (ADR-014):
 * - Raw HTML is shown as text, never rendered.
 * - Images become links: an external image would load from another host for every reader,
 *   and images live inside the app (ADR-011).
 * - Only http(s) and mailto links are links; anything else is just its text.
 * - Headings start at h3 so they sit under the page's own titles.
 * The result still goes through Angular's sanitizer when bound with `[innerHTML]`.
 */
const marked = new Marked({
  gfm: true,
  breaks: true,
  renderer: {
    html({ text }: Tokens.HTML | Tokens.Tag) {
      return escapeHtml(text);
    },
    image({ href, text }: Tokens.Image) {
      return isSafeHref(href) ? externalLink(href, escapeHtml(text || href)) : escapeHtml(text);
    },
    link({ href, tokens }: Tokens.Link) {
      const inner = this.parser.parseInline(tokens);
      return isSafeHref(href) ? externalLink(href, inner) : inner;
    },
    heading({ tokens, depth }: Tokens.Heading) {
      const level = Math.min(depth + 2, 6);
      return `<h${level}>${this.parser.parseInline(tokens)}</h${level}>\n`;
    },
  },
});

export function renderMarkdown(text: string): string {
  return marked.parse(text, { async: false });
}
