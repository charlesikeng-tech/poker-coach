import * as Sentry from '@sentry/angular';
import { isDevMode } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

Sentry.init({
  dsn: isDevMode()
    ? 'https://8a3271308a05e4d75fd750015931914e@o4512249532841984.ingest.de.sentry.io/4512249615482960'
    : 'https://cbad532c5ce4e91f2677188b8a31b113@o4512249532841984.ingest.de.sentry.io/4512249552830544',
  environment: isDevMode() ? 'development' : 'production',
  integrations: [Sentry.browserTracingIntegration()],
  tracesSampleRate: 0.1,
});

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
