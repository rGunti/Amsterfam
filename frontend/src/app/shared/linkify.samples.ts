// The sample description used while building the link support: one line per case, with the links
// linkify() should produce for it (shortened label and absolute href). Each section covers one
// family of inputs. Lines with an empty list must stay plain text, which matters most for the
// dangerous ones in the XSS section.

export interface SampleCase {
  /** One line of the description. */
  line: string;
  /** Expected links on that line, in order. */
  links: { label: string; href: string }[];
}

export interface SampleSection {
  title: string;
  cases: SampleCase[];
}

export const SAMPLE_SECTIONS: SampleSection[] = [
  {
    title: 'PLAIN URLS',
    cases: [
      {
        line: 'Standard http: http://example.com',
        links: [{ label: 'example.com', href: 'http://example.com/' }],
      },
      {
        line: 'Standard https: https://example.com',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: 'With path and query: https://example.com/path/to/page?id=42&name=test&sort=asc#section-2',
        links: [
          {
            label: 'example.com/path/to/page?id=42&name=tes…',
            href: 'https://example.com/path/to/page?id=42&name=test&sort=asc#section-2',
          },
        ],
      },
      {
        line: 'With port: http://example.com:8080/admin',
        links: [{ label: 'example.com:8080/admin', href: 'http://example.com:8080/admin' }],
      },
      {
        line: 'With userinfo: https://user:pass@example.com/secret',
        links: [{ label: 'example.com/secret', href: 'https://user:pass@example.com/secret' }],
      },
      {
        line: 'IP address: http://192.168.1.1/router',
        links: [{ label: '192.168.1.1/router', href: 'http://192.168.1.1/router' }],
      },
      {
        line: 'IPv6: http://[2001:db8::1]:8080/index.html',
        links: [
          { label: '[2001:db8::1]:8080/index.html', href: 'http://[2001:db8::1]:8080/index.html' },
        ],
      },
      {
        line: 'Localhost: http://localhost:3000/dev',
        links: [{ label: 'localhost:3000/dev', href: 'http://localhost:3000/dev' }],
      },
    ],
  },
  {
    title: 'NO PROTOCOL / BARE DOMAINS',
    cases: [
      {
        line: 'With www: www.example.com',
        links: [{ label: 'example.com', href: 'https://www.example.com/' }],
      },
      {
        line: 'Bare domain: example.com',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: 'Subdomain: docs.api.example.org',
        links: [{ label: 'docs.api.example.org', href: 'https://docs.api.example.org/' }],
      },
      {
        line: 'Domain with path: example.com/some/path?x=1',
        links: [{ label: 'example.com/some/path?x=1', href: 'https://example.com/some/path?x=1' }],
      },
      { line: 'Protocol-relative: //example.com/asset.js', links: [] },
      {
        line: 'Short TLD: example.io',
        links: [{ label: 'example.io', href: 'https://example.io/' }],
      },
      {
        line: 'Long TLD: example.photography',
        links: [{ label: 'example.photography', href: 'https://example.photography/' }],
      },
    ],
  },
  {
    title: 'OTHER SCHEMES',
    cases: [
      { line: 'Mail: mailto:test@example.com', links: [] },
      {
        line: 'Mail with params: mailto:test@example.com?subject=Hello&body=Hi%20there',
        links: [],
      },
      { line: 'Phone: tel:+4915112345678', links: [] },
      { line: 'FTP: ftp://ftp.example.com/pub/file.zip', links: [] },
      { line: 'File: file:///etc/hosts', links: [] },
    ],
  },
  {
    title: 'EDGE CASES',
    cases: [
      {
        line: 'Trailing period: Visit https://example.com.',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: 'Trailing comma: See https://example.com, then continue.',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: 'In parentheses: (https://example.com/page)',
        links: [{ label: 'example.com/page', href: 'https://example.com/page' }],
      },
      {
        line: 'In quotes: "https://example.com/quoted"',
        links: [{ label: 'example.com/quoted', href: 'https://example.com/quoted' }],
      },
      {
        line: 'Wikipedia-style parens: https://en.wikipedia.org/wiki/Example_(disambiguation)',
        links: [
          {
            label: 'en.wikipedia.org/wiki/Example_(disambig…',
            href: 'https://en.wikipedia.org/wiki/Example_(disambiguation)',
          },
        ],
      },
      {
        line: 'Percent-encoded: https://example.com/a%20b/c%2Fd',
        links: [{ label: 'example.com/a%20b/c%2Fd', href: 'https://example.com/a%20b/c%2Fd' }],
      },
      {
        line: 'Unicode/IDN: https://münchen.example/straße',
        links: [
          {
            label: 'xn--mnchen-3ya.example/stra%C3%9Fe',
            href: 'https://xn--mnchen-3ya.example/stra%C3%9Fe',
          },
        ],
      },
      {
        line: 'Very long: https://example.com/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
        links: [
          {
            label: 'example.com/aaaaaaaaaaaaaaaaaaaaaaaaaaa…',
            href: 'https://example.com/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
          },
        ],
      },
      { line: 'Not a link: file.txt, v1.2.3, e.g., 3.14, foo.bar()', links: [] },
    ],
  },
  {
    title: 'MARKDOWN / HTML LINKS',
    cases: [
      {
        line: '[Markdown link](https://example.com)',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: '[Markdown with title](https://example.com "Title text")',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: '<https://example.com/autolink>',
        links: [{ label: 'example.com/autolink', href: 'https://example.com/autolink' }],
      },
      {
        line: '<a href="https://example.com">HTML anchor</a>',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: '<a href="https://example.com" target="_blank" rel="noopener">Anchor with attrs</a>',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
    ],
  },
  {
    title: 'XSS SAMPLES (for testing sanitizers)',
    cases: [
      { line: "[JS URL](javascript:alert('XSS'))", links: [] },
      { line: '<a href="javascript:alert(1)">Click me</a>', links: [] },
      { line: '<a href="JaVaScRiPt:alert(1)">Mixed case</a>', links: [] },
      { line: '<a href="java&#x09;script:alert(1)">Tab-obfuscated</a>', links: [] },
      { line: '<a href="&#106;avascript:alert(1)">Entity-encoded</a>', links: [] },
      { line: '<a href="data:text/html,<script>alert(1)</script>">Data URI</a>', links: [] },
      { line: '<a href="vbscript:msgbox(1)">VBScript</a>', links: [] },
      {
        line: '<a href="https://example.com" onclick="alert(1)">Onclick attr</a>',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      {
        line: '<a href="https://example.com" onmouseover="alert(1)">Onmouseover attr</a>',
        links: [{ label: 'example.com', href: 'https://example.com/' }],
      },
      { line: "<script>alert('XSS')</script>", links: [] },
      { line: '<img src=x onerror=alert(1)>', links: [] },
      { line: '<svg onload=alert(1)>', links: [] },
      { line: '<iframe src="javascript:alert(1)"></iframe>', links: [] },
      { line: '<body onload=alert(1)>', links: [] },
      { line: '"><script>alert(1)</script>', links: [] },
      { line: "'><img src=x onerror=alert(1)>", links: [] },
      {
        line: 'https://example.com/?q=<script>alert(1)</script>',
        links: [{ label: 'example.com?q=', href: 'https://example.com/?q=' }],
      },
      {
        line: 'https://example.com/?q="onmouseover="alert(1)',
        links: [{ label: 'example.com?q=', href: 'https://example.com/?q=' }],
      },
      {
        line: 'http://example.com/#"><img src=x onerror=alert(1)>',
        links: [{ label: 'example.com', href: 'http://example.com/#' }],
      },
      { line: 'javascript:alert(document.cookie)', links: [] },
    ],
  },
];

/** The whole description as it was pasted: headings, blank lines between sections, all cases. */
export const SAMPLE_DESCRIPTION = SAMPLE_SECTIONS.map((section) =>
  [`=== ${section.title} ===`, ...section.cases.map((c) => c.line)].join('\n'),
).join('\n\n');
