import { Injectable, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom, map } from 'rxjs';

import { PushApi } from '../api/push.api';
import { CurrentEventService } from '../event/current-event.service';
import { SavePushSubscriptionRequest } from '../models/push';

/**
 * Push notifications for news on this device (ADR-017). Having a subscription is the opt-in:
 * turning it on subscribes through the service worker and stores the subscription on the
 * server; turning it off removes both.
 */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly swPush = inject(SwPush);
  private readonly api = inject(PushApi);
  private readonly currentEvent = inject(CurrentEventService);

  private publicKey: string | null = null;
  private started = false;

  /** The service worker runs and the server has push set up. */
  readonly available = signal(false);

  /** This device is subscribed. */
  readonly subscribed = toSignal(this.swPush.subscription.pipe(map((s) => s !== null)), {
    initialValue: false,
  });

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
        void this.resync(publicKey);
      },
      // 404: push is off on the server; anything else: try again next start.
      error: () => this.available.set(false),
    });
  }

  async enable(): Promise<void> {
    if (!this.publicKey) {
      throw new Error('Push notifications are not available.');
    }
    const subscription = await this.swPush.requestSubscription({
      serverPublicKey: this.publicKey,
    });
    await firstValueFrom(this.api.save(toRequest(subscription)));
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
    try {
      await firstValueFrom(this.api.remove(subscription.endpoint));
    } catch {
      // The server drops it on its own once the push service reports it gone.
    } finally {
      await this.swPush.unsubscribe();
    }
  }

  /**
   * Re-saves an existing subscription, so the server learns about it again if it dropped it.
   * One made with a different server key can't receive anything any more and is removed.
   */
  private async resync(publicKey: string): Promise<void> {
    const subscription = await firstValueFrom(this.swPush.subscription);
    if (!subscription) {
      return;
    }
    if (!sameKey(subscription.options?.applicationServerKey ?? null, publicKey)) {
      await this.swPush.unsubscribe().catch(() => undefined);
      return;
    }
    this.api.save(toRequest(subscription)).subscribe({ error: () => undefined });
  }
}

function toRequest(subscription: PushSubscription): SavePushSubscriptionRequest {
  const json = subscription.toJSON();
  return {
    endpoint: subscription.endpoint,
    keys: { p256dh: json.keys?.['p256dh'] ?? '', auth: json.keys?.['auth'] ?? '' },
  };
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
