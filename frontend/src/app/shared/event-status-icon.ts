import { EventStatus } from '../core/models/event';

const STATUS_ICONS: Record<EventStatus, string> = {
  Draft: 'edit_note',
  Open: 'check_circle',
  Closed: 'lock',
};

export function statusIcon(status: EventStatus): string {
  return STATUS_ICONS[status];
}
