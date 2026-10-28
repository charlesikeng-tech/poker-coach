import { WritableSignal } from '@angular/core';
import { LoaderCircle, RefreshCw, Sparkles } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { coachErrorCode } from './coaching-api';

export const coachPanelIcons = { LoaderCircle, RefreshCw, Sparkles };

export function coachDate(language: LanguageService): (value: string) => string {
  return (value) => formatDateTime(value, language.current());
}

export async function coachWrite(
  writing: WritableSignal<boolean>,
  writeError: WritableSignal<string | null>,
  action: () => Promise<void>,
): Promise<void> {
  if (writing()) return;
  writing.set(true);
  writeError.set(null);
  try {
    await action();
  } catch (error) {
    writeError.set(coachErrorCode(error));
  } finally {
    writing.set(false);
  }
}
