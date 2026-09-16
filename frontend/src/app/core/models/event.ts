export type EventStatus = 'Draft' | 'Open' | 'Closed';
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
  costPerNight: number | null;
  status: EventStatus;
  createdAt: string;
  currentUserRole: AttendanceRole | null;
  isMember: boolean;
  createdById: number;
  organisers: OrganiserSummary[];
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
  costPerNight: number | null;
}

export interface CreateEventRequest {
  name: string;
  description: string | null;
  startDate: string | null;
  endDate: string | null;
  location: string;
  costPerNight: number | null;
}
