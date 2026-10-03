import { formatMoney } from './money';

describe('formatMoney', () => {
  it('formats in the event currency with two decimals', () => {
    expect(formatMoney(12.5, 'EUR')).toBe('€12.50');
    expect(formatMoney(3, 'GBP')).toBe('£3.00');
    // Intl puts a non-breaking space between the code and the amount.
    expect(formatMoney(7.25, 'CHF')).toMatch(/^CHF\s7\.25$/);
  });
});
