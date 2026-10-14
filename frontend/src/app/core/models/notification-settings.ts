/** News by email (ADR-018), from `GET /api/v1/me/notification-settings`. */
export interface NotificationSettings {
  /** False while the server has no SMTP set up; the profile page hides email then. */
  emailAvailable: boolean;
  emailNotifications: boolean;
  /** The address from the login, or null when the login sent none. */
  loginEmail: string | null;
  /** A custom address, confirmed or still waiting for confirmation. */
  notificationEmail: string | null;
  notificationEmailConfirmed: boolean;
  /** Where news goes right now, or null when there's no address. */
  effectiveEmail: string | null;
}
