import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  ElementRef,
  TemplateRef,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';

const DURATION_MS = 200;
const EASING = 'ease';

/**
 * Title (plus an optional status chip) for use inside `app-event-banner`.
 *
 * Collapsed, a long title is cut to one line with an ellipsis so it doesn't swallow the image,
 * and the chip sits beside it. Clicking, tapping or pressing Enter shows the whole title: the
 * text grows in height, the side chip shrinks away and a copy of the chip flows inline after
 * the last word. Both are animated together, so the text reflows continuously instead of
 * jumping when the layout changes.
 *
 * Classes are toggled by hand (not through bindings) because the animation needs the new
 * layout measured synchronously:
 * - `expanded` (host) follows the state at once and swaps the two chips.
 * - `open` (text) lets the text wrap. On the way back it stays until the shrink has finished,
 *   so the text doesn't snap to one line before the box has followed.
 *
 * The collapsed title is a one-line clamp rather than `nowrap`, so it is exactly the first
 * line of the expanded text and only the ellipsis appears when the animation ends.
 */
@Component({
  selector: 'app-banner-title',
  imports: [NgTemplateOutlet],
  template: `
    <div
      #text
      class="text"
      tabindex="0"
      [attr.aria-expanded]="expanded()"
      (click)="toggle()"
      (keydown.enter)="toggle()"
    >
      <ng-content />
      @if (chip(); as c) {
        <span class="inline-chip"><ng-container [ngTemplateOutlet]="c" /></span>
      }
    </div>
    @if (chip(); as c) {
      <div #side class="side"><ng-container [ngTemplateOutlet]="c" /></div>
    }
  `,
  styleUrl: './banner-title.scss',
})
export class BannerTitle {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly text = viewChild.required<ElementRef<HTMLElement>>('text');
  private readonly side = viewChild<ElementRef<HTMLElement>>('side');
  private animations: Animation[] = [];
  private first = true;

  /** Status chip (or similar), shown beside the title and, expanded, inline after it. */
  readonly chip = input<TemplateRef<unknown> | null>(null);

  protected readonly expanded = signal(false);

  constructor() {
    effect(() => {
      const expand = this.expanded();
      untracked(() => this.animateTo(expand));
    });
  }

  protected toggle(): void {
    const text = this.text().nativeElement;
    // A title that already fits has nothing more to show.
    const truncated = text.scrollHeight > text.clientHeight + 1;
    if (this.expanded() || truncated) {
      this.expanded.update((v) => !v);
    }
  }

  private animateTo(expand: boolean): void {
    if (this.first) {
      this.first = false;
      return;
    }

    const host = this.host;
    const text = this.text().nativeElement;
    const side = this.side()?.nativeElement;

    // Read before cancelling so an interrupted animation continues from where it is.
    const fromHeight = text.offsetHeight;
    const fromWidth = side?.offsetWidth ?? 0;
    const fromMargin = side ? parseFloat(getComputedStyle(side).marginLeft) : 0;
    this.animations.forEach((a) => a.cancel());

    host.classList.toggle('expanded', expand);
    if (expand) {
      text.classList.add('open');
    }

    // Measured in the end state: expanded text is taller, collapsed is one line.
    const toHeight = expand ? text.offsetHeight : parseFloat(getComputedStyle(text).lineHeight);
    const toWidth = expand ? 0 : (side?.offsetWidth ?? 0);
    const toMargin = side ? parseFloat(getComputedStyle(side).marginLeft) : 0;

    const options: KeyframeAnimationOptions = {
      duration: matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : DURATION_MS,
      easing: EASING,
      fill: 'forwards',
    };
    const textAnimation = text.animate(
      [{ height: `${fromHeight}px` }, { height: `${toHeight}px` }],
      options,
    );
    this.animations = [textAnimation];
    if (side) {
      this.animations.push(
        side.animate(
          [
            { width: `${fromWidth}px`, marginLeft: `${fromMargin}px` },
            { width: `${toWidth}px`, marginLeft: `${toMargin}px` },
          ],
          options,
        ),
      );
    }

    textAnimation.onfinish = () => {
      if (!expand) {
        text.classList.remove('open');
      }
      this.animations.forEach((a) => a.cancel());
    };
  }
}
