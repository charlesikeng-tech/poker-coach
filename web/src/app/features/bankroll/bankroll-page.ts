import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CircleAlert, Settings2, Trash2, Wallet } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import {
  formatDateTime,
  formatInteger,
  formatMoney,
  formatSignedMoney,
  formatSignedPercent,
} from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { ChartMarker, LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import {
  BUY_IN_RULES,
  Bankroll,
  BankrollApi,
  BankrollMovement,
  BuyInRule,
  MOVEMENT_KINDS,
  MovementKind,
} from './bankroll-api';

type LoadState = 'loading' | 'ready' | 'error';
type Tone = 'good' | 'warn' | 'bad';

/** Movements drawn on the curve: beyond that, markers hide the line. */
const MAX_MARKERS = 12;

const RULE_BUY_INS: Record<BuyInRule, number> = {
  conservative: 200,
  standard: 100,
  aggressive: 50,
};

/** "YYYY-MM-DD" of a date in the user's time zone, for date inputs. */
function localDay(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * The bankroll (roadmap step 1): balance since a start date, the buy-in limit of the chosen rule, and how
 * risky the road ahead is given the player's own results. Replaces the empty Sessions page.
 */
@Component({
  selector: 'app-bankroll-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    NgTemplateOutlet,
    PageHeader,
    EmptyState,
    Button,
    LineChart,
    StatTile,
    Icon,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './bankroll-page.html',
  styleUrl: './bankroll-page.scss',
})
export class BankrollPage {
  private readonly api = inject(BankrollApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  protected readonly icons = { CircleAlert, Settings2, Trash2, Wallet };
  protected readonly rules = BUY_IN_RULES;
  protected readonly ruleBuyIns = RULE_BUY_INS;
  protected readonly kinds = MOVEMENT_KINDS;
  protected readonly today = localDay(new Date());

  protected readonly state = signal<LoadState>('loading');
  protected readonly bankroll = signal<Bankroll | null>(null);
  protected readonly editingSettings = signal(false);
  protected readonly saving = signal(false);
  /** Error code of the last failed write (VALIDATION_FAILED, BANKROLL_NOT_CONFIGURED…), or 'unknown'. */
  protected readonly writeError = signal<string | null>(null);

  // Settings form.
  protected readonly startingAmount = signal('');
  protected readonly startedOn = signal(localDay(new Date()));
  protected readonly rule = signal<BuyInRule>('conservative');

  // Movement form.
  protected readonly kind = signal<MovementKind>('deposit');
  protected readonly amount = signal('');
  protected readonly occurredOn = signal(localDay(new Date()));
  protected readonly note = signal('');

  private readonly locale = computed(() => this.language.current());

  protected readonly settingsValid = computed(() => {
    const amount = Number(this.startingAmount());
    return (
      this.startingAmount() !== '' && Number.isFinite(amount) && amount >= 0 && !!this.startedOn()
    );
  });

  protected readonly movementValid = computed(() => {
    const amount = Number(this.amount());
    if (this.amount() === '' || !Number.isFinite(amount) || !this.occurredOn()) {
      return false;
    }
    return this.kind() === 'adjustment' ? amount !== 0 : amount > 0;
  });

  protected readonly chartPoints = computed<LinePoint[]>(() => {
    const curve = this.bankroll()?.summary?.curve ?? [];
    const locale = this.locale();
    return curve.map((p, i) => ({
      x: i,
      y: p.balance,
      label:
        p.event === 'start'
          ? this.transloco.translate('pages.bankroll.chart.start')
          : (p.label ?? this.transloco.translate('pages.bankroll.chart.movement')),
      details: [
        formatDateTime(p.at, locale),
        ...(p.event === 'start' ? [] : [formatSignedMoney(p.change, 'EUR', locale)]),
        `${this.transloco.translate('pages.bankroll.chart.balance')} ${formatMoney(p.balance, 'EUR', locale)}`,
      ],
    }));
  });

  protected readonly chartMarkers = computed<ChartMarker[]>(() => {
    const curve = this.bankroll()?.summary?.curve ?? [];
    return curve
      .map((p, i) => ({ p, i }))
      .filter(({ p }) => p.event === 'movement')
      .slice(-MAX_MARKERS)
      .map(({ p, i }) => ({
        pointIndex: i,
        label: p.change >= 0 ? '+' : '−',
        tone: p.change >= 0 ? 'positive' : 'negative',
      }));
  });

  protected readonly formatAxis = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: 'EUR',
        maximumFractionDigits: 0,
      }).format(value);
  });

  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      money: (value: number | null) => formatMoney(value, 'EUR', locale),
      signedMoney: (value: number | null) => formatSignedMoney(value, 'EUR', locale),
    };
  });

  /** How the recent average buy-in compares with what the rule allows. */
  protected readonly limitTone = computed<Tone | null>(() => {
    const limit = this.bankroll()?.summary?.limit;
    if (!limit || limit.recentAverageBuyIn === null) {
      return null;
    }
    if (limit.maxAverageBuyIn === 0 || limit.recentAverageBuyIn > limit.maxAverageBuyIn) {
      return 'bad';
    }
    return limit.recentAverageBuyIn > limit.maxAverageBuyIn * 0.8 ? 'warn' : 'good';
  });

  /** Share of the limit used by the recent average, capped for the gauge. */
  protected readonly limitUse = computed(() => {
    const limit = this.bankroll()?.summary?.limit;
    if (!limit || limit.recentAverageBuyIn === null || limit.maxAverageBuyIn <= 0) {
      return 1;
    }
    return Math.min(limit.recentAverageBuyIn / limit.maxAverageBuyIn, 1);
  });

  protected readonly ruinTone = computed<Tone | null>(() => {
    const risk = this.bankroll()?.summary?.risk;
    if (!risk) {
      return null;
    }
    return risk.ruin < 0.01 ? 'good' : risk.ruin < 0.05 ? 'warn' : 'bad';
  });

  /** Positions (0–100 %) of the ROI interval on a scale that always shows zero. */
  protected readonly roiScale = computed(() => {
    const roi = this.bankroll()?.summary?.roi;
    if (!roi || roi.tournaments === 0) {
      return null;
    }
    const min = Math.min(roi.low, 0);
    const max = Math.max(roi.high, 0);
    const span = max - min || 1;
    const at = (v: number) => ((v - min) / span) * 100;
    return { low: at(roi.low), mean: at(roi.mean), high: at(roi.high), zero: at(0) };
  });

  constructor() {
    void this.load();
  }

  protected retry(): void {
    void this.load();
  }

  protected money(value: number | null): string {
    return formatMoney(value, 'EUR', this.locale());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, 'EUR', this.locale());
  }

  protected percent(value: number): string {
    return formatSignedPercent(value, this.locale());
  }

  protected chance(value: number): string {
    return new Intl.NumberFormat(this.locale(), {
      style: 'percent',
      maximumFractionDigits: 1,
    }).format(value);
  }

  protected integer(value: number): string {
    return formatInteger(value, this.locale());
  }

  protected date(iso: string): string {
    return new Intl.DateTimeFormat(this.locale(), { dateStyle: 'medium' }).format(new Date(iso));
  }

  protected signedAmount(movement: BankrollMovement): number {
    return movement.kind === 'withdrawal' ? -movement.amount : movement.amount;
  }

  protected sign(value: number): 'positive' | 'negative' | 'neutral' {
    return value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected editSettings(): void {
    const settings = this.bankroll()?.settings;
    if (settings) {
      this.startingAmount.set(String(settings.startingAmount));
      this.startedOn.set(localDay(new Date(settings.startedOn)));
      this.rule.set(settings.rule);
    }
    this.writeError.set(null);
    this.editingSettings.set(true);
  }

  protected cancelSettings(): void {
    this.editingSettings.set(false);
    this.writeError.set(null);
  }

  protected async saveSettings(): Promise<void> {
    if (!this.settingsValid() || this.saving()) {
      return;
    }
    await this.write(async () => {
      await this.api.saveSettings({
        startingAmount: Number(this.startingAmount()),
        // Local midnight: the day the player chose, in his time zone.
        startedOn: new Date(`${this.startedOn()}T00:00:00`).toISOString(),
        rule: this.rule(),
      });
      this.editingSettings.set(false);
    });
  }

  protected async addMovement(): Promise<void> {
    if (!this.movementValid() || this.saving()) {
      return;
    }
    await this.write(async () => {
      const day = this.occurredOn();
      // Today means now (after today's tournaments so far); another day, its middle.
      const occurredAt = day === localDay(new Date()) ? new Date() : new Date(`${day}T12:00:00`);
      await this.api.addMovement({
        kind: this.kind(),
        amount: Number(this.amount()),
        occurredAt: occurredAt.toISOString(),
        note: this.note().trim() || null,
      });
      this.amount.set('');
      this.note.set('');
    });
  }

  protected async deleteMovement(movement: BankrollMovement): Promise<void> {
    if (this.saving()) {
      return;
    }
    await this.write(() => this.api.deleteMovement(movement.id));
  }

  /** Runs a write, then reloads: balance, limit and risk all move with it. */
  private async write(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    this.writeError.set(null);
    try {
      await action();
      await this.load(true);
    } catch (error) {
      this.writeError.set(errorCode(error));
    } finally {
      this.saving.set(false);
    }
  }

  private async load(quiet = false): Promise<void> {
    if (!quiet) {
      this.state.set('loading');
    }
    try {
      this.bankroll.set(await this.api.get());
      this.state.set('ready');
    } catch {
      if (!quiet) {
        this.state.set('error');
      }
    }
  }
}

function errorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string') {
      return code;
    }
  }
  return 'unknown';
}
