import { TimelineEntry, TimelineUser, TimelineVisibility } from '../core/models/timeline';
import { EventStatus } from '../core/models/event';
import { APP_LOCALE } from './app-locale';
import { statusIcon, statusLabel } from './event-status';

/**
 * A run of sentence text. `user` marks a reference to a person so it can be highlighted;
 * `avatar` is set when their picture should be shown in front of it.
 */
export interface TimelinePart {
  text: string;
  user: boolean;
  avatar?: { url: string | null };
}

export interface TimelineLine {
  icon: string;
  parts: TimelinePart[];
  /** The whole sentence as plain text. */
  text: string;
}

const person = (text: string): TimelinePart => ({ text, user: true });
const personWithAvatar = (text: string, u: TimelineUser): TimelinePart => ({
  text,
  user: true,
  avatar: { url: u.avatarUrl },
});

/**
 * Template tag that builds sentence parts. Interpolated parts keep their `user` flag;
 * everything else becomes plain text, merged with its neighbours.
 */
function t(strings: TemplateStringsArray, ...values: unknown[]): TimelinePart[] {
  const parts: TimelinePart[] = [];
  const push = (part: TimelinePart) => {
    const last = parts.at(-1);
    if (last && !last.user && !part.user) {
      last.text += part.text;
    } else if (part.text) {
      parts.push({ ...part });
    }
  };
  strings.forEach((s, i) => {
    push({ text: s, user: false });
    if (i < values.length) {
      const value = values[i];
      const items = Array.isArray(value) ? value : [value];
      for (const item of items) {
        push(isPart(item) ? item : { text: String(item), user: false });
      }
    }
  });
  return parts;
}

function isPart(value: unknown): value is TimelinePart {
  return typeof value === 'object' && value !== null && 'text' in value && 'user' in value;
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
  const { icon, parts } = describeParts(entry, viewerId);
  return { icon, parts, text: parts.map((p) => p.text).join('') };
}

function describeParts(
  entry: TimelineEntry,
  viewerId: number | null,
): { icon: string; parts: TimelinePart[] } {
  // Everyone mentioned is highlighted. The actor's avatar already leads the entry, so only
  // the subject gets an inline one.
  const isMe = (u: TimelineUser | null) => u !== null && u.id === viewerId;
  const actor = entry.actor
    ? person(isMe(entry.actor) ? 'You' : entry.actor.displayName)
    : 'Someone';
  const subjectOf = (u: TimelineUser | null) =>
    u ? personWithAvatar(isMe(u) ? 'you' : u.displayName, u) : 'someone';
  const whose = (u: TimelineUser | null): TimelinePart[] | TimelinePart | string =>
    u === null
      ? "someone's"
      : isMe(u)
        ? personWithAvatar('your', u)
        : t`${personWithAvatar(u.displayName, u)}'s`;

  const subject = subjectOf(entry.subject);
  const data = entry.data ?? {};
  const self = entry.actor !== null && entry.actor.id === entry.subject?.id;
  // "their" reads oddly after "You", so match the pronoun to the actor.
  const own = isMe(entry.actor) ? 'your' : 'their';

  switch (entry.type) {
    case 'EventCreated':
      return {
        icon: 'celebration',
        parts: entry.actor ? t`${actor} created the event` : t`The event was created`,
      };
    case 'EventDetailsUpdated': {
      const fields = ((data['changed'] as string[] | undefined) ?? []).map(
        (f) => FIELD_NAMES[f] ?? f,
      );
      return { icon: 'edit', parts: t`${actor} updated the ${listOf(fields) || 'details'}` };
    }
    case 'StatusChanged': {
      const to = data['to'] as EventStatus;
      return {
        icon: statusIcon(to),
        parts: entry.actor
          ? t`${actor} moved the event to ${statusLabel(to)}`
          : t`The event moved to ${statusLabel(to)} automatically`,
      };
    }
    case 'PollRangeChanged':
      return {
        icon: 'date_range',
        parts: data['start']
          ? t`${actor} set the date poll to ${shortDate(data['start'])} – ${shortDate(data['end'])}`
          : t`${actor} cleared the date poll range`,
      };
    case 'DatePollResponded':
      return { icon: 'event_available', parts: t`${actor} updated ${own} availability` };
    case 'JoinRequested': {
      const as = data['requestedOrganiser'] ? ' as organiser' : '';
      const via = data['link'] ? ` via “${data['link']}”` : '';
      return { icon: 'person_add', parts: t`${actor} asked to join${as}${via}` };
    }
    case 'JoinRequestDeclined':
      return {
        icon: 'person_remove',
        parts: t`${actor} declined ${whose(entry.subject)} request to join`,
      };
    case 'JoinRequestWithdrawn':
      return { icon: 'undo', parts: t`${actor} withdrew ${own} request to join` };
    case 'AttendeeConfirmed':
      return {
        icon: 'how_to_reg',
        parts:
          data['role'] === 'Organiser'
            ? t`${actor} confirmed ${subject} as organiser`
            : t`${actor} confirmed ${subject}`,
      };
    case 'AttendeeLeft':
      return { icon: 'logout', parts: t`${actor} left the event` };
    case 'AttendeeRemoved':
      return { icon: 'person_remove', parts: t`${actor} removed ${subject} from the event` };
    case 'OrganiserPromoted':
      return { icon: 'admin_panel_settings', parts: t`${actor} made ${subject} an organiser` };
    case 'OrganiserDemoted':
      return { icon: 'remove_moderator', parts: t`${actor} removed ${subject} as organiser` };
    case 'OwnershipTransferred':
      return { icon: 'key', parts: t`${actor} handed ownership of the event to ${subject}` };
    case 'TravelDatesChanged': {
      const arrival = data['arrival'] ? `arriving ${shortDate(data['arrival'])}` : null;
      const departure = data['departure'] ? `leaving ${shortDate(data['departure'])}` : null;
      const dates = [arrival, departure].filter((d) => d !== null).join(', ');
      const target = self ? own : whose(entry.subject);
      return {
        icon: 'flight',
        parts: t`${actor} updated ${target} travel dates${dates ? ` (${dates})` : ''}`,
      };
    }
    case 'CostOverrideChanged':
      return {
        icon: 'payments',
        parts:
          data['costOverride'] === null || data['costOverride'] === undefined
            ? t`${actor} cleared the cost override for ${subject}`
            : t`${actor} set a cost override of ${data['costOverride']} for ${subject}`,
      };
    case 'JoinLinkCreated':
      return { icon: 'add_link', parts: t`${actor} created the join link “${data['label']}”` };
    case 'JoinLinkRevoked':
      return { icon: 'link_off', parts: t`${actor} revoked the join link “${data['label']}”` };
    case 'JoinLinkRegenerated':
      return { icon: 'autorenew', parts: t`${actor} regenerated the join link “${data['label']}”` };
    default:
      return { icon: 'history', parts: t`${actor} changed something` };
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
