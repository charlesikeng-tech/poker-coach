export const SUPPORTED_LANGUAGES = ['fr', 'en', 'es'] as const;

export type Language = (typeof SUPPORTED_LANGUAGES)[number];

export const FALLBACK_LANGUAGE: Language = 'en';

/** Endonyms: a language is always offered in its own name, whatever the current UI language. */
export const LANGUAGE_NAMES: Readonly<Record<Language, string>> = {
  fr: 'Français',
  en: 'English',
  es: 'Español',
};

export function isSupportedLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (SUPPORTED_LANGUAGES as readonly string[]).includes(value);
}

export interface LanguageSources {
  /** Saved on the user's account (available once signed in). */
  readonly userPreference?: string | null;
  /** Last choice made in this browser. */
  readonly storedChoice?: string | null;
  /** navigator.languages, most preferred first. */
  readonly browserLanguages?: readonly string[];
}

/** Resolution order (spec §13): user preference → stored choice → browser → English. */
export function resolveLanguage(sources: LanguageSources): Language {
  const candidates = [
    sources.userPreference,
    sources.storedChoice,
    ...(sources.browserLanguages ?? []),
  ];

  for (const candidate of candidates) {
    const language = toSupportedLanguage(candidate);
    if (language) {
      return language;
    }
  }

  return FALLBACK_LANGUAGE;
}

function toSupportedLanguage(tag: string | null | undefined): Language | null {
  if (!tag) {
    return null;
  }
  // 'fr-FR', 'es_419', 'EN' → primary subtag.
  const primary = tag.trim().toLowerCase().split(/[-_]/)[0];
  return isSupportedLanguage(primary) ? primary : null;
}
