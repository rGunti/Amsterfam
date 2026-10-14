import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatSlideToggle, MatSlideToggleChange } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { vi } from 'vitest';

import { PushDevice } from '../../core/models/push';
import { PushService, PushStatus } from '../../core/notifications/push.service';
import { PushSettingsCard } from './push-settings-card';

describe('PushSettingsCard', () => {
  const device = (id: number, endpoint: string, name: string): PushDevice => ({
    id,
    name,
    endpoint,
    createdAt: '2026-10-13T00:00:00Z',
    lastSuccessAt: null,
  });

  let snackBar: { open: ReturnType<typeof vi.fn> };

  function render(
    status: PushStatus,
    subscribed = false,
    devices: PushDevice[] = [],
    extra: { enable?: () => Promise<void>; permissionUndecided?: boolean } = {},
  ) {
    const push = {
      status: signal(status),
      subscribed: signal(subscribed),
      currentEndpoint: signal(subscribed ? 'https://push/this' : null),
      devices: signal(devices),
      enable: extra.enable ?? (async () => undefined),
      permissionUndecided: extra.permissionUndecided ?? false,
    };
    snackBar = { open: vi.fn() };
    TestBed.configureTestingModule({
      imports: [PushSettingsCard],
      providers: [
        { provide: PushService, useValue: push },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    const fixture = TestBed.createComponent(PushSettingsCard);
    fixture.detectChanges();
    const toggle = fixture.debugElement.query(By.directive(MatSlideToggle))
      .componentInstance as MatSlideToggle;
    return { fixture, toggle, text: () => fixture.nativeElement.textContent as string };
  }

  it('is enabled without a reason when push is ready', () => {
    const { toggle, text } = render('ready');
    expect(toggle.disabled).toBe(false);
    expect(text()).toContain('when an organiser posts news');
  });

  it.each([
    ['off', "aren't set up"],
    ['unsupported', "can't receive push notifications"],
    ['install', 'Home Screen'],
    ['blocked', 'browser settings'],
    ['loading', 'Checking'],
  ] as const)('stays visible but disabled when %s', (status, reason) => {
    const { toggle, text } = render(status);
    expect(toggle.disabled).toBe(true);
    expect(text()).toContain(reason);
  });

  it('can still be turned off when the server is unreachable', () => {
    const { toggle } = render('error', true);
    expect(toggle.disabled).toBe(false);
  });

  it('lists devices and marks this one', () => {
    const { text } = render('ready', true, [
      device(1, 'https://push/this', 'Chrome on Android'),
      device(2, 'https://push/other', 'Firefox on Linux'),
    ]);
    expect(text()).toContain('Chrome on Android');
    expect(text()).toContain('this device');
    expect(text()).toContain('Firefox on Linux');
  });

  it('asks to allow notifications when the browser prompt was dismissed', async () => {
    const { fixture, toggle } = render('ready', false, [], {
      enable: () => Promise.reject(new Error('dismissed')),
      permissionUndecided: true,
    });

    await fixture.componentInstance.toggle({
      checked: true,
      source: toggle,
    } as MatSlideToggleChange);

    expect(snackBar.open).toHaveBeenCalledWith(
      expect.stringContaining('Allow notifications'),
      'Dismiss',
      expect.anything(),
    );
  });

  it('shows the generic error for other failures', async () => {
    const { fixture, toggle } = render('ready', false, [], {
      enable: () => Promise.reject(new Error('500')),
    });

    await fixture.componentInstance.toggle({
      checked: true,
      source: toggle,
    } as MatSlideToggleChange);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Could not change push notifications',
      'Dismiss',
      expect.anything(),
    );
  });
});
