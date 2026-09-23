export type TimelineEntryType =
  | 'EventCreated'
  | 'EventDetailsUpdated'
  | 'StatusChanged'
  | 'PollRangeChanged'
  | 'DatePollResponded'
  | 'JoinRequested'
  | 'JoinRequestDeclined'
  | 'JoinRequestWithdrawn'
  | 'AttendeeConfirmed'
  | 'AttendeeLeft'
  | 'AttendeeRemoved'
  | 'OrganiserPromoted'
  | 'OrganiserDemoted'
  | 'OwnershipTransferred'
  | 'TravelDatesChanged'
  | 'CostOverrideChanged'
  | 'JoinLinkCreated'
  | 'JoinLinkRevoked'
  | 'JoinLinkRegenerated';

/** Who can see an entry: everyone in the event, organisers, or only the owner. */
export type TimelineVisibility = 'Everyone' | 'Organisers' | 'Owner';

export interface TimelineUser {
  id: number;
  displayName: string;
  avatarUrl: string | null;
}

export interface TimelineEntry {
  id: number;
  type: TimelineEntryType;
  visibility: TimelineVisibility;
  occurredAt: string;
  /** Null for automatic changes. */
  actor: TimelineUser | null;
  /** The attendee the change is about, if any. */
  subject: TimelineUser | null;
  /** Type-specific details, e.g. `{ from, to }` for status changes. */
  data: Record<string, unknown> | null;
}
