import { resolveLanguage } from './languages';

describe('resolveLanguage', () => {
  it('prefers the account preference over everything else', () => {
    expect(
      resolveLanguage({ userPreference: 'es', storedChoice: 'fr', browserLanguages: ['en-US'] }),
    ).toBe('es');
  });

  it('uses the stored choice when there is no account preference', () => {
    expect(resolveLanguage({ storedChoice: 'fr', browserLanguages: ['es-ES'] })).toBe('fr');
  });

  it('uses the first supported browser language, ignoring unsupported ones', () => {
    expect(resolveLanguage({ browserLanguages: ['de-DE', 'pt-BR', 'es-419', 'fr'] })).toBe('es');
  });

  it('normalizes region subtags and case', () => {
    expect(resolveLanguage({ browserLanguages: ['FR_ca'] })).toBe('fr');
  });

  it('skips invalid stored values instead of trusting them', () => {
    expect(resolveLanguage({ storedChoice: 'klingon', browserLanguages: ['fr-FR'] })).toBe('fr');
  });

  it('falls back to English', () => {
    expect(resolveLanguage({ browserLanguages: ['de-DE'] })).toBe('en');
    expect(resolveLanguage({})).toBe('en');
  });
});
