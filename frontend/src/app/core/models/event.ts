export type EventStatus =
  | 'Draft'
  | 'LookingForDate'
  | 'Open'
  | 'InProgress'
  | 'Closed'
  | 'Archived'
  | 'Cancelled';
export type AttendanceRole = 'Pending' | 'Attendee' | 'Organiser';

export interface OrganiserSummary {
  userId: number;
  displayName: string;
  avatarUrl: string | null;
}

export interface EventResponse {
  id: string;
  name: string;
  description: string | null;
  startDate: string | null; // DateOnly "yyyy-MM-dd"
  endDate: string | null;
  location: string;
  pollRangeStart: string | null;
  pollRangeEnd: string | null;
  status: EventStatus;
  createdAt: string;
  currentUserRole: AttendanceRole | null;
  isMember: boolean;
  createdById: number;
  organisers: OrganiserSummary[];
  /** Statuses the current user may move this event to right now (server-computed). */
  allowedTransitions: EventStatus[];
  /** True after an owner reset; automatic start/close is skipped until the next manual move. */
  autoTransitionsPaused: boolean;
  /** Requests waiting for approval; only sent to organisers. */
  pendingAttendeeCount: number | null;
}

export interface UpdatePollRangeRequest {
  pollRangeStart: string | null;
  pollRangeEnd: string | null;
}

export interface UpdateEventRequest {
  name: string;
  description: string | null;
  startDate: string | null;
  endDate: string | null;
  location: string;
}

export interface CreateEventRequest {
  name: string;
  description: string | null;
  startDate: string | null;
  endDate: string | null;
  location: string;
}
