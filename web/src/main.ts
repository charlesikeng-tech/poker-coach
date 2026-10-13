import * as Sentry from '@sentry/angular';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

Sentry.init({
  dsn: 'https://cbad532c5ce4e91f2677188b8a31b113@o4512249532841984.ingest.de.sentry.io/4512249552830544',
  environment: 'production',
  integrations: [Sentry.browserTracingIntegration()],
  // Traces 10 % des transactions — suffisant pour détecter les lenteurs sans exploser le quota.
  tracesSampleRate: 0.1,
});

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
