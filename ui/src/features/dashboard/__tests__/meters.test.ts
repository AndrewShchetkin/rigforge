import { describe, expect, it } from 'vitest';
import { blockBar, healthTone, meterFraction, meterTone } from '../meters.ts';

describe('healthTone', () => {
  it('is green below 60, yellow from 60, red from 80', () => {
    expect(healthTone(59.9)).toBe('ok');
    expect(healthTone(60)).toBe('warn');
    expect(healthTone(79.9)).toBe('warn');
    expect(healthTone(80)).toBe('bad');
  });

  it('is neutral when the reading is missing', () => {
    expect(healthTone(null)).toBe('neutral');
    expect(healthTone(Number.NaN)).toBe('neutral');
  });
});

describe('meterTone', () => {
  it('paints temperature and load, and leaves power and rpm neutral', () => {
    expect(meterTone('GpuTemperature', 72)).toBe('warn');
    expect(meterTone('CpuLoad', 22)).toBe('ok');
    expect(meterTone('GpuPower', 186)).toBe('neutral');
    expect(meterTone('PumpSpeed', 2840)).toBe('neutral');
  });
});

describe('meterFraction', () => {
  it('scales each role against its display ceiling', () => {
    expect(meterFraction('CpuTemperature', 48)).toBeCloseTo(0.48);
    expect(meterFraction('GpuPower', 200)).toBeCloseTo(0.5);
    expect(meterFraction('PumpSpeed', 1500)).toBeCloseTo(0.5);
    expect(meterFraction('CpuLoad', null)).toBe(0);
  });

  it('clamps a reading past the ceiling', () => {
    expect(meterFraction('GpuTemperature', 140)).toBe(1);
    expect(meterFraction('CpuLoad', -5)).toBe(0);
  });
});

describe('blockBar', () => {
  it('rounds a fraction into ten cells', () => {
    expect(blockBar(0.48)).toEqual({ filled: '█████', empty: '░░░░░' });
    expect(blockBar(0)).toEqual({ filled: '', empty: '░░░░░░░░░░' });
    expect(blockBar(1)).toEqual({ filled: '██████████', empty: '' });
  });

  it('clamps fractions outside 0..1', () => {
    expect(blockBar(2).filled).toHaveLength(10);
    expect(blockBar(-1).empty).toHaveLength(10);
  });
});
