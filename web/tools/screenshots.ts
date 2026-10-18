/**
 * Generates documentation screenshots for every feature page.
 * Requires the Angular dev server to be running: npm start
 * Run with: npm run screenshots
 *
 * Output: docs/screenshots/<page>.png  (relative to the project root)
 */
import { chromium, type Page, type Route } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------

const BASE_URL = 'http://localhost:4200';
// Resolve from the web/ working directory (where `npm run screenshots` runs)
const OUT_DIR = resolve(process.cwd(), '../docs/screenshots');
const VIEWPORT = { width: 1440, height: 900 };

// ---------------------------------------------------------------------------
// Mock data
// ---------------------------------------------------------------------------

const ME = {
  id: 'doc-user',
  displayName: 'Alex Dupont',
  email: 'alex.dupont@example.com',
  preferredLanguage: 'en',
};

const statRate = (made: number, opportunities: number) => ({
  made,
  opportunities,
  rate: opportunities > 0 ? Math.round((made / opportunities) * 1000) / 1000 : null,
});

const STAT_LINE = {
  hands: 3124,
  vpip: statRate(938, 3124),
  pfr: statRate(656, 3124),
  rfi: statRate(342, 1280),
  limp: statRate(38, 1280),
  steal: statRate(184, 630),
  threeBet: statRate(97, 1950),
  foldToThreeBet: statRate(158, 290),
  cbetFlop: statRate(312, 445),
  wentToShowdown: statRate(216, 938),
  wonAtShowdown: statRate(122, 216),
  bigBlindsPer100: 3.8,
  foldToCbetFlop: statRate(154, 310),
  raiseCbetFlop: statRate(20, 310),
  cbetTurn: statRate(136, 218),
  checkRaiseFlop: statRate(24, 445),
  wonWhenSawFlop: statRate(214, 445),
  postflopAggression: statRate(198, 445),
};

const PERF_FIGURES = {
  tournaments: 127,
  tournamentsWithResult: 118,
  entries: 134,
  paidEntries: 127,
  buyIns: 1245,
  winnings: 1401,
  profit: 156,
  roi: 0.125,
  roiLow: 0.042,
  roiHigh: 0.208,
  itm: 0.22,
  itmlLow: 0.15,
  itmHigh: 0.29,
};

const CURVE: unknown[] = Array.from({ length: 127 }, (_, i) => ({
  index: i + 1,
  startedAt: new Date(Date.now() - (127 - i) * 86_400_000 * 0.8).toISOString(),
  name: `Tournament ${i + 1}`,
  profit: (Math.random() - 0.42) * 30,
  cumulativeProfit: i * 1.2 + (Math.random() - 0.4) * 15,
}));

const PERFORMANCE_REPORT = {
  totals: PERF_FIGURES,
  curve: CURVE,
  byBuyIn: [
    { key: 'upTo5', figures: { ...PERF_FIGURES, tournaments: 34, profit: 22 } },
    { key: 'from5To10', figures: { ...PERF_FIGURES, tournaments: 58, profit: 89 } },
    { key: 'from10To20', figures: { ...PERF_FIGURES, tournaments: 35, profit: 45 } },
  ],
  byType: [
    { key: 'mtt', figures: { ...PERF_FIGURES, tournaments: 98, profit: 142 } },
    { key: 'sng', figures: { ...PERF_FIGURES, tournaments: 29, profit: 14 } },
  ],
  bySpeed: [
    { key: 'normal', figures: { ...PERF_FIGURES, tournaments: 72, profit: 98 } },
    { key: 'turbo', figures: { ...PERF_FIGURES, tournaments: 55, profit: 58 } },
  ],
  byMonth: [
    { key: '2026-08-01', figures: { ...PERF_FIGURES, tournaments: 38, profit: 42 } },
    { key: '2026-09-01', figures: { ...PERF_FIGURES, tournaments: 45, profit: 67 } },
    { key: '2026-10-01', figures: { ...PERF_FIGURES, tournaments: 44, profit: 47 } },
  ],
  byDayOfWeek: [
    { key: 'friday', figures: { ...PERF_FIGURES, tournaments: 22, profit: 31 } },
    { key: 'saturday', figures: { ...PERF_FIGURES, tournaments: 48, profit: 72 } },
    { key: 'sunday', figures: { ...PERF_FIGURES, tournaments: 57, profit: 53 } },
  ],
  byTimeSlot: [],
  rooms: ['winamax', 'pokerstars'],
};

