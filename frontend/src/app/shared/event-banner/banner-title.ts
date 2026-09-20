import { Component, ElementRef, effect, inject, signal, untracked } from '@angular/core';

const DURATION_MS = 200;

/**
 * Title line for use inside `app-event-banner`. Long titles are cut to one line with an
 * ellipsis so they don't swallow the image. Clicking or tapping (or pressing Enter) toggles
 * the whole title, animating the height between the two.
 *
 * The `open` class is toggled by hand rather than through a binding because the animation
 * needs the new layout measured synchronously. On the way back it stays until the shrink has
 * finished, so the text doesn't snap to one line before the box has followed.
 */
@Component({
  selector: 'app-banner-title',
  template: '<ng-content />',
  styleUrl: './banner-title.scss',
  host: {
    tabindex: '0',
    '[attr.aria-expanded]': 'expanded()',
    '(click)': 'expanded.update((v) => !v)',
    '(keydown.enter)': 'expanded.update((v) => !v)',
  },
})
export class BannerTitle {
  private readonly el = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private animation?: Animation;
  private first = true;

  protected readonly expanded = signal(false);

  constructor() {
    effect(() => {
      const expand = this.expanded();
      untracked(() => this.animateTo(expand));
    });
  }

  private animateTo(expand: boolean): void {
    const el = this.el;
    if (this.first) {
      this.first = false;
      return;
    }

    // Read before cancelling so an interrupted animation continues from where it is.
    const from = el.offsetHeight;
    this.animation?.cancel();

    let to: number;
    if (expand) {
      el.classList.add('open');
      to = el.offsetHeight;
    } else {
      to = parseFloat(getComputedStyle(el).lineHeight);
    }

    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const animation = el.animate([{ height: `${from}px` }, { height: `${to}px` }], {
      duration: reduced ? 0 : DURATION_MS,
      easing: 'ease',
      fill: 'forwards',
    });
    this.animation = animation;
    animation.onfinish = () => {
      if (!expand) {
        el.classList.remove('open');
      }
      animation.cancel();
    };
  }
}
