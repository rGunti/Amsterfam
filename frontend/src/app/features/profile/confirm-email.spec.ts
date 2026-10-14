import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { UserApi } from '../../core/api/user.api';
import { ConfirmEmail } from './confirm-email';

describe('ConfirmEmail', () => {
  async function render(token: string | null, confirm: () => Observable<unknown>) {
    const api = { confirmNotificationEmail: vi.fn(confirm) };
    const snackBar = { open: vi.fn() };
    TestBed.configureTestingModule({
      imports: [ConfirmEmail],
      providers: [
        provideRouter([]),
        { provide: UserApi, useValue: api },
        { provide: MatSnackBar, useValue: snackBar },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(token ? { token } : {}) } },
        },
      ],
    });
    const router = TestBed.inject(Router);
    const navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.detectChanges();
    // ngOnInit awaits a few promises in a row; let them all settle.
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    return {
      api,
      snackBar,
      navigateByUrl,
      text: () => fixture.nativeElement.textContent as string,
    };
  }

  it('confirms the token and goes back to the profile', async () => {
    const { api, snackBar, navigateByUrl } = await render('abc', () =>
      of({ effectiveEmail: 'new@example.com' }),
    );

    expect(api.confirmNotificationEmail).toHaveBeenCalledWith('abc');
    expect(snackBar.open).toHaveBeenCalledWith(
      'News will now be emailed to new@example.com',
      'Dismiss',
      { duration: 5000 },
    );
    expect(navigateByUrl).toHaveBeenCalledWith('/profile', { replaceUrl: true });
  });

  it('says the link is invalid when the server rejects it', async () => {
    const { text, navigateByUrl } = await render('abc', () =>
      throwError(() => new HttpErrorResponse({ status: 400 })),
    );

    expect(text()).toContain('Link invalid or expired');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('says the link is invalid without a token', async () => {
    const { api, text } = await render(null, () => of({}));

    expect(api.confirmNotificationEmail).not.toHaveBeenCalled();
    expect(text()).toContain('Link invalid or expired');
  });
});
