import { describe, expect, it } from 'vitest';

import { linkify, shortenUrl } from './linkify';

describe('linkify', () => {
  it('returns plain text untouched', () => {
    expect(linkify('Just a trip.\nSecond line.')).toEqual([{ text: 'Just a trip.\nSecond line.' }]);
  });

  it('turns a URL into a shortened link and keeps the surrounding text', () => {
    expect(linkify('Book here: https://www.example.com/stay/farm-lodge now')).toEqual([
      { text: 'Book here: ' },
      { text: 'example.com/stay/farm-lodge', href: 'https://www.example.com/stay/farm-lodge' },
      { text: ' now' },
    ]);
  });

  it('recognises www. links and opens them over https', () => {
    expect(linkify('www.example.com')).toEqual([
      { text: 'example.com', href: 'https://www.example.com/' },
    ]);
  });

  it('leaves sentence punctuation outside the link', () => {
    const [, link, tail] = linkify('See https://example.com/a, or https://example.com/b.');
    expect(link).toEqual({ text: 'example.com/a', href: 'https://example.com/a' });
    expect(tail).toEqual({ text: ', or ' });
    expect(linkify('https://example.com/b.').at(-1)).toEqual({ text: '.' });
  });

  it('drops an unbalanced closing bracket but keeps a balanced one', () => {
    expect(linkify('(see https://example.com/a)')).toEqual([
      { text: '(see ' },
      { text: 'example.com/a', href: 'https://example.com/a' },
      { text: ')' },
    ]);
    expect(linkify('https://en.wikipedia.org/wiki/Foo_(bar)')[0].href).toBe(
      'https://en.wikipedia.org/wiki/Foo_(bar)',
    );
  });

  it('handles several links on separate lines', () => {
    const links = linkify('https://a.example\nhttp://b.example/x').filter((s) => s.href);
    expect(links.map((s) => s.href)).toEqual(['https://a.example/', 'http://b.example/x']);
  });

  it('only links http and https', () => {
    for (const text of ['javascript:alert(1)', 'ftp://example.com', 'data:text/html,hi']) {
      expect(linkify(text).some((s) => s.href)).toBe(false);
    }
  });

  it('never treats markup as HTML', () => {
    expect(linkify('<img src=x onerror=alert(1)>')).toEqual([
      { text: '<img src=x onerror=alert(1)>' },
    ]);
  });

  it('shows the real host when a URL has credentials', () => {
    const [link] = linkify('https://google.com@evil.example/login');
    expect(link.text).toBe('evil.example/login');
  });
});

describe('bare domains', () => {
  const hrefs = (text: string) => linkify(text).flatMap((s) => (s.href ? [s.href] : []));

  it('links a bare domain, a subdomain and a domain with path and query', () => {
    expect(hrefs('example.com')).toEqual(['https://example.com/']);
    expect(hrefs('Docs: docs.api.example.org')).toEqual(['https://docs.api.example.org/']);
    expect(hrefs('example.com/some/path?x=1')).toEqual(['https://example.com/some/path?x=1']);
    expect(hrefs('example.io and example.photography')).toEqual([
      'https://example.io/',
      'https://example.photography/',
    ]);
    expect(hrefs('example.com:8080/admin')).toEqual(['https://example.com:8080/admin']);
  });

  it('leaves trailing punctuation and surrounding brackets outside', () => {
    expect(linkify('Visit example.com.')).toEqual([
      { text: 'Visit ' },
      { text: 'example.com', href: 'https://example.com/' },
      { text: '.' },
    ]);
    expect(hrefs('(example.com/page) and "example.com/quoted"')).toEqual([
      'https://example.com/page',
      'https://example.com/quoted',
    ]);
  });

  it('does not link things that only look like domains', () => {
    for (const text of [
      'file.txt',
      'v1.2.3',
      'e.g.',
      'i.e. this',
      '3.14',
      'foo.bar()',
      'README.md',
      'script.py',
      'archive.zip',
      'Thanks.It was great',
      'localhost.local',
    ]) {
      expect(hrefs(text), text).toEqual([]);
    }
  });

  it('does not link e-mail addresses or other schemes', () => {
    for (const text of [
      'test@example.com',
      'mailto:test@example.com?subject=Hi',
      'tel:+4915112345678',
      'ftp://ftp.example.com/pub/file.zip',
      'file:///etc/hosts',
      '//example.com/asset.js',
    ]) {
      expect(hrefs(text), text).toEqual([]);
    }
  });

  it('still prefers the explicit scheme for a full URL', () => {
    expect(hrefs('https://example.com/a.b')).toEqual(['https://example.com/a.b']);
  });
});

describe('shortenUrl', () => {
  it('drops the scheme, www and a bare trailing slash', () => {
    expect(shortenUrl(new URL('https://www.example.com/'))).toBe('example.com');
  });

  it('keeps the query string', () => {
    expect(shortenUrl(new URL('https://example.com/watch?v=abc'))).toBe('example.com/watch?v=abc');
  });

  it('cuts long links with an ellipsis', () => {
    const label = shortenUrl(new URL(`https://example.com/${'a'.repeat(100)}`), 20);
    expect(label).toHaveLength(20);
    expect(label.endsWith('…')).toBe(true);
  });
});
