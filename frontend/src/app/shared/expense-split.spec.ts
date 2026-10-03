import { previewSplit } from './expense-split';

const people = (...ids: number[]) => ids.map((userId) => ({ userId, value: null }));

describe('previewSplit', () => {
  it('splits equally, giving leftover cents to the lowest ids', () => {
    const preview = previewSplit(10, 'Equal', people(3, 1, 2));
    expect(preview.error).toBeNull();
    expect(preview.shares.get(1)).toBe(3.34);
    expect(preview.shares.get(2)).toBe(3.33);
    expect(preview.shares.get(3)).toBe(3.33);
  });

  it('gives leftover cents to the biggest rounding loss for percentages', () => {
    const preview = previewSplit(0.1, 'Percentage', [
      { userId: 1, value: 33.33 },
      { userId: 2, value: 66.67 },
    ]);
    expect(preview.shares.get(1)).toBe(0.03);
    expect(preview.shares.get(2)).toBe(0.07);
  });

  it('reports what is left to assign', () => {
    const percent = previewSplit(50, 'Percentage', [
      { userId: 1, value: 60 },
      { userId: 2, value: 30 },
    ]);
    expect(percent.error).not.toBeNull();
    expect(percent.remaining).toBe(10);

    const exact = previewSplit(25, 'Exact', [
      { userId: 1, value: 20 },
      { userId: 2, value: 4.5 },
    ]);
    expect(exact.error).not.toBeNull();
    expect(exact.remaining).toBe(0.5);
  });

  it('accepts exact amounts that add up', () => {
    const preview = previewSplit(25, 'Exact', [
      { userId: 1, value: 20 },
      { userId: 2, value: 5 },
    ]);
    expect(preview.error).toBeNull();
    expect(preview.shares.get(2)).toBe(5);
  });

  it('needs an amount and participants', () => {
    expect(previewSplit(null, 'Equal', people(1)).error).not.toBeNull();
    expect(previewSplit(10, 'Equal', []).error).not.toBeNull();
  });
});
