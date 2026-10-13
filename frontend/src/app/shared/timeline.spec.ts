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

  it('marks everyone mentioned, with their avatar', () => {
    const e = entry({ type: 'OrganiserPromoted', subject: bob });
    expect(describeEntry(e, 2).parts).toEqual([
      { text: 'Alice', user: true, avatar: { url: null, initial: 'A' } },
      { text: ' made ', user: false },
      { text: 'you', user: true, avatar: { url: null, initial: 'B' } },
      { text: ' an organiser', user: false },
    ]);

    const declined = entry({ type: 'JoinRequestDeclined', subject: bob });
    expect(describeEntry(declined, 1).parts).toEqual([
      { text: 'You', user: true, avatar: { url: null, initial: 'A' } },
      { text: ' declined ', user: false },
      { text: 'Bob', user: true, avatar: { url: null, initial: 'B' } },
      { text: "'s request to join", user: false },
    ]);
  });

  it('keeps self-references plain', () => {
    const e = entry({ type: 'DatePollResponded' });
    expect(describeEntry(e, 1).parts).toEqual([
      { text: 'You', user: true, avatar: { url: null, initial: 'A' } },
      { text: ' updated your availability', user: false },
    ]);
  });

  it('describes automatic status changes without an actor', () => {
    const e = entry({
      type: 'StatusChanged',
      actor: null,
      data: { from: 'Open', to: 'InProgress' },
    });
    expect(describeEntry(e, 1).text).toBe('The event moved to “In progress” automatically');
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

  it('describes expenses with their amount in the event currency', () => {
    const e = entry({
      type: 'ExpenseAdded',
      data: { title: 'Groceries', amount: 42.5, currency: 'EUR' },
    });
    expect(describeEntry(e, 99).text).toBe('Alice added the expense “Groceries” (€42.50)');
  });

  it('describes repayments from the viewer’s point of view', () => {
    const e = entry({
      type: 'PaymentRecorded',
      data: { fromId: 2, from: 'Bob', toId: 1, to: 'Alice', amount: 15, currency: 'GBP' },
    });
    expect(describeEntry(e, 99).text).toBe('Alice recorded that Bob paid Alice £15.00');
    expect(describeEntry(e, 2).text).toBe('Alice recorded that you paid Alice £15.00');
  });

  it('describes news posts by title, falling back when there is none', () => {
    const posted = entry({ type: 'NewsPosted', data: { postId: 3, title: 'Packing list' } });
    expect(describeEntry(posted, 99).text).toBe('Alice posted news: “Packing list”');
    expect(describeEntry(posted, 99).icon).toBe('campaign');

    const untitled = entry({ type: 'NewsPosted', data: { postId: 3, title: null } });
    expect(describeEntry(untitled, 1).text).toBe('You posted news');

    const edited = entry({ type: 'NewsEdited', data: { postId: 3, title: 'Packing' } });
    expect(describeEntry(edited, 99).text).toBe('Alice edited a news post: “Packing”');
    const image = entry({
      type: 'NewsEdited',
      data: { postId: 3, title: 'Packing', changed: ['image'] },
    });
    expect(describeEntry(image, 99).text).toBe('Alice changed the image of a news post: “Packing”');
    const deleted = entry({ type: 'NewsDeleted', data: { postId: 3, title: 'Packing' } });
    expect(describeEntry(deleted, 99).text).toBe('Alice deleted a news post: “Packing”');
  });

  it('describes pinning and unpinning news posts', () => {
    const pinned = entry({ type: 'NewsPinned', data: { postId: 3, title: 'Deposit' } });
    expect(describeEntry(pinned, 99).text).toBe('Alice pinned a news post: “Deposit”');
    expect(describeEntry(pinned, 99).icon).toBe('push_pin');

    const unpinned = entry({ type: 'NewsUnpinned', data: { postId: 3, title: null } });
    expect(describeEntry(unpinned, 99).text).toBe('Alice unpinned a news post');
  });

  it('describes scheduling a news post with its time', () => {
    const scheduled = entry({
      type: 'NewsScheduled',
      data: { postId: 3, title: 'Dinner', publishAt: '2030-07-02T18:00:00Z' },
    });
    const line = describeEntry(scheduled, 99);
    expect(line.text).toMatch(/^Alice scheduled a news post “Dinner” for .*2/);
    expect(line.icon).toBe('schedule_send');

    const untitled = entry({ type: 'NewsScheduled', data: { postId: 3, title: null } });
    expect(describeEntry(untitled, 99).text).toBe('Alice scheduled a news post');
  });

  it('describes an organiser removing a comment, without its text', () => {
    const removed = entry({
      type: 'NewsCommentRemoved',
      subject: bob,
      data: { postId: 3, title: 'Deposit' },
    });
    expect(describeEntry(removed, 99).text).toBe('Alice removed a comment by Bob on “Deposit”');
    expect(describeEntry(removed, 99).icon).toBe('speaker_notes_off');

    const untitled = entry({ type: 'NewsCommentRemoved', subject: bob, data: { postId: 3 } });
    expect(describeEntry(untitled, 99).text).toBe('Alice removed a comment by Bob on a news post');
  });

  it('notes restricted visibility', () => {
    expect(visibilityNote('Everyone')).toBeNull();
    expect(visibilityNote('Organisers')).toBe('Only organisers see this');
  });
});
