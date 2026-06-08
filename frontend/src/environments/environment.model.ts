import { InjectionToken, Provider } from '@angular/core';

export interface Environment {
  apiAddress: string;
  useFakeAuth: boolean;
  fakeAuthToken?: string;
  fakeAuthExternalId?: string;
}

export const ENVIRONMENT = new InjectionToken<Environment>('appEnvironment');

export function provideEnvironment(environment: Environment): Provider {
  return {
    provide: ENVIRONMENT,
    useValue: environment,
  };
}
