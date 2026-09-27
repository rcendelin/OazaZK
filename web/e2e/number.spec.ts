import { expect, test } from '@playwright/test';
import { invalidNumberMessage, parseCzechNumber } from '../src/utils/number';

// Unit check of the Czech number parser (#8) — runs in the Playwright runner without a browser page
// (the repo has no separate JS unit-test runner).

test('parseCzechNumber: Czech formats incl. typographic minus and non-breaking spaces', () => {
  const cases: [string, number][] = [
    ['0', 0],
    ['1542,7', 1542.7],
    ['1 542,7', 1542.7],
    ['2\u00a0500,50', 2500.5], // NBSP thousands separator (cs-CZ Intl output)
    ['2\u202f500', 2500], // narrow NBSP
    ['−20 000', -20000], // U+2212 as in the help text
    ['\u221220\u00a0000', -20000],
    ['–5', -5], // en dash
    ['-5', -5],
    ['+7', 7],
    ['  12,5  ', 12.5],
    ['12.5', 12.5], // dot is accepted too (prefilled values use String(number))
    [',5', 0.5],
    ['260,855', 260.855],
  ];
  for (const [input, expected] of cases) {
    expect(parseCzechNumber(input), input).toBe(expected);
  }
});

test('parseCzechNumber: garbage and empty input are null, never 0', () => {
  for (const input of ['', '   ', 'abc', '12abc', 'abc12', '1,2,3', '1.2,3', '\u2212', '--5', '5-', '1e3', 'NaN', 'Infinity']) {
    expect(parseCzechNumber(input), JSON.stringify(input)).toBeNull();
  }
});

test('invalidNumberMessage: Czech message naming the field', () => {
  expect(invalidNumberMessage('Částka')).toBe('Částka: zadejte číslo, např. 1 234,5 nebo −20 000.');
});
