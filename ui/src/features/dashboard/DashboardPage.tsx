import { useMemo } from 'react';
import { Card } from '../../components/ui/Card.tsx';
import { useDaemonState, useReadings, useSystem } from '../../state/hooks.ts';
import { FanStatusList } from './FanStatusList.tsx';
import { PerformanceShowcase } from './PerformanceShowcase.tsx';
import { SensorTable } from './SensorTable.tsx';
import { StatCards } from './StatCards.tsx';
import { buildStats } from './stats.ts';

export function DashboardPage() {
  const system = useSystem();
  const readings = useReadings();
  const { fanStatus } = useDaemonState();
  const stats = useMemo(() => buildStats(readings), [readings]);

  return (
    <div className="space-y-2">
      <PerformanceShowcase />
      <StatCards stats={stats} />
      <div className="grid gap-2 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Card title="Sensors" description={`${readings.length} readings, grouped by device`}>
          <SensorTable readings={readings} devices={system.devices} />
        </Card>
        <Card title="Fan control" description="Live status of every controlled channel">
          <FanStatusList statuses={fanStatus} devices={system.devices} />
        </Card>
      </div>
    </div>
  );
}
