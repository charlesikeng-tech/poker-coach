import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionService } from './session.service';

/** UX only: the API enforces authentication on every call regardless of what the web app shows. */
export const authenticatedGuard: CanActivateFn = (_route, state) => {
  if (inject(SessionService).status() === 'authenticated') {
    return true;
  }
  return inject(Router).createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};

export const anonymousOnlyGuard: CanActivateFn = () =>
  inject(SessionService).status() === 'authenticated'
    ? inject(Router).createUrlTree(['/dashboard'])
    : true;
