export type DatePollStatus = 'Available' | 'Unavailable' | 'Partial';

export interface DatePollEntry {
  weekStart: string; // DateOnly "yyyy-MM-dd", Monday of the ISO week
  status: DatePollStatus;
}

export interface UpdateDatePollEntriesRequest {
  entries: DatePollEntry[];
}

export interface DatePollWeekSummary {
  weekStart: string;
  available: number;
  unavailable: number;
  partial: number;
  noResponse: number;
}

export interface DatePollSummaryResponse {
  pollRangeStart: string | null;
  pollRangeEnd: string | null;
  weeks: DatePollWeekSummary[];
}
