import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ArrowLeft, BrainCircuit, NotebookPen, Save, Swords, Tag, X } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import {
  ExploitResponse,
  OpponentCoachingResponse,
  OpponentDetail,
  OpponentsApi,
  PlayerType,
} from './opponents-api';

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-opponent-detail',
  imports: [TranslocoDirective, RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './opponent-detail.html',
  styleUrl: './opponent-detail.scss',
})
export class OpponentDetailPage {
  private readonly api = inject(OpponentsApi);
  private readonly language = inject(LanguageService);

  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<OpponentDetail | null>(null);
  protected readonly editingNote = signal(false);
  protected readonly noteText = signal('');
  protected readonly editingTags = signal(false);
  protected readonly tagsText = signal('');
  protected readonly saving = signal(false);
  protected readonly exploit = signal<ExploitResponse | null>(null);
  protected readonly coaching = signal<OpponentCoachingResponse | null>(null);
  protected readonly coachingState = signal<'idle' | 'loading' | 'error'>('idle');
  protected readonly icons = { ArrowLeft, BrainCircuit, NotebookPen, Save, Swords, Tag, X };

  private readonly locale = computed(() => this.language.current());

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  protected percent(value: number | null): string {
    if (value === null) return '—';
    return (value * 100).toFixed(1) + ' %';
  }

  protected date(value: string): string {
    return formatDateTime(value, this.locale());
  }

  protected typeClass(type: PlayerType): string {
    return `type--${type}`;
  }

  protected startEditNote(): void {
    this.noteText.set(this.data()?.note ?? '');
    this.editingNote.set(true);
  }

  protected cancelEditNote(): void {
    this.editingNote.set(false);
  }

  protected async saveNote(): Promise<void> {
    const d = this.data();
    if (!d) return;
    this.saving.set(true);
    try {
      const text = this.noteText().trim() || null;
      if (text) {
        await this.api.setNote(d.id, text);
      } else {
        await this.api.clearNote(d.id);
      }
      this.data.set({ ...d, note: text });
      this.editingNote.set(false);
    } finally {
      this.saving.set(false);
    }
  }

  protected startEditTags(): void {
    this.tagsText.set((this.data()?.tags ?? []).join(', '));
    this.editingTags.set(true);
  }

  protected cancelEditTags(): void {
    this.editingTags.set(false);
  }

  protected async saveTags(): Promise<void> {
    const d = this.data();
    if (!d) return;
    this.saving.set(true);
    try {
      const tags = this.tagsText()
        .split(',')
        .map((t) => t.trim())
        .filter((t) => t.length > 0);
      await this.api.setTags(d.id, tags);
      this.data.set({ ...d, tags });
      this.editingTags.set(false);
    } finally {
      this.saving.set(false);
    }
  }

  protected async requestCoaching(): Promise<void> {
    const d = this.data();
    if (!d) return;
    this.coachingState.set('loading');
    try {
      const result = await this.api.generateCoaching(d.id);
      this.coaching.set(result);
      this.coachingState.set('idle');
    } catch {
      this.coachingState.set('error');
    }
  }

  protected retry(): void {
    void this.load(this.id());
  }

  private async load(id: string): Promise<void> {
    this.state.set('loading');
    this.exploit.set(null);
    this.coaching.set(null);
    this.coachingState.set('idle');
    try {
      const data = await this.api.get(id);
      this.data.set(data);
      this.state.set('ready');
      // Load exploit tips and cached coaching in parallel.
      void this.loadExploit(id);
      void this.loadCoaching(id);
    } catch {
      this.state.set('error');
    }
  }

  private async loadExploit(id: string): Promise<void> {
    try {
      this.exploit.set(await this.api.getExploit(id));
    } catch {
      // Exploit tips are optional: no error state needed.
    }
  }

  private async loadCoaching(id: string): Promise<void> {
    try {
      this.coaching.set(await this.api.getCoaching(id));
    } catch {
      // 404 = no cached coaching: expected, the user must generate it.
    }
  }
}
