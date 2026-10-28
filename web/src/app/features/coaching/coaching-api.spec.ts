import { HttpErrorResponse } from '@angular/common/http';

import { coachErrorCode, notWrittenYet } from './coaching-api';

function httpError(status: number, code?: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: code ? { code } : null });
}

describe('coachErrorCode', () => {
  it('returns the code for a known coach error', () => {
    expect(coachErrorCode(httpError(402, 'COACHING_BUDGET_EXHAUSTED'))).toBe(
      'COACHING_BUDGET_EXHAUSTED',
    );
    expect(coachErrorCode(httpError(429, 'COACHING_DAILY_LIMIT'))).toBe('COACHING_DAILY_LIMIT');
    expect(coachErrorCode(httpError(404, 'NOT_ENOUGH_HANDS'))).toBe('NOT_ENOUGH_HANDS');
    expect(coachErrorCode(httpError(404, 'TOURNAMENT_NOT_FOUND'))).toBe('TOURNAMENT_NOT_FOUND');
    expect(coachErrorCode(httpError(503, 'COACHING_UNAVAILABLE'))).toBe('COACHING_UNAVAILABLE');
    expect(coachErrorCode(httpError(500, 'COACHING_FAILED'))).toBe('COACHING_FAILED');
    expect(coachErrorCode(httpError(403, 'PLAN_REQUIRED'))).toBe('PLAN_REQUIRED');
    expect(coachErrorCode(httpError(429, 'PLAN_LIMIT_REACHED'))).toBe('PLAN_LIMIT_REACHED');
  });

  it('returns "network" for status 0 (offline / CORS)', () => {
    expect(coachErrorCode(httpError(0))).toBe('network');
  });

  it('returns "unknown" for an HTTP error with an unrecognised code', () => {
    expect(coachErrorCode(httpError(500, 'SOMETHING_ELSE'))).toBe('unknown');
  });

  it('returns "unknown" for an HTTP error with no body code', () => {
    expect(coachErrorCode(httpError(500))).toBe('unknown');
  });

  it('returns "unknown" for a non-HTTP error', () => {
    expect(coachErrorCode(new Error('timeout'))).toBe('unknown');
    expect(coachErrorCode(null)).toBe('unknown');
    expect(coachErrorCode(undefined)).toBe('unknown');
  });
});

describe('notWrittenYet', () => {
  it('returns true for 404 with a *_NOT_FOUND code', () => {
    expect(notWrittenYet(httpError(404, 'DEBRIEF_NOT_FOUND'))).toBe(true);
    expect(notWrittenYet(httpError(404, 'REVIEW_NOT_FOUND'))).toBe(true);
    expect(notWrittenYet(httpError(404, 'CASH_DEBRIEF_NOT_FOUND'))).toBe(true);
    expect(notWrittenYet(httpError(404, 'MONTHLY_REPORT_NOT_FOUND'))).toBe(true);
    expect(notWrittenYet(httpError(404, 'TOURNAMENT_SELECTION_NOT_FOUND'))).toBe(true);
    expect(notWrittenYet(httpError(404, 'STRATEGY_PLAN_NOT_FOUND'))).toBe(true);
    expect(notWrittenYet(httpError(404, 'PLAYER_PROFILE_NOT_FOUND'))).toBe(true);
  });

  it('returns false for 404 with an unknown code', () => {
    expect(notWrittenYet(httpError(404, 'TOURNAMENT_NOT_FOUND'))).toBe(false);
  });

  it('returns false for a non-404 status', () => {
    expect(notWrittenYet(httpError(500, 'DEBRIEF_NOT_FOUND'))).toBe(false);
  });

  it('returns false for non-HTTP errors', () => {
    expect(notWrittenYet(new Error('fail'))).toBe(false);
    expect(notWrittenYet(null)).toBe(false);
  });
});
