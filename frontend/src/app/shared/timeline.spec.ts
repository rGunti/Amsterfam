import { TimelineEntry } from '../core/models/timeline';
import { describeEntry, visibilityNote } from './timeline';

const alice = { id: 1, displayName: 'Alice', avatarUrl: null };
const bob = { id: 2, displayName: 'Bob', avatarUrl: null };

function entry(partial: Partial<TimelineEntry>): TimelineEntry {
  return {
    id: 1,
    type: 'EventCreated',
    visibility: 'Everyone',
    occurredAt: '2030-01-01T00:00:00Z',
    actor: alice,
    subject: null,
    data: null,
    ...partial,
  };
}

describe('describeEntry', () => {
  it('names the actor, or "You" for the viewer', () => {
    const e = entry({ type: 'OrganiserPromoted', subject: bob });
    expect(describeEntry(e, 99).text).toBe('Alice made Bob an organiser');
    expect(describeEntry(e, 1).text).toBe('You made Bob an organiser');
    expect(describeEntry(e, 2).text).toBe('Alice made you an organiser');
  });

  it('describes automatic status changes without an actor', () => {
    const e = entry({
      type: 'StatusChanged',
      actor: null,
      data: { from: 'Open', to: 'InProgress' },
    });
    expect(describeEntry(e, 1).text).toBe('The event moved to In progress automatically');
  });

  it('lists the changed fields', () => {
    const e = entry({
      type: 'EventDetailsUpdated',
      data: { changed: ['name', 'location', 'banner'] },
    });
    expect(describeEntry(e, 99).text).toBe('Alice updated the name, location and banner image');
  });

  it('uses the right possessive for travel dates', () => {
    const own = entry({
      type: 'TravelDatesChanged',
      subject: alice,
      data: { arrival: null, departure: null },
    });
    expect(describeEntry(own, 99).text).toBe('Alice updated their travel dates');
    expect(describeEntry(own, 1).text).toBe('You updated your travel dates');

    const other = entry({ type: 'TravelDatesChanged', subject: bob, data: {} });
    expect(describeEntry(other, 99).text).toBe("Alice updated Bob's travel dates");
  });

  it('notes restricted visibility', () => {
    expect(visibilityNote('Everyone')).toBeNull();
    expect(visibilityNote('Organisers')).toBe('Only organisers see this');
  });
});
