import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom, timeout } from 'rxjs';

import { PushApi } from '../api/push.api';
import { CurrentEventService } from '../event/current-event.service';
import { PushDevice, SavePushSubscriptionRequest } from '../models/push';
import { deviceName } from './device-name';

/** How long turning push off waits for the server, so logging out never hangs on it. */
export const REMOVE_TIMEOUT_MS = 5000;

/**
 * Push notifications for news (ADR-017). Having a subscription is the opt-in: turning it on
 * subscribes through the service worker and stores the subscription on the server; turning it
 * off removes both. Members can also see, rename and revoke all their devices.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly swPush = inject(SwPush);
  private readonly api = inject(PushApi);
  private readonly currentEvent = inject(CurrentEventService);

  private publicKey: string | null = null;
  private started = false;

  /** Counts the member's own changes, so the startup check never undoes one made meanwhile. */
  private changes = 0;

  /** The service worker runs and the server has push set up. */
  readonly available = signal(false);

  /** This device's subscription in the browser, if any. */
  private readonly subscription = toSignal(this.swPush.subscription, { initialValue: null });

  /** This device is subscribed. */
  readonly subscribed = computed(() => this.subscription() !== null);

  /** This device's endpoint, to tell it apart in {@link devices}. */
  readonly currentEndpoint = computed(() => this.subscription()?.endpoint ?? null);

  /** All the member's devices, as the server has them. */
  readonly devices = signal<PushDevice[]>([]);

  /** Whether the browser has blocked notifications for the app. */
  get blocked(): boolean {
    return typeof Notification !== 'undefined' && Notification.permission === 'denied';
  }

  /** Called once at startup. */
  init(): void {
    if (this.started || !this.swPush.isEnabled) {
      return;
    }
    this.started = true;

    // A push while the app is open means there's news: update the badge right away.
    this.swPush.messages.subscribe(() => this.currentEvent.refreshBadges());

    this.api.getConfig().subscribe({
      next: ({ publicKey }) => {
        this.publicKey = publicKey;
        this.available.set(true);
        void this.reconcile(publicKey);
      },
      // 404: push is off on the server; anything else: try again next start.
      error: () => this.available.set(false),
    });
  }

  async enable(): Promise<void> {
    if (!this.publicKey) {
      throw new Error('Push notifications are not available.');
    }
    this.changes++;
    const subscription = await this.swPush.requestSubscription({
      serverPublicKey: this.publicKey,
    });
    try {
      await firstValueFrom(
        this.api.save({ ...toRequest(subscription), name: deviceName(navigator.userAgent) }),
      );
    } catch (error) {
      // Without the server knowing, the device would look on but never get anything.
      await this.swPush.unsubscribe().catch(() => undefined);
      throw error;
    }
    await this.loadDevices();
  }

  /** Best effort on the server; the device is unsubscribed either way. */
  async disable(): Promise<void> {
    // Without the service worker, `subscription` never emits.
    if (!this.swPush.isEnabled) {
      return;
    }
    const subscription = await firstValueFrom(this.swPush.subscription);
    if (!subscription) {
      return;
    }
    this.changes++;
    try {
      await firstValueFrom(this.api.remove(subscription.endpoint).pipe(timeout(REMOVE_TIMEOUT_MS)));
    } catch {
      // The server drops it on its own once the push service reports it gone.
    } finally {
      await this.swPush.unsubscribe();
    }
    await this.loadDevices().catch(() => undefined);
  }

  async loadDevices(): Promise<void> {
    this.devices.set(await firstValueFrom(this.api.list()));
  }

  async rename(id: number, name: string): Promise<void> {
    await firstValueFrom(this.api.rename(id, name));
    this.devices.update((devices) => devices.map((d) => (d.id === id ? { ...d, name } : d)));
  }

  /** Revokes any of the member's devices; revoking this one also unsubscribes it here. */
  async revoke(device: PushDevice): Promise<void> {
    this.changes++;
    await firstValueFrom(this.api.revoke(device.id));
    if (device.endpoint === this.currentEndpoint()) {
      await this.swPush.unsubscribe().catch(() => undefined);
    }
    this.devices.update((devices) => devices.filter((d) => d.id !== device.id));
  }

  /**
   * Brings this device in line with the server at startup. A subscription the server no longer
   * lists (revoked from another device, dropped past the device limit, or saved by someone
   * else since) is removed here too, so the toggle shows it's off. One made with a different
   * server key can't receive anything any more and is removed as well.
   */
  private async reconcile(publicKey: string): Promise<void> {
    const changes = this.changes;
    try {
      await this.loadDevices();
    } catch {
      return;
    }
    const subscription = await firstValueFrom(this.swPush.subscription);
    if (!subscription || this.changes !== changes) {
      return;
    }
    const known = this.devices().some((d) => d.endpoint === subscription.endpoint);
    if (!known || !sameKey(subscription.options?.applicationServerKey ?? null, publicKey)) {
      await this.swPush.unsubscribe().catch(() => undefined);
    }
  }
}

function toRequest(subscription: PushSubscription): SavePushSubscriptionRequest {
  const keys = subscription.toJSON().keys;
  const p256dh = keys?.['p256dh'];
  const auth = keys?.['auth'];
  if (!p256dh || !auth) {
    throw new Error('The browser returned a push subscription without keys.');
  }
  return { endpoint: subscription.endpoint, keys: { p256dh, auth } };
}

function sameKey(key: ArrayBuffer | null, base64Url: string): boolean {
  if (!key) {
    return true;
  }
  const bytes = new Uint8Array(key);
  let binary = '';
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  const encoded = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return encoded === base64Url.replace(/=+$/, '');
}
