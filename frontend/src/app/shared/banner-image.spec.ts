import { describe, expect, it } from 'vitest';

import { fitWithin } from './banner-image';

describe('fitWithin', () => {
  it('leaves small images alone', () => {
    expect(fitWithin(800, 400, 1600)).toEqual([800, 400]);
  });

  it('scales landscape images by width', () => {
    expect(fitWithin(4000, 3000, 1600)).toEqual([1600, 1200]);
  });

  it('scales portrait images by height', () => {
    expect(fitWithin(3000, 4000, 1600)).toEqual([1200, 1600]);
  });

  it('never returns a zero edge', () => {
    expect(fitWithin(100000, 10, 1600)).toEqual([1600, 1]);
  });
});