const CASH_PERFORMANCE_REPORT = {
  totals: {
    hands: 4280,
    netBigBlinds: 185.2,
    bigBlindsPer100: 4.33,
    netMoney: 92.6,
    allInAdjBigBlindsPer100: 5.1,
    bigBlindsPer100Low: 1.8,
    bigBlindsPer100High: 6.9,
  },
  curve: Array.from({ length: 12 }, (_, i) => ({
    date: new Date(Date.now() - (12 - i) * 7 * 86_400_000).toISOString().slice(0, 10),
    hands: 350,
    netBigBlinds: (Math.random() - 0.38) * 40,
    cumulativeNetBigBlinds: i * 15 + (Math.random() - 0.4) * 10,
    netMoney: (Math.random() - 0.38) * 20,
    cumulativeNetMoney: i * 7.5 + (Math.random() - 0.4) * 5,
  })),
  byStake: [],
  byMonth: [],
  byDayOfWeek: [],
  rooms: ['winamax'],
};

const TOURNAMENTS_PAGE = {
  items: [
    {
      id: 't1',
      name: 'Sunday Major NLH €10 GTD 10k',
      startedAt: '2026-10-12T14:00:00Z',
      currency: 'EUR',
      buyIn: 10,
      registeredPlayers: 842,
      finishPosition: 45,
      handCount: 287,
      result: { status: 'known', entries: 1, totalBuyIn: 10, prizeWinnings: 18, bountyWinnings: 0, profit: 8 },
      coverage: { status: 'full', handCount: 287, firstLevel: 1, lastLevel: 18, missingHands: 0 },
    },
    {
      id: 't2',
      name: 'NLH Turbo €5 Bounty',
      startedAt: '2026-10-11T20:00:00Z',
      currency: 'EUR',
      buyIn: 5,
      registeredPlayers: 312,
      finishPosition: 12,
      handCount: 142,
      result: { status: 'known', entries: 1, totalBuyIn: 5, prizeWinnings: 0, bountyWinnings: 8, profit: 3 },
      coverage: { status: 'partial', handCount: 142, firstLevel: 3, lastLevel: 12, missingHands: 24 },
    },
    {
      id: 't3',
      name: 'Monday NLH €7 Reentry',
      startedAt: '2026-10-07T20:00:00Z',
      currency: 'EUR',
      buyIn: 7,
      registeredPlayers: 560,
      finishPosition: 180,
      handCount: 98,
      result: { status: 'known', entries: 2, totalBuyIn: 14, prizeWinnings: 0, bountyWinnings: 0, profit: -14 },
      coverage: { status: 'full', handCount: 98, firstLevel: 1, lastLevel: 8, missingHands: 0 },
    },
    {
      id: 't4',
      name: 'NLH SNG €10 6-Max',
      startedAt: '2026-10-06T19:30:00Z',
      currency: 'EUR',
      buyIn: 10,
      registeredPlayers: 6,
      finishPosition: 2,
      handCount: 54,
      result: { status: 'known', entries: 1, totalBuyIn: 10, prizeWinnings: 30, bountyWinnings: 0, profit: 20 },
      coverage: { status: 'full', handCount: 54, firstLevel: 1, lastLevel: 6, missingHands: 0 },
    },
    {
      id: 't5',
      name: 'Thursday NLH €5',
      startedAt: '2026-10-03T19:00:00Z',
      currency: 'EUR',
      buyIn: 5,
      registeredPlayers: 228,
      finishPosition: 68,
      handCount: 112,
      result: { status: 'known', entries: 1, totalBuyIn: 5, prizeWinnings: 0, bountyWinnings: 0, profit: -5 },
      coverage: { status: 'full', handCount: 112, firstLevel: 1, lastLevel: 10, missingHands: 0 },
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 127,
  totals: PERF_FIGURES,
};

const BANKROLL = {
  settings: {
    startingAmount: 200,
    startedOn: '2026-01-01',
    rule: 'standard',
    buyIns: 20,
  },
  summary: {
    balance: 356,
    tournamentProfit: 156,
    cashProfit: 92.6,
    netDeposits: -92.6,
    tournaments: 127,
    unknownResults: 9,
    limit: {
      maxAverageBuyIn: 17.8,
      recentAverageBuyIn: 8.2,
      aboveLimitRecently: 0,
      playedRecently: 44,
    },
    curve: Array.from({ length: 10 }, (_, i) => ({
      at: new Date(Date.now() - (10 - i) * 10 * 86_400_000).toISOString(),
      balance: 200 + i * 16 + (Math.random() - 0.3) * 20,
      change: (Math.random() - 0.3) * 20,
      event: i % 3 === 0 ? 'movement' : 'tournament',
      label: i % 3 === 0 ? 'Deposit' : null,
    })),
    movements: [
      { id: 'm1', kind: 'deposit', amount: 200, occurredAt: '2026-01-01T00:00:00Z', note: 'Initial deposit' },
      { id: 'm2', kind: 'withdrawal', amount: 100, occurredAt: '2026-06-15T00:00:00Z', note: null },
    ],
    roi: { tournaments: 118, mean: 0.125, low: 0.042, high: 0.208 },
  },
};

const STATISTICS_REPORT = {
  overall: STAT_LINE,
  byPosition: [
    { position: 'button', line: { ...STAT_LINE, hands: 498 } },
    { position: 'cutoff', line: { ...STAT_LINE, hands: 372 } },
    { position: 'bigBlind', line: { ...STAT_LINE, hands: 468 } },
    { position: 'smallBlind', line: { ...STAT_LINE, hands: 436 } },
    { position: 'hijack', line: { ...STAT_LINE, hands: 340 } },
    { position: 'utg', line: { ...STAT_LINE, hands: 560 } },
  ],
  pendingHands: 0,
  sample: { tournaments: 127, completeTournaments: 118 },
  format: 'sixMax',
  handsByFormat: { sixMax: 2840, fullRing: 284 },
  rooms: ['winamax'],
};

const LEAK_ANALYSIS = {
  leaks: [
    {
      stat: 'steal',
      position: 'button',
      direction: 'tooLow',
      confidence: 'confirmed',
      rate: 0.29,
      made: 184,
      opportunities: 630,
      intervalLow: 0.25,
      intervalHigh: 0.33,
      range: { min: 0.35, max: 0.55 },
    },
    {
      stat: 'threeBet',
      position: null,
      direction: 'tooLow',
      confidence: 'possible',
      rate: 0.05,
      made: 97,
      opportunities: 1950,
      intervalLow: 0.04,
      intervalHigh: 0.06,
      range: { min: 0.06, max: 0.10 },
    },
    {
      stat: 'foldToThreeBet',
      position: null,
      direction: 'tooHigh',
      confidence: 'confirmed',
      rate: 0.545,
      made: 158,
      opportunities: 290,
      intervalLow: 0.49,
      intervalHigh: 0.60,
      range: { min: 0.40, max: 0.52 },
    },
  ],
  underSampled: [],
  hands: 3124,
  tournaments: 127,
  completeTournaments: 118,
  pendingHands: 0,
  referenceVersion: 4,
  format: 'sixMax',
  handsByFormat: { sixMax: 2840, fullRing: 284 },
  lockedLeaks: 0,
};

const PROGRESS_REPORT = {
  weekStart: '2026-10-13',
  current: {
    weekStart: '2026-10-13',
    gameType: 'tournament',
    subFormat: null,
    format: 'sixMax',
    referenceVersion: 4,
    createdAt: new Date().toISOString(),
    priorities: [
      {
        stat: 'steal',
        position: 'button',
        direction: 'tooLow',
        confidence: 'confirmed',
        baselineRate: 0.29,
        baselineOpportunities: 630,
        min: 0.35,
        max: 0.55,
        weekMade: 42,
        weekOpportunities: 134,
        weekRate: 0.313,
        status: 'improving',
        drill: null,
      },
      {
        stat: 'threeBet',
        position: null,
        direction: 'tooLow',
        confidence: 'possible',
        baselineRate: 0.05,
        baselineOpportunities: 1950,
        min: 0.06,
        max: 0.10,
        weekMade: 8,
        weekOpportunities: 112,
        weekRate: 0.071,
        status: 'onTrack',
        drill: null,
      },
    ],
    hands: 246,
  },
  baselineHands: 3124,
  minBaselineHands: 500,
  minWeekSpots: 20,
  drillAttempts: 12,
  drillCorrect: 9,
  drillGoal: 15,
  history: [],
};

const BILLING_SUMMARY = {
  billingEnabled: true,
  plan: 'pro',
  source: 'subscription',
  grantEndsAt: null,
  status: 'active',
  interval: 'monthly',
  currentPeriodEnd: '2026-11-18T00:00:00Z',
  cancelAtPeriodEnd: false,
  canManage: true,
  tier: 'pro',
  monthlyCredits: 30,
  creditsUsedThisMonth: 8,
  topUpCredits: 0,
};

const CASH_SESSIONS_PAGE = {
  items: [
    {
      id: 'cs1',
      startedAt: '2026-10-15T19:00:00Z',
      endedAt: '2026-10-15T23:30:00Z',
      stake: '0.05/0.10',
      room: 'winamax',
      handCount: 312,
      netBigBlinds: 42.5,
      netMoney: 4.25,
    },
    {
      id: 'cs2',
      startedAt: '2026-10-14T20:00:00Z',
      endedAt: '2026-10-14T22:45:00Z',
      stake: '0.05/0.10',
      room: 'winamax',
      handCount: 198,
      netBigBlinds: -18.2,
      netMoney: -1.82,
    },
    {
      id: 'cs3',
      startedAt: '2026-10-12T18:30:00Z',
      endedAt: '2026-10-12T21:00:00Z',
      stake: '0.05/0.10',
      room: 'winamax',
      handCount: 245,
      netBigBlinds: 28.0,
      netMoney: 2.80,
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 48,
};

// ---------------------------------------------------------------------------
// Route mock registry
// ---------------------------------------------------------------------------

type MockEntry = { pattern: RegExp | string; body: unknown; status?: number };

const API_MOCKS: MockEntry[] = [
  { pattern: /\/api\/me$/, body: ME },
  { pattern: /\/api\/performance\/cash/, body: CASH_PERFORMANCE_REPORT },
  { pattern: /\/api\/performance/, body: PERFORMANCE_REPORT },
  { pattern: /\/api\/tournaments/, body: TOURNAMENTS_PAGE },
  { pattern: /\/api\/bankroll$/, body: BANKROLL },
  { pattern: /\/api\/statistics\/breakdowns/, body: { format: 'sixMax', byPhase: [], byMonth: [] } },
  { pattern: /\/api\/statistics\/all-in/, body: { hands: 342, equity: [], swing: [] } },
  { pattern: /\/api\/statistics/, body: STATISTICS_REPORT },
  { pattern: /\/api\/leaks/, body: LEAK_ANALYSIS },
  { pattern: /\/api\/progress/, body: PROGRESS_REPORT },
  { pattern: /\/api\/billing/, body: BILLING_SUMMARY },
  { pattern: /\/api\/cash-sessions/, body: CASH_SESSIONS_PAGE },
  { pattern: /\/api\/import\/accounts/, body: [] },
  { pattern: /\/api\/training/, body: null, status: 404 },
  { pattern: /\/api\/ranges/, body: null, status: 404 },
];

async function installMocks(page: Page): Promise<void> {
  // Catch-all: return 200 [] for unmatched API calls
  await page.route('**/api/**', (route: Route) =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }),
  );
  // Specific mocks — registered last → highest priority (Playwright LIFO)
  for (const { pattern, body, status = 200 } of API_MOCKS) {
    await page.route(pattern, (route: Route) =>
      route.fulfill({
        status,
        contentType: 'application/json',
        body: body === null ? '' : JSON.stringify(body),
      }),
    );
  }
}

// ---------------------------------------------------------------------------
// Pages to capture
// ---------------------------------------------------------------------------

interface PageEntry {
  readonly path: string;
  readonly slug: string;
  readonly waitFor?: string;
}

const PAGES: PageEntry[] = [
  // Public
  { path: '/sign-in', slug: 'sign-in', waitFor: 'h1' },
  { path: '/forgot-password', slug: 'forgot-password', waitFor: 'form' },
  { path: '/privacy', slug: 'privacy', waitFor: 'h1' },
  // Authenticated — shell pages
  { path: '/dashboard', slug: 'dashboard' },
  { path: '/tournaments', slug: 'tournaments' },
  { path: '/cash-sessions', slug: 'cash-sessions' },
  { path: '/performance', slug: 'performance' },
  { path: '/bankroll', slug: 'bankroll' },
  { path: '/statistics', slug: 'statistics' },
  { path: '/leaks', slug: 'leaks' },
  { path: '/ranges', slug: 'ranges' },
  { path: '/progress', slug: 'progress' },
  { path: '/training', slug: 'training' },
  { path: '/import', slug: 'import' },
  { path: '/subscription', slug: 'subscription' },
  { path: '/account', slug: 'account' },
];

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

void (async () => {
  mkdirSync(OUT_DIR, { recursive: true });

  const browser = await chromium.launch();
  const context = await browser.newContext({
    viewport: VIEWPORT,
    colorScheme: 'light',
    reducedMotion: 'reduce',
    locale: 'en-US',
  });
  const page = await context.newPage();

  await installMocks(page);

  let ok = 0;
  let failed = 0;

  for (const { path, slug, waitFor } of PAGES) {
    try {
      await page.goto(`${BASE_URL}${path}`, { waitUntil: 'domcontentloaded' });
      await page.waitForLoadState('networkidle');

      if (waitFor) {
        await page.waitForSelector(waitFor, { timeout: 5_000 });
      }

      const outPath = join(OUT_DIR, `${slug}.png`);
      await page.screenshot({ path: outPath, fullPage: false });
      console.log(`✓  ${slug}`);
      ok++;
    } catch (err) {
      console.error(`✗  ${slug}: ${(err as Error).message}`);
      failed++;
    }
  }

  await browser.close();
  console.log(`\n${ok} captured, ${failed} failed → ${OUT_DIR}`);
})();
