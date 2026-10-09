/**
 * Number and date formatting for the user's language. Pure functions: the caller passes the locale
 * (the current UI language), so formatting follows a language switch without a reload.
 * Null always renders as an em dash: unknown is never shown as zero.
 */
const UNKNOWN = '—';

export function formatMoney(
  value: number | null | undefined,
  currency: string | null,
  locale: string,
): string {
  if (value === null || value === undefined) {
    return UNKNOWN;
  }
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: currency ?? 'EUR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

/** Profit-style amount: always signed, so a gain and a loss are told apart without color. */
export function formatSignedMoney(
  value: number | null | undefined,
  currency: string | null,
  locale: string,
): string {
  if (value === null || value === undefined) {
    return UNKNOWN;
  }
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: currency ?? 'EUR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
    signDisplay: 'exceptZero',
  }).format(value);
}

/** Ratio to signed percent: 0.1234 → "+12.3 %". */
export function formatSignedPercent(ratio: number | null | undefined, locale: string): string {
  if (ratio === null || ratio === undefined) {
    return UNKNOWN;
  }
  return new Intl.NumberFormat(locale, {
    style: 'percent',
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
    signDisplay: 'exceptZero',
  }).format(ratio);
}

export function formatInteger(value: number | null | undefined, locale: string): string {
  return value === null || value === undefined
    ? UNKNOWN
    : new Intl.NumberFormat(locale).format(value);
}

export function formatDateTime(iso: string | null | undefined, locale: string): string {
  if (!iso) {
    return UNKNOWN;
  }
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(iso),
  );
}
