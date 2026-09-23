import { TimelineEntry, TimelineUser, TimelineVisibility } from '../core/models/timeline';
import { EventStatus } from '../core/models/event';
import { APP_LOCALE } from './app-locale';
import { statusIcon, statusLabel } from './event-status';

export interface TimelineLine {
  icon: string;
  text: string;
}

const FIELD_NAMES: Record<string, string> = {
  name: 'name',
  description: 'description',
  dates: 'dates',
  location: 'location',
  banner: 'banner image',
};

/** "a", "a and b", "a, b and c". */
function listOf(items: string[]): string {
  return items.length <= 1
    ? (items[0] ?? '')
    : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}

function shortDate(iso: unknown): string {
  return typeof iso === 'string'
    ? new Date(`${iso}T00:00:00`).toLocaleDateString(APP_LOCALE, {
        day: 'numeric',
        month: 'short',
        year: 'numeric',
      })
    : '?';
}

/** Turns a timeline entry into a sentence from the point of view of `viewerId`. */
export function describeEntry(entry: TimelineEntry, viewerId: number | null): TimelineLine {
  const isMe = (u: TimelineUser | null) => u !== null && u.id === viewerId;
  const who = (u: TimelineUser | null, start = false) =>
    u === null ? 'Someone' : isMe(u) ? (start ? 'You' : 'you') : u.displayName;
  const whose = (u: TimelineUser | null) =>
    isMe(u) ? 'your' : u === null ? "someone's" : `${u.displayName}'s`;

  const actor = who(entry.actor, true);
  const subject = who(entry.subject);
  const data = entry.data ?? {};
  const self = entry.actor !== null && entry.actor.id === entry.subject?.id;
  // "their" reads oddly after "You", so match the pronoun to the actor.
  const own = isMe(entry.actor) ? 'your' : 'their';

  switch (entry.type) {
    case 'EventCreated':
      return {
        icon: 'celebration',
        text: entry.actor ? `${actor} created the event` : 'The event was created',
      };
    case 'EventDetailsUpdated': {
      const fields = ((data['changed'] as string[] | undefined) ?? []).map(
        (f) => FIELD_NAMES[f] ?? f,
      );
      return { icon: 'edit', text: `${actor} updated the ${listOf(fields) || 'details'}` };
    }
    case 'StatusChanged': {
      const to = data['to'] as EventStatus;
      return {
        icon: statusIcon(to),
        text: entry.actor
          ? `${actor} moved the event to ${statusLabel(to)}`
          : `The event moved to ${statusLabel(to)} automatically`,
      };
    }
    case 'PollRangeChanged':
      return {
        icon: 'date_range',
        text: data['start']
          ? `${actor} set the date poll to ${shortDate(data['start'])} – ${shortDate(data['end'])}`
          : `${actor} cleared the date poll range`,
      };
    case 'DatePollResponded':
      return { icon: 'event_available', text: `${actor} updated ${own} availability` };
    case 'JoinRequested': {
      const as = data['requestedOrganiser'] ? ' as organiser' : '';
      const via = data['link'] ? ` via “${data['link']}”` : '';
      return { icon: 'person_add', text: `${actor} asked to join${as}${via}` };
    }
    case 'JoinRequestDeclined':
      return {
        icon: 'person_remove',
        text: `${actor} declined ${whose(entry.subject)} request to join`,
      };
    case 'JoinRequestWithdrawn':
      return { icon: 'undo', text: `${actor} withdrew ${own} request to join` };
    case 'AttendeeConfirmed':
      return {
        icon: 'how_to_reg',
        text:
          data['role'] === 'Organiser'
            ? `${actor} confirmed ${subject} as organiser`
            : `${actor} confirmed ${subject}`,
      };
    case 'AttendeeLeft':
      return { icon: 'logout', text: `${actor} left the event` };
    case 'AttendeeRemoved':
      return { icon: 'person_remove', text: `${actor} removed ${subject} from the event` };
    case 'OrganiserPromoted':
      return { icon: 'admin_panel_settings', text: `${actor} made ${subject} an organiser` };
    case 'OrganiserDemoted':
      return { icon: 'remove_moderator', text: `${actor} removed ${subject} as organiser` };
    case 'OwnershipTransferred':
      return { icon: 'key', text: `${actor} handed ownership of the event to ${subject}` };
    case 'TravelDatesChanged': {
      const arrival = data['arrival'] ? `arriving ${shortDate(data['arrival'])}` : null;
      const departure = data['departure'] ? `leaving ${shortDate(data['departure'])}` : null;
      const dates = [arrival, departure].filter((d) => d !== null).join(', ');
      const target = self ? own : whose(entry.subject);
      return {
        icon: 'flight',
        text: `${actor} updated ${target} travel dates${dates ? ` (${dates})` : ''}`,
      };
    }
    case 'CostOverrideChanged':
      return {
        icon: 'payments',
        text:
          data['costOverride'] === null || data['costOverride'] === undefined
            ? `${actor} cleared the cost override for ${subject}`
            : `${actor} set a cost override of ${data['costOverride']} for ${subject}`,
      };
    case 'JoinLinkCreated':
      return { icon: 'add_link', text: `${actor} created the join link “${data['label']}”` };
    case 'JoinLinkRevoked':
      return { icon: 'link_off', text: `${actor} revoked the join link “${data['label']}”` };
    case 'JoinLinkRegenerated':
      return { icon: 'autorenew', text: `${actor} regenerated the join link “${data['label']}”` };
    default:
      return { icon: 'history', text: `${actor} changed something` };
  }
}

/** Tooltip for entries not everyone can see; null for public ones. */
export function visibilityNote(visibility: TimelineVisibility): string | null {
  switch (visibility) {
    case 'Organisers':
      return 'Only organisers see this';
    case 'Owner':
      return 'Only the owner sees this';
    default:
      return null;
  }
}
