import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { PushApi } from '../api/push.api';
import { CurrentEventService } from '../event/current-event.service';
import { PushService } from './push.service';

const PUBLIC_KEY =
  'BHGX-ILgM_QPwPjN4xskjOD5zZJzKnOyakHgLK7Ulq83xjGEq5vqaGTGZpMHtdspOQV8Bw6sdDFVGKaFon5nmwM';

function keyBytes(base64Url: string): ArrayBuffer {
  const binary = atob(base64Url.replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(binary, (c) => c.charCodeAt(0)).buffer;
}

function fakeSubscription(key = PUBLIC_KEY): PushSubscription {
  return {
    endpoint: 'https://fcm.googleapis.com/fcm/send/abc',
    options: { applicationServerKey: keyBytes(key) },
    toJSON: () => ({
      endpoint: 'https://fcm.googleapis.com/fcm/send/abc',
      keys: { p256dh: 'p256', auth: 'auth' },
    }),
  } as unknown as PushSubscription;
}

describe('PushService', () => {
  let subscription$: BehaviorSubject<PushSubscription | null>;
  let messages$: Subject<object>;
  let swPush: {
    isEnabled: boolean;
    subscription: BehaviorSubject<PushSubscription | null>;
    messages: Subject<object>;
    requestSubscription: ReturnType<typeof vi.fn>;
    unsubscribe: ReturnType<typeof vi.fn>;
  };
  let api: {
    getConfig: ReturnType<typeof vi.fn>;
    save: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
  };
  let refreshBadges: ReturnType<typeof vi.fn>;

  function setup(options: { enabled?: boolean; configured?: boolean } = {}): PushService {
    subscription$ = new BehaviorSubject<PushSubscription | null>(null);
    messages$ = new Subject();
    swPush = {
      isEnabled: options.enabled ?? true,
      subscription: subscription$,
      messages: messages$,
      requestSubscription: vi.fn(async () => {
        const sub = fakeSubscription();
        subscription$.next(sub);
        return sub;
      }),
      unsubscribe: vi.fn(async () => subscription$.next(null)),
    };
    api = {
      getConfig: vi.fn(() =>
        options.configured === false
          ? throwError(() => ({ status: 404 }))
          : of({ publicKey: PUBLIC_KEY }),
      ),
      save: vi.fn(() => of(undefined)),
      remove: vi.fn(() => of(undefined)),
    };
    refreshBadges = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: SwPush, useValue: swPush },
        { provide: PushApi, useValue: api },
        { provide: CurrentEventService, useValue: { refreshBadges } },
      ],
    });
    return TestBed.inject(PushService);
  }

  it('is available once the server hands out its key', () => {
    const service = setup();
    service.init();
    expect(service.available()).toBe(true);
  });

  it('is unavailable when push is off on the server', () => {
    const service = setup({ configured: false });
    service.init();
    expect(service.available()).toBe(false);
  });

  it('is unavailable without the service worker', () => {
    const service = setup({ enabled: false });
    service.init();
    expect(api.getConfig).not.toHaveBeenCalled();
    expect(service.available()).toBe(false);
  });

  it('subscribes with the server key and saves the subscription', async () => {
    const service = setup();
    service.init();

    await service.enable();

    expect(swPush.requestSubscription).toHaveBeenCalledWith({ serverPublicKey: PUBLIC_KEY });
    expect(api.save).toHaveBeenCalledWith({
      endpoint: 'https://fcm.googleapis.com/fcm/send/abc',
      keys: { p256dh: 'p256', auth: 'auth' },
    });
    expect(service.subscribed()).toBe(true);
  });

  it('removes the subscription on the server and the device', async () => {
    const service = setup();
    subscription$.next(fakeSubscription());

    await service.disable();

    expect(api.remove).toHaveBeenCalledWith('https://fcm.googleapis.com/fcm/send/abc');
    expect(swPush.unsubscribe).toHaveBeenCalled();
  });

  it('unsubscribes the device even when the server call fails', async () => {
    const service = setup();
    subscription$.next(fakeSubscription());
    api.remove.mockReturnValue(throwError(() => new Error('offline')));

    await service.disable();

    expect(swPush.unsubscribe).toHaveBeenCalled();
  });

  it('does nothing on disable without the service worker', async () => {
    const service = setup({ enabled: false });
    await service.disable();
    expect(api.remove).not.toHaveBeenCalled();
  });

  it('re-saves an existing subscription at startup', async () => {
    const service = setup();
    subscription$.next(fakeSubscription());

    service.init();
    await vi.waitFor(() => expect(api.save).toHaveBeenCalled());
  });

  it('drops a subscription made with another server key', async () => {
    const service = setup();
    subscription$.next(fakeSubscription('BAAA' + PUBLIC_KEY.slice(4)));

    service.init();
    await vi.waitFor(() => expect(swPush.unsubscribe).toHaveBeenCalled());
    expect(api.save).not.toHaveBeenCalled();
  });

  it('refreshes the news badge when a push arrives', () => {
    const service = setup();
    service.init();

    messages$.next({ notification: { title: 'Dinner' } });

    expect(refreshBadges).toHaveBeenCalled();
  });
});
