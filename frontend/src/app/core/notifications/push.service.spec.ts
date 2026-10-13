import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { BehaviorSubject, NEVER, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { PushApi } from '../api/push.api';
import { CurrentEventService } from '../event/current-event.service';
import { PushDevice } from '../models/push';
import { PushService, REMOVE_TIMEOUT_MS } from './push.service';

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
    list: ReturnType<typeof vi.fn>;
    save: ReturnType<typeof vi.fn>;
    remove: ReturnType<typeof vi.fn>;
    rename: ReturnType<typeof vi.fn>;
    revoke: ReturnType<typeof vi.fn>;
  };
  let serverDevices: PushDevice[];
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
      list: vi.fn(() => of(serverDevices)),
      save: vi.fn(() => of(undefined)),
      remove: vi.fn(() => of(undefined)),
      rename: vi.fn(() => of(undefined)),
      revoke: vi.fn(() => of(undefined)),
    };
    serverDevices = [];
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
      name: expect.any(String),
    });
    expect(service.subscribed()).toBe(true);
    expect(api.list).toHaveBeenCalled();
  });

  it('unsubscribes the device again when the server could not save it', async () => {
    const service = setup();
    service.init();
    api.save.mockReturnValue(throwError(() => new Error('500')));

    await expect(service.enable()).rejects.toThrow();

    expect(swPush.unsubscribe).toHaveBeenCalled();
    expect(service.subscribed()).toBe(false);
  });

  it('does not wait forever for the server when turning push off', async () => {
    vi.useFakeTimers();
    try {
      const service = setup();
      subscription$.next(fakeSubscription());
      api.remove.mockReturnValue(NEVER);

      const done = service.disable();
      await vi.advanceTimersByTimeAsync(REMOVE_TIMEOUT_MS);
      await done;

      expect(swPush.unsubscribe).toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
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

  const device = (overrides: Partial<PushDevice> = {}): PushDevice => ({
    id: 1,
    name: 'Chrome on Android',
    endpoint: 'https://fcm.googleapis.com/fcm/send/abc',
    createdAt: '2026-10-13T00:00:00Z',
    lastSuccessAt: null,
    ...overrides,
  });

  it('loads the devices at startup and keeps a subscription the server knows', async () => {
    const service = setup();
    serverDevices = [device()];
    subscription$.next(fakeSubscription());

    service.init();
    await vi.waitFor(() => expect(service.devices()).toHaveLength(1));
    expect(swPush.unsubscribe).not.toHaveBeenCalled();
    expect(api.save).not.toHaveBeenCalled();
  });

  it('drops a subscription the server no longer lists, e.g. revoked elsewhere', async () => {
    const service = setup();
    serverDevices = [device({ endpoint: 'https://fcm.googleapis.com/fcm/send/other' })];
    subscription$.next(fakeSubscription());

    service.init();
    await vi.waitFor(() => expect(swPush.unsubscribe).toHaveBeenCalled());
  });

  it('drops a subscription made with another server key', async () => {
    const service = setup();
    serverDevices = [device()];
    subscription$.next(fakeSubscription('BAAA' + PUBLIC_KEY.slice(4)));

    service.init();
    await vi.waitFor(() => expect(swPush.unsubscribe).toHaveBeenCalled());
  });

  it('does not undo turning push on while the startup check is running', async () => {
    const service = setup();
    service.init();
    await service.enable();
    await vi.waitFor(() => expect(api.list).toHaveBeenCalledTimes(2));

    expect(swPush.unsubscribe).not.toHaveBeenCalled();
    expect(service.subscribed()).toBe(true);
  });

  it('keeps the subscription when the device list cannot be loaded', async () => {
    const service = setup();
    api.list.mockReturnValue(throwError(() => new Error('offline')));
    subscription$.next(fakeSubscription());

    service.init();
    await vi.waitFor(() => expect(api.list).toHaveBeenCalled());
    expect(swPush.unsubscribe).not.toHaveBeenCalled();
  });

  it('renames a device', async () => {
    const service = setup();
    service.devices.set([device()]);

    await service.rename(1, 'Phone');

    expect(api.rename).toHaveBeenCalledWith(1, 'Phone');
    expect(service.devices()[0].name).toBe('Phone');
  });

  it('revokes another device without touching this one', async () => {
    const service = setup();
    subscription$.next(fakeSubscription());
    const other = device({ id: 2, endpoint: 'https://fcm.googleapis.com/fcm/send/other' });
    service.devices.set([device(), other]);

    await service.revoke(other);

    expect(api.revoke).toHaveBeenCalledWith(2);
    expect(swPush.unsubscribe).not.toHaveBeenCalled();
    expect(service.devices().map((d) => d.id)).toEqual([1]);
  });

  it('unsubscribes here when revoking this device', async () => {
    const service = setup();
    subscription$.next(fakeSubscription());
    service.devices.set([device()]);

    await service.revoke(device());

    expect(swPush.unsubscribe).toHaveBeenCalled();
  });

  it('refreshes the news badge when a push arrives', () => {
    const service = setup();
    service.init();

    messages$.next({ notification: { title: 'Dinner' } });

    expect(refreshBadges).toHaveBeenCalled();
  });
});
