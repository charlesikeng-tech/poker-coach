import { formatMoney, formatSignedMoney, formatSignedPercent } from './format';

// Intl output uses narrow no-break spaces in fr: compare with spaces normalized.
const normalize = (text: string) => text.replace(/\s/g, ' ');

describe('format', () => {
  it('never shows an unknown value as zero', () => {
    expect(formatMoney(null, 'EUR', 'fr')).toBe('—');
    expect(formatSignedMoney(undefined, 'EUR', 'en')).toBe('—');
    expect(formatSignedPercent(null, 'en')).toBe('—');
  });

  it('signs profits and losses', () => {
    expect(formatSignedMoney(39.28, 'EUR', 'en')).toBe('+€39.28');
    expect(formatSignedMoney(-10, 'EUR', 'en')).toBe('-€10.00');
    expect(normalize(formatSignedMoney(-10, 'EUR', 'fr'))).toBe('-10,00 €');
  });

  it('turns a ratio into a signed percent', () => {
    expect(formatSignedPercent(1.952, 'en')).toBe('+195.2%');
    expect(formatSignedPercent(-0.125, 'en')).toBe('-12.5%');
  });
});
