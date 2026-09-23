import { EventResponse, EventStatus } from '../core/models/event';
import { ConfirmDialogData } from './confirm-dialog/confirm-dialog';

interface StatusMeta {
  label: string;
  icon: string;
}

const STATUS_META: Record<EventStatus, StatusMeta> = {
  Draft: { label: 'Draft', icon: 'edit_note' },
  LookingForDate: { label: 'Looking for date', icon: 'event_available' },
  Open: { label: 'Open', icon: 'check_circle' },
  InProgress: { label: 'In progress', icon: 'flight_takeoff' },
  Closed: { label: 'Closed', icon: 'receipt_long' },
  Archived: { label: 'Archived', icon: 'inventory_2' },
  Cancelled: { label: 'Cancelled', icon: 'cancel' },
};

export function statusIcon(status: EventStatus): string {
  return STATUS_META[status].icon;
}

export function statusLabel(status: EventStatus): string {
  return STATUS_META[status].label;
}

/** CSS class for status-coloured icons and chips, e.g. `status-lookingfordate`. */
export function statusClass(status: EventStatus): string {
  return `status-${status.toLowerCase()}`;
}

// Mirrors EventStateMachine on the backend. The server is the source of truth for which
// transitions are allowed (EventResponse.allowedTransitions); these only drive presentation.
export const isReadOnly = (status: EventStatus) => status === 'Archived' || status === 'Cancelled';
export const areDatesLocked = (status: EventStatus) =>
  status !== 'Draft' && status !== 'LookingForDate';
export const acceptsJoins = (status: EventStatus) =>
  status === 'LookingForDate' || status === 'Open';

/** Organisers manage join links until the event is archived or cancelled. */
export function canManageJoinLinks(ev: EventResponse): boolean {
  return ev.currentUserRole === 'Organiser' && !isReadOnly(ev.status);
}

/** Confirmed members see the timeline; pending requests don't. */
export function canViewTimeline(ev: EventResponse): boolean {
  return ev.currentUserRole === 'Attendee' || ev.currentUserRole === 'Organiser';
}

/**
 * Whether the overview's share button applies. Drafts only take organiser links, which
 * only the owner can create; attendee links work while joins are accepted.
 */
export function canShareJoinLink(ev: EventResponse, isOwner: boolean): boolean {
  if (ev.currentUserRole !== 'Organiser') {
    return false;
  }
  return ev.status === 'Draft' ? isOwner : acceptsJoins(ev.status);
}

/**
 * Whether "Find a date" is available: confirmed members vote while the event is looking
 * for a date; organisers can already set up the poll range while it's a draft.
 */
export function canUseDatePoll(ev: EventResponse): boolean {
  if (ev.currentUserRole === 'Organiser') {
    return ev.status === 'Draft' || ev.status === 'LookingForDate';
  }
  return ev.currentUserRole === 'Attendee' && ev.status === 'LookingForDate';
}

export interface TransitionAction {
  target: EventStatus;
  label: string;
  icon: string;
  /** Shown in the owner-only Danger Zone instead of the regular action row. */
  danger: boolean;
  primary: boolean;
  /** Stepping back (e.g. Open → Draft): tucked into a "More" menu so forward moves stand out. */
  secondary: boolean;
  confirm: ConfirmDialogData | null;
  /** Longer explanation, shown next to Danger Zone actions. */
  description: string | null;
  done: string;
}

/** Presentation for each transition the current user may perform, in display order. */
export function transitionActions(ev: EventResponse): TransitionAction[] {
  return ev.allowedTransitions.map((target) => describeTransition(ev, target));
}

function describeTransition(ev: EventResponse, target: EventStatus): TransitionAction {
  const from = ev.status;
  const action = (
    label: string,
    icon: string,
    done: string,
    opts: Partial<
      Pick<TransitionAction, 'danger' | 'primary' | 'secondary' | 'confirm' | 'description'>
    > = {},
  ): TransitionAction => ({
    target,
    label,
    icon,
    done,
    danger: opts.danger ?? false,
    primary: opts.primary ?? false,
    secondary: opts.secondary ?? false,
    confirm: opts.confirm ?? null,
    description: opts.description ?? null,
  });

  switch (target) {
    case 'Draft':
      return action('Back to draft', 'undo', 'Event moved back to draft', { secondary: true });
    case 'LookingForDate':
      return from === 'Open'
        ? action('Unfix date', 'event_busy', 'Event is looking for a date again', {
            secondary: true,
          })
        : action('Look for a date', 'event_available', 'Event is now looking for a date', {
            primary: true,
          });
    case 'Open':
      if (from === 'InProgress' || from === 'Closed') {
        return action('Reset to Open', 'restart_alt', 'Event reset to Open', {
          danger: true,
          description: 'Move the event back to Open. Agendas, expenses and participants are kept.',
          confirm: {
            title: 'Reset event to Open',
            message: `This moves “${ev.name}” back to Open. Agendas, expenses and participants are kept, and automatic start/close is paused until you move the event on manually.`,
            confirmLabel: 'Reset',
          },
        });
      }
      return action('Open event', 'lock_open', 'Event is now open', { primary: true });
    case 'InProgress':
      return action('Start now', 'flight_takeoff', 'Event started', {
        confirm: {
          title: 'Start event now',
          message: `This starts “${ev.name}” ahead of its start date. It can't be moved back to an earlier state afterwards (only the owner can reset it from the Danger Zone). The event's dates stay unchanged.`,
          confirmLabel: 'Start',
        },
      });
    case 'Closed':
      return action('Close event', 'lock', 'Event closed', {
        confirm: {
          title: 'Close event',
          message: `This closes “${ev.name}” for wrap-up. It can't be moved back to an earlier state afterwards (only the owner can reset it from the Danger Zone). The event's dates stay unchanged.`,
          confirmLabel: 'Close',
        },
      });
    case 'Archived':
      return action('Archive', 'inventory_2', 'Event archived', {
        confirm: {
          title: 'Archive event',
          message: `Archiving “${ev.name}” makes it read-only. This cannot be undone.`,
          confirmLabel: 'Archive',
        },
      });
    case 'Cancelled':
      return action('Cancel event', 'cancel', 'Event cancelled', {
        danger: true,
        description: 'Call the event off. Only organisers will still be able to see it.',
        confirm: {
          title: 'Cancel event',
          message: `Cancelling “${ev.name}” makes it read-only and hides it from everyone except organisers. This cannot be undone.`,
          confirmLabel: 'Cancel event',
          dismissLabel: 'Keep event',
        },
      });
  }
}
