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
import { ArrowLeft, NotebookPen, Save, Tag, X } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { OpponentDetail, OpponentsApi, PlayerType } from './opponents-api';

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
  protected readonly icons = { ArrowLeft, NotebookPen, Save, Tag, X };

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

  protected retry(): void {
    void this.load(this.id());
  }

  private async load(id: string): Promise<void> {
    this.state.set('loading');
    try {
      const data = await this.api.get(id);
      this.data.set(data);
      this.state.set('ready');
    } catch {
      this.state.set('error');
    }
  }
}
