/** The VAPID public key the browser subscribes with (ADR-017). */
export interface PushConfig {
  publicKey: string;
}

/** Shaped like the browser's `PushSubscription.toJSON()`. */
export interface SavePushSubscriptionRequest {
  endpoint: string;
  keys: { p256dh: string; auth: string };
}
