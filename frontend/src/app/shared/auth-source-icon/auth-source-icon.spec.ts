import { TestBed } from '@angular/core/testing';
import { AuthSourceIcon, authSourceLabel } from './auth-source-icon';

describe('authSourceLabel', () => {
  it('names the known sources', () => {
    expect(authSourceLabel('discord')).toBe('Discord');
    expect(authSourceLabel('internal')).toBe('Internal');
  });

  it('title-cases any other source slug', () => {
    expect(authSourceLabel('google')).toBe('Google');
    expect(authSourceLabel('work-sso')).toBe('Work Sso');
  });
});

describe('AuthSourceIcon', () => {
  function render(source: string | null): HTMLElement {
    const fixture = TestBed.createComponent(AuthSourceIcon);
    fixture.componentRef.setInput('source', source);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the Discord mark, labelled for screen readers', () => {
    const el = render('discord');
    expect(el.querySelector('svg.discord')).not.toBeNull();
    expect(el.querySelector('mat-icon')).toBeNull();
    expect(el.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe('Discord');
  });

  it('shows a Material icon for internal accounts', () => {
    const el = render('internal');
    expect(el.querySelector('mat-icon')?.textContent?.trim()).toBe('badge');
    expect(el.querySelector('[role="img"]')?.getAttribute('aria-label')).toBe('Internal');
  });

  it('falls back to a generic icon for other sources', () => {
    expect(render('google').querySelector('mat-icon')?.textContent?.trim()).toBe('login');
  });

  it('renders nothing while the source is unknown', () => {
    expect(render(null).querySelector('[role="img"]')).toBeNull();
  });
});
