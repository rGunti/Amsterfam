import { APP_LOCALE } from './app-locale';

/** Currencies an event can use. Keep in sync with Currencies.cs on the backend. */
export const CURRENCIES: readonly string[] = [
  'EUR',
  'GBP',
  'CHF',
  'USD',
  'CAD',
  'AUD',
  'NZD',
  'SEK',
  'DKK',
  'NOK',
  'CZK',
  'PLN',
  'HUF',
  'RON',
  'BGN',
  'TRY',
];

export const DEFAULT_CURRENCY = 'EUR';

/** The currency's name in the app locale, e.g. "Swiss Franc", for pickers. */
export function currencyName(code: string): string {
  try {
    return new Intl.DisplayNames(APP_LOCALE, { type: 'currency' }).of(code) ?? code;
  } catch {
    return code;
  }
}

/** "€12.50", "CHF 12.50". Signs are left to the caller's wording ("owes", "is owed"). */
export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat(APP_LOCALE, { style: 'currency', currency }).format(amount);
}
