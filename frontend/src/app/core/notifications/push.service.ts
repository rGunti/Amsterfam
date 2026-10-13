import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom, timeout } from 'rxjs';

import { PushApi } from '../api/push.api';
import { CurrentEventService } from '../event/current-event.service';
import { PushDevice, SavePushSubscriptionRequest } from '../models/push';
import { deviceName, isAppleMobile } from './device-name';

/** How long turning push off waits for the server, so logging out never hangs on it. */
export const REMOVE_TIMEOUT_MS = 5000;

/**
 * Whether push can be turned on here, and if not, why:
 * - `loading`: still asking the server.
 * - `ready`: it can.
 * - `install`: iPhone/iPad outside the Home Screen app, where Safari has no push.
 * - `unsupported`: this browser (or a build without the service worker) can't do push.
 * - `off`: push isn't set up on the server.
 * - `error`: the server couldn't be asked; it's tried again next start.
 * - `blocked`: the member blocked notifications for the app in the browser.
 */
export type PushStatus =
  | 'loading'
  | 'ready'
  | 'install'
  | 'unsupported'
  | 'off'
  | 'error'
  | 'blocked';

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

  /** The service worker runs and the browser has the Push API. */
  readonly supported =
    this.swPush.isEnabled &&
    typeof window !== 'undefined' &&
    'PushManager' in window &&
    'Notification' in window;

  private readonly server = signal<'loading' | 'on' | 'off' | 'error'>('loading');
  private readonly permission = signal(currentPermission());

  /** This device's subscription in the browser, if any. */
  private readonly subscription = toSignal(this.swPush.subscription, { initialValue: null });

  /** This device is subscribed. */
  readonly subscribed = computed(() => this.subscription() !== null);

  /** This device's endpoint, to tell it apart in {@link devices}. */
  readonly currentEndpoint = computed(() => this.subscription()?.endpoint ?? null);

  /** All the member's devices, as the server has them. */
  readonly devices = signal<PushDevice[]>([]);

  readonly status = computed<PushStatus>(() => {
    if (!this.supported) {
      return isAppleMobile() && !isStandalone() ? 'install' : 'unsupported';
    }
    const server = this.server();
    if (server !== 'on') {
      return server;
    }
    // A device that's already on can still be turned off.
    return this.permission() === 'denied' && !this.subscribed() ? 'blocked' : 'ready';
  });

  /** Called once at startup, once signed in. */
  init(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    // The device list works without push here, so other devices can be managed from anywhere.
    const loadDevices = () => void this.loadDevices().catch(() => undefined);
    if (!this.supported) {
      loadDevices();
      return;
    }

    // A push while the app is open means there's news: update the badge right away.
    this.swPush.messages.subscribe(() => this.currentEvent.refreshBadges());

    this.api.getConfig().subscribe({
      next: ({ publicKey }) => {
        this.publicKey = publicKey;
        this.server.set('on');
        void this.reconcile(publicKey);
      },
      // 404: push is off on the server; anything else: try again next start.
      error: (error: { status?: number }) => {
        this.server.set(error.status === 404 ? 'off' : 'error');
        loadDevices();
      },
    });
  }

  async enable(): Promise<void> {
    if (!this.publicKey) {
      throw new Error('Push notifications are not available.');
    }
    this.changes++;
    let subscription: PushSubscription;
    try {
      subscription = await this.swPush.requestSubscription({ serverPublicKey: this.publicKey });
    } finally {
      // The member may just have answered the browser's permission prompt.
      this.permission.set(currentPermission());
    }
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

function currentPermission(): NotificationPermission {
  return typeof Notification === 'undefined' ? 'default' : Notification.permission;
}

/** Running as the installed app rather than in a browser tab. */
function isStandalone(): boolean {
  if (typeof window === 'undefined') {
    return false;
  }
  const iosStandalone = (navigator as Navigator & { standalone?: boolean }).standalone === true;
  return iosStandalone || window.matchMedia?.('(display-mode: standalone)').matches === true;
}
