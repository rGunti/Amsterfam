import { TestBed } from '@angular/core/testing';
import { AuthSourceChip, authSourceLabel } from './auth-source-chip';

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

describe('AuthSourceChip', () => {
  function render(source: string | null): HTMLElement {
    const fixture = TestBed.createComponent(AuthSourceChip);
    fixture.componentRef.setInput('source', source);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the source label', () => {
    expect(render('discord').textContent?.trim()).toBe('Discord');
  });

  it('renders nothing while the source is unknown', () => {
    expect(render(null).querySelector('.chip')).toBeNull();
  });
});
