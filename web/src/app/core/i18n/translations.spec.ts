import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';
import fr from '../../../../public/i18n/fr.json';

type TranslationTree = { [key: string]: string | TranslationTree };

function flattenKeys(tree: TranslationTree, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([key, value]) =>
    typeof value === 'string' ? [`${prefix}${key}`] : flattenKeys(value, `${prefix}${key}.`),
  );
}

function emptyValues(tree: TranslationTree, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([key, value]) =>
    typeof value === 'string'
      ? value.trim() === ''
        ? [`${prefix}${key}`]
        : []
      : emptyValues(value, `${prefix}${key}.`),
  );
}

// English is the fallback language and the reference key set.
describe('translation files', () => {
  const reference = flattenKeys(en).sort();

  it.each([
    ['fr', fr],
    ['es', es],
  ] as const)('%s has exactly the same keys as en', (_, translations) => {
    expect(flattenKeys(translations).sort()).toEqual(reference);
  });

  it.each([
    ['en', en],
    ['fr', fr],
    ['es', es],
  ] as const)('%s has no empty values', (_, translations) => {
    expect(emptyValues(translations)).toEqual([]);
  });
});
