import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { TelegramApi } from '../../core/api/telegram.api';
import { TelegramStatus } from '../../core/models/telegram';
import { TelegramCard } from './telegram-card';

describe('TelegramCard', () => {
  let api: Record<
    'get' | 'setToken' | 'setChat' | 'remove' | 'chats' | 'test',
    ReturnType<typeof vi.fn>
  >;
  let snack: ReturnType<typeof vi.fn>;

  const none: TelegramStatus = {
    configured: false,
    tokenSaved: false,
    tokenHint: null,
    botUsername: null,
    chatId: null,
    chatTitle: null,
  };
  const tokenOnly: TelegramStatus = {
    ...none,
    tokenSaved: true,
    tokenHint: 'wxyz',
    botUsername: 'trip_bot',
  };
  const connected: TelegramStatus = {
    ...tokenOnly,
    configured: true,
    chatId: -100,
    chatTitle: 'Trip crew',
  };

  function render(status: TelegramStatus) {
    api.get.mockReturnValue(of(status));
    TestBed.configureTestingModule({
      providers: [
        { provide: TelegramApi, useValue: api },
        { provide: MatSnackBar, useValue: { open: snack } },
      ],
    });
    const fixture = TestBed.createComponent(TelegramCard);
    fixture.componentRef.setInput('eventId', 'ev-1');
    fixture.detectChanges();
    return fixture;
  }

  const text = (fixture: { nativeElement: unknown }) =>
    (fixture.nativeElement as HTMLElement).textContent ?? '';

  beforeEach(() => {
    api = {
      get: vi.fn(),
      setToken: vi.fn(),
      setChat: vi.fn(),
      remove: vi.fn(),
      chats: vi.fn(),
      test: vi.fn(),
    };
    snack = vi.fn();
  });

  it('asks for a token first, then for the group', () => {
    const fixture = render(none);
    expect(text(fixture)).toContain('Bot token');

    api.setToken.mockReturnValue(of(tokenOnly));
    fixture.componentInstance.token.setValue('123:abc');
    fixture.componentInstance.saveToken();
    fixture.detectChanges();

    expect(api.setToken).toHaveBeenCalledWith('ev-1', '123:abc');
    expect(text(fixture)).toContain('Add @trip_bot to your group');
    expect(text(fixture)).not.toContain('123:abc');
  });

  it('shows the server error for a token it rejects', () => {
    const fixture = render(none);
    api.setToken.mockReturnValue(
      throwError(() => ({ error: { error: "That isn't a Telegram bot token." } })),
    );
    fixture.componentInstance.token.setValue('nope');
    fixture.componentInstance.saveToken();
    fixture.detectChanges();

    expect(text(fixture)).toContain("That isn't a Telegram bot token.");
  });

  it('detects groups and connects the picked one', () => {
    const fixture = render(tokenOnly);
    api.chats.mockReturnValue(of([{ id: -100, title: 'Trip crew', type: 'supergroup' }]));
    fixture.componentInstance.detect();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Trip crew');
    // The only group found is picked already.
    expect(fixture.componentInstance.chatToSave()).toBe(-100);

    api.setChat.mockReturnValue(of(connected));
    fixture.componentInstance.saveChat();
    fixture.detectChanges();

    expect(api.setChat).toHaveBeenCalledWith('ev-1', -100);
    expect(text(fixture)).toContain('Posting to “Trip crew” as @trip_bot');
  });

  it('says so when no groups are found, and takes a typed chat id', () => {
    const fixture = render(tokenOnly);
    api.chats.mockReturnValue(of([]));
    fixture.componentInstance.detect();
    fixture.detectChanges();
    expect(text(fixture)).toContain('No groups yet');

    fixture.componentInstance.manualChat.setValue('-100123');
    expect(fixture.componentInstance.chatToSave()).toBe(-100123);
    fixture.componentInstance.manualChat.setValue('abc');
    expect(fixture.componentInstance.chatToSave()).toBeNull();
  });

  it('sends a test message and can disconnect', () => {
    const fixture = render(connected);
    expect(text(fixture)).toContain('ending in “wxyz”');

    api.test.mockReturnValue(of(undefined));
    fixture.componentInstance.test();
    expect(api.test).toHaveBeenCalledWith('ev-1');
    expect(snack).toHaveBeenCalledWith('Test message sent', 'Dismiss', expect.anything());

    api.remove.mockReturnValue(of(undefined));
    fixture.componentInstance.remove();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Bot token');
  });
});
