/** The VAPID public key the browser subscribes with (ADR-017). */
export interface PushConfig {
  publicKey: string;
}

/** Shaped like the browser's `PushSubscription.toJSON()`, plus a name for a new device. */
export interface SavePushSubscriptionRequest {
  endpoint: string;
  keys: { p256dh: string; auth: string };
  name?: string;
}

/** One of the member's subscribed devices. */
export interface PushDevice {
  id: number;
  name: string;
  endpoint: string;
  createdAt: string;
  lastSuccessAt: string | null;
}

export const MAX_DEVICE_NAME_LENGTH = 60;
