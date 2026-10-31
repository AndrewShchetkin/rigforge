import { Callout } from '../../components/ui/Callout.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { useSystem } from '../../state/hooks.ts';
import { curveIdFor } from './curveModel.ts';
import { FanChannelCard } from './FanChannelCard.tsx';

export function CoolingPage() {
  const { devices } = useSystem();
  const channels = devices.flatMap((device) => device.fanChannels.map((channel) => ({ device, channel })));

  return (
    <div className="space-y-6">
      <Callout title="Safety rules enforced by the daemon">
        Pumps are never driven below their minimum duty. Losing all temperature sources, or a liquid temperature of
        60 °C or more, forces 100 %.
      </Callout>
      {channels.length === 0 ? (
        <EmptyState title="No fan or pump channels detected">
          Fan headers and AIO pumps appear here once a supported controller is found.
        </EmptyState>
      ) : (
        <div className="grid gap-6 2xl:grid-cols-2">
          {channels.map(({ device, channel }) => (
            <FanChannelCard key={curveIdFor(device.id, channel.id)} device={device} channel={channel} />
          ))}
        </div>
      )}
    </div>
  );
}
