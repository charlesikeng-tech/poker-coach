import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Renderer2,
  effect,
  inject,
  input,
} from '@angular/core';

/** Structural copy of lucide's IconNode, so feature code does not depend on lucide's type exports. */
export type IconNode = readonly (readonly [
  tag: string,
  attrs: Record<string, string | number | undefined>,
])[];

const SVG_NAMESPACE = 'svg';

/**
 * Renders a lucide icon node as inline SVG. Decorative by design (aria-hidden): the accessible name
 * always comes from the surrounding control or text.
 */
@Component({
  selector: 'app-icon',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      width: var(--icon-size, 1.125rem);
      height: var(--icon-size, 1.125rem);
    }
  `,
})
export class Icon {
  readonly icon = input.required<IconNode>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);

  constructor() {
    effect(() => this.render(this.icon()));
  }

  private render(node: IconNode): void {
    const host = this.host.nativeElement;
    for (const child of Array.from(host.childNodes)) {
      this.renderer.removeChild(host, child);
    }

    const svg = this.renderer.createElement('svg', SVG_NAMESPACE) as SVGElement;
    const svgAttributes: Record<string, string> = {
      viewBox: '0 0 24 24',
      width: '100%',
      height: '100%',
      fill: 'none',
      stroke: 'currentColor',
      'stroke-width': '1.75',
      'stroke-linecap': 'round',
      'stroke-linejoin': 'round',
      focusable: 'false',
    };
    for (const [name, value] of Object.entries(svgAttributes)) {
      this.renderer.setAttribute(svg, name, value);
    }

    for (const [tag, attributes] of node) {
      const element = this.renderer.createElement(tag, SVG_NAMESPACE) as SVGElement;
      for (const [name, value] of Object.entries(attributes)) {
        if (value !== undefined) {
          this.renderer.setAttribute(element, name, String(value));
        }
      }
      this.renderer.appendChild(svg, element);
    }

    this.renderer.appendChild(host, svg);
  }
}
