/**
 * Whether the event mirrors news to a Telegram group. The bot token itself is never returned;
 * `configured` needs both a saved token and a picked group.
 */
export interface TelegramStatus {
  configured: boolean;
  tokenSaved: boolean;
  /** The token's last few characters, to tell bots apart. */
  tokenHint: string | null;
  botUsername: string | null;
  chatId: number | null;
  chatTitle: string | null;
}

/** A group the bot has heard from recently. */
export interface TelegramChatOption {
  id: number;
  title: string;
  type: string;
}
