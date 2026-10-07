import { provideHttpClient, withFetch } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';

import { routes } from './app.routes';
import { LanguageService } from './core/i18n/language.service';
import { FALLBACK_LANGUAGE, SUPPORTED_LANGUAGES } from './core/i18n/languages';
import { TranslatedTitleStrategy } from './core/i18n/translated-title-strategy';
import { TranslocoHttpLoader } from './core/i18n/transloco-http-loader';
import { ThemeService } from './core/theme/theme.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch()),
    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: FALLBACK_LANGUAGE,
        fallbackLang: FALLBACK_LANGUAGE,
        missingHandler: { useFallbackTranslation: true, logMissingKey: isDevMode() },
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideAppInitializer(() => {
      inject(ThemeService).initialize();
      return inject(LanguageService).initialize();
    }),
  ],
};
