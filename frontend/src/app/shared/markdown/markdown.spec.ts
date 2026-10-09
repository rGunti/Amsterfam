import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { vi } from 'vitest';

import { ExternalLinkDialog } from '../external-link-dialog/external-link-dialog';
import { Markdown } from './markdown';
import { renderMarkdown } from './render-markdown';

describe('renderMarkdown', () => {
  it('renders basic formatting', () => {
    const html = renderMarkdown('**bold** _it_ ~~gone~~\n\n- one\n- two\n\n> quote\n\n`code`');
    expect(html).toContain('<strong>bold</strong>');
    expect(html).toContain('<em>it</em>');
    expect(html).toContain('<del>gone</del>');
    expect(html).toContain('<li>one</li>');
    expect(html).toContain('<blockquote>');
    expect(html).toContain('<code>code</code>');
  });

  it('keeps single line breaks', () => {
    expect(renderMarkdown('one\ntwo')).toContain('one<br>two');
  });

  it('shows raw HTML as text', () => {
    const html = renderMarkdown('<script>alert(1)</script>\n\nHi <b onclick="x()">there</b>');
    expect(html).not.toContain('<script');
    expect(html).not.toContain('<b ');
    expect(html).toContain('&lt;script&gt;');
  });

  it('turns images into links', () => {
    const html = renderMarkdown('![a cat](https://example.com/cat.png)');
    expect(html).not.toContain('<img');
    expect(html).toContain('href="https://example.com/cat.png"');
    expect(html).toContain('a cat');
  });

  it('opens links in a new tab and drops unsafe ones', () => {
    const html = renderMarkdown('[ok](https://example.com) [bad](javascript:alert(1))');
    expect(html).toContain('href="https://example.com"');
    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener noreferrer nofollow"');
    expect(html).not.toContain('javascript:');
    expect(html).toContain('bad');
  });

  it('links bare URLs', () => {
    expect(renderMarkdown('see https://example.com/x')).toContain('href="https://example.com/x"');
  });

  it('starts headings at h3', () => {
    expect(renderMarkdown('# Title')).toContain('<h3>Title</h3>');
    expect(renderMarkdown('## Sub')).toContain('<h4>Sub</h4>');
  });
});

describe('Markdown', () => {
  function render(text: string) {
    const fixture = TestBed.createComponent(Markdown);
    fixture.componentRef.setInput('text', text);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the markup', () => {
    expect(render('**hi**').querySelector('strong')?.textContent).toBe('hi');
  });

  it('asks before following a plain click on a link', () => {
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue(undefined as never);
    const link = render('[go](https://example.com)').querySelector('a')!;

    const plain = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
    link.dispatchEvent(plain);
    expect(plain.defaultPrevented).toBe(true);
    expect(open).toHaveBeenCalledWith(ExternalLinkDialog, {
      data: { href: 'https://example.com' },
    });

    open.mockClear();
    const modified = new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true });
    link.dispatchEvent(modified);
    expect(modified.defaultPrevented).toBe(false);
    expect(open).not.toHaveBeenCalled();
  });
});
