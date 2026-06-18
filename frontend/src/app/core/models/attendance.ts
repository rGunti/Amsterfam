import { AttendanceRole } from './event';

export interface AttendeeResponse {
  userId: number;
  displayName: string;
  role: AttendanceRole;
  plannedArrival: string | null;
  plannedDeparture: string | null;
}
