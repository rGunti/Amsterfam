import { statusStages } from './event-status';

const summary = (status: Parameters<typeof statusStages>[0]) =>
  statusStages(status).map((s) => `${s.status}:${s.state}`);

describe('statusStages', () => {
  it('marks earlier stages done and later ones upcoming', () => {
    expect(summary('Open')).toEqual([
      'Draft:done',
      'LookingForDate:done',
      'Open:current',
      'InProgress:upcoming',
      'Closed:upcoming',
      'Archived:upcoming',
    ]);
  });

  it('starts with only the draft stage active', () => {
    expect(summary('Draft')[0]).toBe('Draft:current');
    expect(
      statusStages('Draft')
        .slice(1)
        .every((s) => s.state === 'upcoming'),
    ).toBe(true);
  });

  it('checks off every stage before Archived', () => {
    expect(summary('Archived')).toEqual([
      'Draft:done',
      'LookingForDate:done',
      'Open:done',
      'InProgress:done',
      'Closed:done',
      'Archived:current',
    ]);
  });

  it('replaces Archived with Cancelled and greys out the rest', () => {
    expect(summary('Cancelled')).toEqual([
      'Draft:inactive',
      'LookingForDate:inactive',
      'Open:inactive',
      'InProgress:inactive',
      'Closed:inactive',
      'Cancelled:current',
    ]);
    expect(statusStages('Cancelled')[5].icon).toBe('cancel');
  });
});
