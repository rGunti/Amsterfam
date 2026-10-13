import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { DiscordWebhookApi } from '../../core/api/discord-webhook.api';
import { DiscordWebhookStatus } from '../../core/models/discord-webhook';
import { DiscordWebhookCard } from './discord-webhook-card';

describe('DiscordWebhookCard', () => {
  let api: {
    get: ReturnType<typeof vi.fn>;
    set: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
    test: ReturnType<typeof vi.fn>;
  };
  let snack: ReturnType<typeof vi.fn>;

  function render(status: DiscordWebhookStatus) {
    api.get.mockReturnValue(of(status));
    TestBed.configureTestingModule({
      providers: [
        { provide: DiscordWebhookApi, useValue: api },
        { provide: MatSnackBar, useValue: { open: snack } },
      ],
    });
    const fixture = TestBed.createComponent(DiscordWebhookCard);
    fixture.componentRef.setInput('eventId', 'ev-1');
    fixture.detectChanges();
    return fixture;
  }

  const text = (fixture: { nativeElement: unknown }) =>
    (fixture.nativeElement as HTMLElement).textContent ?? '';

  beforeEach(() => {
    api = { get: vi.fn(), set: vi.fn(), remove: vi.fn(), test: vi.fn() };
    snack = vi.fn();
  });

  it('asks for a URL when nothing is connected, and saves it', () => {
    const fixture = render({ configured: false, hint: null });
    expect(text(fixture)).toContain('Webhook URL');

    api.set.mockReturnValue(of({ configured: true, hint: 'abcd' }));
    fixture.componentInstance.url.setValue('https://discord.com/api/webhooks/1/abcd');
    fixture.componentInstance.save();
    fixture.detectChanges();

    expect(api.set).toHaveBeenCalledWith('ev-1', 'https://discord.com/api/webhooks/1/abcd');
    expect(text(fixture)).toContain('ending in “abcd”');
  });

  it('shows the server error for a URL it rejects', () => {
    const fixture = render({ configured: false, hint: null });
    api.set.mockReturnValue(
      throwError(() => ({ error: { error: "That isn't a Discord webhook URL." } })),
    );
    fixture.componentInstance.url.setValue('https://example.com');
    fixture.componentInstance.save();
    fixture.detectChanges();

    expect(text(fixture)).toContain("That isn't a Discord webhook URL.");
  });

  it('sends a test message and can disconnect', () => {
    const fixture = render({ configured: true, hint: 'wxyz' });
    expect(text(fixture)).toContain('ending in “wxyz”');

    api.test.mockReturnValue(of(undefined));
    fixture.componentInstance.test();
    expect(api.test).toHaveBeenCalledWith('ev-1');
    expect(snack).toHaveBeenCalledWith('Test message sent', 'Dismiss', expect.anything());

    api.remove.mockReturnValue(of(undefined));
    fixture.componentInstance.remove();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Webhook URL');
  });
});
