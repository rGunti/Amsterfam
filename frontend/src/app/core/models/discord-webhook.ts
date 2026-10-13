/** Whether the event mirrors news to Discord. The webhook URL itself is never returned. */
export interface DiscordWebhookStatus {
  configured: boolean;
  /** The URL's last few characters, to tell webhooks apart. */
  hint: string | null;
}
