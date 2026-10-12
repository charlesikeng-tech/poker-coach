import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { roomName } from '../../../core/poker/rooms';

/**
 * A poker room's name in a small pill, in our colours. Deliberately not the room's logo: that is its trademark
 * and would suggest a partnership we do not have.
 */
@Component({
  selector: 'app-room-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="suit" aria-hidden="true">♠</span><span>{{ name() }}</span>`,
  host: { class: 'room-badge' },
  styles: `
    :host {
      display: inline-flex;
      gap: 0.3em;
      align-items: center;
      padding: 1px var(--space-2);
      border: 1px solid var(--border-default);
      border-radius: var(--radius-pill);
      background: var(--surface-secondary);
      color: var(--text-primary);
      font-size: var(--font-size-xs);
      font-weight: var(--font-weight-semibold);
      line-height: 1.6;
      letter-spacing: 0.02em;
      white-space: nowrap;
      vertical-align: middle;
    }

    .suit {
      color: var(--accent-primary);
      font-size: 0.9em;
    }
  `,
})
export class RoomBadge {
  /** The room as the API names it ("winamax"). */
  readonly room = input.required<string>();

  protected readonly name = computed(() => roomName(this.room()));
}
