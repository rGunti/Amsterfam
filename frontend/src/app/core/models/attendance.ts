import { AttendanceRole } from './event';

export interface AttendeeResponse {
  userId: number;
  displayName: string;
  avatarUrl: string | null;
  role: AttendanceRole;
  plannedArrival: string | null;
  plannedDeparture: string | null;
  requestedOrganiser: boolean;
  /** Label of the join link used to request; only sent to organisers. */
  joinLinkLabel: string | null;
}
