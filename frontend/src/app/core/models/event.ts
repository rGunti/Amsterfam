export type EventStatus = 'Draft' | 'Open' | 'Closed';
export type AttendanceRole = 'Pending' | 'Attendee' | 'Organiser';

export interface EventResponse {
  id: number;
  name: string;
  description: string | null;
  startDate: string; // DateOnly "yyyy-MM-dd"
  endDate: string;
  location: string;
  pollRangeStart: string | null;
  pollRangeEnd: string | null;
  costPerNight: number;
  status: EventStatus;
  createdAt: string;
  currentUserRole: AttendanceRole | null;
}

export interface UpdatePollRangeRequest {
  pollRangeStart: string | null;
  pollRangeEnd: string | null;
}

export interface UpdateEventRequest {
  name: string;
  description: string | null;
  startDate: string;
  endDate: string;
  location: string;
  costPerNight: number;
}
