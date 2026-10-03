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

  it('leaves what is not covered unassigned instead of failing', () => {
    const percent = previewSplit(50, 'Percentage', [
      { userId: 1, value: 60 },
      { userId: 2, value: 30 },
    ]);
    expect(percent.error).toBeNull();
    expect(percent.shares.get(1)).toBe(30);
    expect(percent.unassigned).toBe(5);

    const exact = previewSplit(25, 'Exact', [
      { userId: 1, value: 20 },
      { userId: 2, value: 4.5 },
    ]);
    expect(exact.error).toBeNull();
    expect(exact.unassigned).toBe(0.5);

    const nobody = previewSplit(25, 'Exact', []);
    expect(nobody.error).toBeNull();
    expect(nobody.unassigned).toBe(25);
  });

  it('rejects splits that cover more than the total', () => {
    expect(
      previewSplit(10, 'Percentage', [
        { userId: 1, value: 60 },
        { userId: 2, value: 50 },
      ]).error,
    ).not.toBeNull();
    expect(previewSplit(10, 'Exact', [{ userId: 1, value: 10.01 }]).error).not.toBeNull();
  });

  it('accepts exact amounts that add up', () => {
    const preview = previewSplit(25, 'Exact', [
      { userId: 1, value: 20 },
      { userId: 2, value: 5 },
    ]);
    expect(preview.error).toBeNull();
    expect(preview.shares.get(2)).toBe(5);
    expect(preview.unassigned).toBe(0);
  });

  it('rejects fractions of a cent, like the server', () => {
    expect(previewSplit(10.005, 'Equal', [{ userId: 1, value: null }]).error).not.toBeNull();
    expect(previewSplit(20, 'Exact', [{ userId: 1, value: 10.005 }]).error).not.toBeNull();
    expect(previewSplit(20, 'Percentage', [{ userId: 1, value: 33.333 }]).error).not.toBeNull();
    expect(previewSplit(0.3, 'Exact', [{ userId: 1, value: 0.1 + 0.2 }]).error).toBeNull();
  });

  it('needs an amount and participants', () => {
    expect(previewSplit(null, 'Equal', people(1)).error).not.toBeNull();
    expect(previewSplit(10, 'Equal', []).error).not.toBeNull();
  });
});
