import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Signal, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

/** True on phone-sized screens (under 600px). Call from an injection context. */
export function isCompactScreen(): Signal<boolean> {
  return toSignal(
    inject(BreakpointObserver)
      .observe(Breakpoints.XSmall)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );
}
