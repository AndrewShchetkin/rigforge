import { useMemo, useState } from 'react';
import type { EffectCategory, EffectDescriptorDto, EffectSettings } from '../../api/contracts.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { SearchIcon } from '../../components/ui/icons.tsx';
import { EffectMiniPreview } from './EffectMiniPreview.tsx';

const CATEGORY_TABS = ['Flowing', 'Spatial', 'Reactive', 'Static', 'Custom'] as const satisfies readonly EffectCategory[];

type GalleryFilter = 'All' | EffectCategory;

interface EffectGalleryProps {
  effects: readonly EffectDescriptorDto[];
  value: EffectSettings;
  error?: string;
  onSelect: (effectId: string) => void;
}

function previewPalette(effect: EffectDescriptorDto, settings: EffectSettings): string {
  const parameter = effect.parameters.find((item) => item.kind === 'ColorList');
  const fallback = '#7C3AED,#22D3EE,#F472B6';
  if (!parameter) {
    return fallback;
  }
  const active = effect.id === settings.effectId ? settings.parameters[parameter.key] : undefined;
  const source = Array.isArray(active) ? active : parameter.defaultValue;
  if (!Array.isArray(source)) {
    return fallback;
  }
  const colors = source.filter((color): color is string => typeof color === 'string' && /^#[0-9a-f]{6}$/i.test(color));
  return colors.length > 0 ? colors.join(',') : fallback;
}

function usesSensor(effect: EffectDescriptorDto): boolean {
  return effect.parameters.some((parameter) => parameter.kind === 'SensorSelector');
}

/** Category tabs, search, and a two-column card grid in place of an effect dropdown. */
export function EffectGallery({ effects, value, error, onSelect }: EffectGalleryProps) {
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<GalleryFilter>('All');

  const counts = useMemo(() => {
    const tally: Record<EffectCategory, number> = { Flowing: 0, Spatial: 0, Reactive: 0, Static: 0, Custom: 0 };
    for (const effect of effects) {
      tally[effect.category] += 1;
    }
    return tally;
  }, [effects]);

  const tabs = CATEGORY_TABS.filter((category) => category !== 'Custom' || counts.Custom > 0);
  const normalized = query.trim().toLowerCase();
  const visible = effects.filter((effect) => {
    if (filter !== 'All' && effect.category !== filter) {
      return false;
    }
    if (normalized === '') {
      return true;
    }
    return (
      effect.name.toLowerCase().includes(normalized) ||
      effect.description.toLowerCase().includes(normalized) ||
      effect.id.toLowerCase().includes(normalized)
    );
  });
  const activeListed = effects.some((effect) => effect.id === value.effectId);
  const activeVisible = visible.some((effect) => effect.id === value.effectId);

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2 border border-line bg-base px-2.5 focus-within:border-accent">
        <SearchIcon className="size-3.5 shrink-0 text-zinc-500" />
        <input
          type="search"
          value={query}
          onChange={(event) => setQuery(event.currentTarget.value)}
          placeholder="Search effects…"
          aria-label="Search effects"
          className="w-full bg-transparent py-1.5 text-sm text-zinc-100 outline-none placeholder:text-zinc-500"
        />
      </div>
      <div role="group" aria-label="Effect categories" className="flex gap-1 overflow-x-auto pb-0.5">
        <CategoryTab label="All" count={effects.length} pressed={filter === 'All'} onClick={() => setFilter('All')} />
        {tabs.map((category) => (
          <CategoryTab
            key={category}
            label={category}
            count={counts[category]}
            pressed={filter === category}
            onClick={() => setFilter(category)}
          />
        ))}
      </div>
      {visible.length === 0 ? (
        <EmptyState title="No matching effects">Try another category or a shorter search.</EmptyState>
      ) : (
        <div role="group" aria-label="Effects" className="grid max-h-[28rem] grid-cols-2 gap-2 overflow-y-auto pr-0.5">
          {visible.map((effect) => {
            const selected = effect.id === value.effectId;
            return (
              <button
                key={effect.id}
                type="button"
                aria-pressed={selected}
                onClick={() => onSelect(effect.id)}
                className={`overflow-hidden border text-left transition-colors ${selected ? 'border-accent bg-accent/10 text-accent' : 'border-line bg-base hover:border-dim'}`}
              >
                <EffectMiniPreview
                  effectId={effect.id}
                  category={effect.category}
                  palette={previewPalette(effect, value)}
                  animated={effect.isAnimated}
                />
                <span className="block space-y-1 px-2 py-1.5">
                  <span className="block truncate text-sm font-medium text-zinc-100">{effect.name}</span>
                  <span className="block truncate text-[11px] text-zinc-500">{effect.description}</span>
                  <span className="flex flex-wrap gap-1">
                    {!effect.isAnimated && <Badge tone="success">0% CPU</Badge>}
                    {effect.usesAudio && <Badge tone="accent">Audio</Badge>}
                    {usesSensor(effect) && <Badge tone="info">Sensor</Badge>}
                    {effect.category === 'Custom' && <Badge>WASM</Badge>}
                  </span>
                </span>
              </button>
            );
          })}
        </div>
      )}
      {!activeListed && <p className="text-xs text-amber-300">{value.effectId} is not available on this daemon.</p>}
      {activeListed && !activeVisible && visible.length > 0 && (
        <p className="text-xs text-zinc-500">The active effect is hidden by this filter.</p>
      )}
      {error !== undefined && <p className="text-xs text-red-400">{error}</p>}
    </div>
  );
}

function CategoryTab({
  label,
  count,
  pressed,
  onClick,
}: {
  label: string;
  count: number;
  pressed: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      onClick={onClick}
      className={`inline-flex shrink-0 items-center gap-1 border px-2 py-1 text-xs whitespace-nowrap ${pressed ? 'border-accent/60 bg-accent/15 text-accent' : 'border-line text-dim hover:text-ink'}`}
    >
      {label}
      <span className="tabular-nums">{count}</span>
    </button>
  );
}
