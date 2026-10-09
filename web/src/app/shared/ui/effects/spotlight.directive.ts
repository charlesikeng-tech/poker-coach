import { Directive, ElementRef, inject } from '@angular/core';

/**
 * Soft light following the pointer over a panel (styles: `.spotlight` in styles/_effects.scss).
 * Writes two CSS custom properties; no change detection involved.
 */
@Directive({
  selector: '[appSpotlight]',
  host: {
    class: 'spotlight',
    '(pointermove)': 'move($event)',
  },
})
export class Spotlight {
  private readonly element = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;

  protected move(event: PointerEvent): void {
    const bounds = this.element.getBoundingClientRect();
    this.element.style.setProperty('--spot-x', `${event.clientX - bounds.left}px`);
    this.element.style.setProperty('--spot-y', `${event.clientY - bounds.top}px`);
  }
}
