import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatSlideToggle, MatSlideToggleChange } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { UserApi } from '../../core/api/user.api';
import { NotificationSettings } from '../../core/models/notification-settings';
import { EmailSettingsCard } from './email-settings-card';

describe('EmailSettingsCard', () => {
  const base: NotificationSettings = {
    emailAvailable: true,
    emailNotifications: false,
    loginEmail: 'me@example.com',
    notificationEmail: null,
    notificationEmailConfirmed: false,
    effectiveEmail: 'me@example.com',
  };

  let snackBar: { open: ReturnType<typeof vi.fn> };

  async function render(
    settings: Partial<NotificationSettings>,
    api: Partial<Record<keyof UserApi, () => Observable<unknown>>> = {},
  ) {
    const userApi = {
      getNotificationSettings: () => of({ ...base, ...settings }),
      ...api,
    };
    snackBar = { open: vi.fn() };
    TestBed.configureTestingModule({
      imports: [EmailSettingsCard],
      providers: [
        { provide: UserApi, useValue: userApi },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    const fixture = TestBed.createComponent(EmailSettingsCard);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const toggleEl = fixture.debugElement.query(By.directive(MatSlideToggle));
    return {
      fixture,
      component: fixture.componentInstance,
      toggle: toggleEl?.componentInstance as MatSlideToggle | undefined,
      text: () => fixture.nativeElement.textContent as string,
    };
  }

  it('is hidden while email is not set up on the server', async () => {
    const { toggle, text } = await render({ emailAvailable: false });
    expect(toggle).toBeUndefined();
    expect(text().trim()).toBe('');
  });

  it('shows where emails go', async () => {
    const { toggle, text } = await render({});
    expect(toggle!.disabled).toBe(false);
    expect(text()).toContain('Emails go to me@example.com');
  });

  it('is disabled without any address', async () => {
    const { toggle, text } = await render({ loginEmail: null, effectiveEmail: null });
    expect(toggle!.disabled).toBe(true);
    expect(text()).toContain('No email address from your login');
  });

  it('can be turned on while a custom address waits for confirmation', async () => {
    const { toggle, text } = await render({
      loginEmail: null,
      effectiveEmail: null,
      notificationEmail: 'new@example.com',
    });
    expect(toggle!.disabled).toBe(false);
    expect(text()).toContain('Waiting for you to confirm new@example.com');
    expect(text()).toContain('Resend link');
  });

  it('says the login address is used until the custom one is confirmed', async () => {
    const { text } = await render({ notificationEmail: 'new@example.com' });
    expect(text()).toContain('Until then, emails go to me@example.com');
  });

  it('turns email on', async () => {
    const update = vi.fn(() => of({ ...base, emailNotifications: true }));
    const { component } = await render({}, { updateNotificationSettings: update });

    await component.toggle({ checked: true, source: { checked: true } } as MatSlideToggleChange);

    expect(update).toHaveBeenCalledWith(true);
    expect(component.settings()!.emailNotifications).toBe(true);
  });

  it('reverts the switch when saving fails', async () => {
    const { component } = await render(
      {},
      {
        updateNotificationSettings: () => throwError(() => new HttpErrorResponse({ status: 500 })),
      },
    );
    const source = { checked: true };

    await component.toggle({ checked: true, source } as MatSlideToggleChange);

    expect(source.checked).toBe(false);
    expect(snackBar.open).toHaveBeenCalledWith('Could not turn on email', 'Dismiss', {
      duration: 5000,
    });
  });

  it('sends a confirmation link for a new address', async () => {
    const set = vi.fn(() => of({ ...base, notificationEmail: 'new@example.com' }));
    const { component } = await render({}, { setNotificationEmail: set });

    component.startEdit();
    component.emailControl.setValue('new@example.com');
    await component.save();

    expect(set).toHaveBeenCalledWith('new@example.com');
    expect(component.editing()).toBe(false);
    expect(snackBar.open).toHaveBeenCalledWith(
      'We sent a confirmation link to new@example.com',
      'Dismiss',
      { duration: 5000 },
    );
  });

  it('does not send an invalid address', async () => {
    const set = vi.fn();
    const { component } = await render({}, { setNotificationEmail: set });

    component.startEdit();
    component.emailControl.setValue('nope');
    await component.save();

    expect(set).not.toHaveBeenCalled();
    expect(component.editing()).toBe(true);
  });

  it('explains when a resend comes too soon', async () => {
    const { component } = await render(
      { notificationEmail: 'new@example.com' },
      {
        resendNotificationEmail: () => throwError(() => new HttpErrorResponse({ status: 429 })),
      },
    );

    await component.resend();

    expect(snackBar.open).toHaveBeenCalledWith(
      'Wait a minute before asking for another link.',
      'Dismiss',
      { duration: 5000 },
    );
  });

  it('cancels a pending address', async () => {
    const clear = vi.fn(() => of(base));
    const { component } = await render(
      { notificationEmail: 'new@example.com' },
      { clearNotificationEmail: clear },
    );

    await component.useLoginAddress();

    expect(clear).toHaveBeenCalled();
    expect(component.pending()).toBeNull();
  });
});
