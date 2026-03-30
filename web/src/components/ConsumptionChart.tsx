import { useCallback, useMemo, useState } from 'react';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  ResponsiveContainer,
  ReferenceLine,
} from 'recharts';
import { useAuth } from '../auth/AuthContext.tsx';
import { useApi } from '../hooks/useApi.ts';
import { getChartData } from '../api/readings.ts';
import { getHouses } from '../api/houses.ts';
import { Spinner } from './Spinner.tsx';
import type { ChartDataPoint, House } from '../types/index.ts';

const czNumber = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

type TimeRange = 6 | 12 | 24;

interface ConsumptionChartProps {
  fixedHouseId?: string;
  defaultRange?: TimeRange;
  showHouseFilter?: boolean;
}

export function ConsumptionChart({
  fixedHouseId,
  defaultRange = 12,
  showHouseFilter = false,
}: ConsumptionChartProps) {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const [selectedHouseId, setSelectedHouseId] = useState<string | undefined>(
    fixedHouseId,
  );
  const [timeRange, setTimeRange] = useState<TimeRange>(defaultRange);

  const fromDate = useMemo(() => {
    const d = new Date();
    d.setMonth(d.getMonth() - timeRange);
    return d.toISOString().split('T')[0];
  }, [timeRange]);

  const toDate = useMemo(() => new Date().toISOString().split('T')[0], []);

  const effectiveHouseId = fixedHouseId ?? selectedHouseId;

  const fetchChart = useCallback(
    () => getChartData(effectiveHouseId, fromDate, toDate),
    [effectiveHouseId, fromDate, toDate],
  );

  const fetchHouses = useCallback(() => getHouses(), []);

  const {
    data: chartData,
    loading,
    error,
  } = useApi(fetchChart, [effectiveHouseId, fromDate, toDate]);

  const { data: houses } = useApi<House[]>(
    fetchHouses,
    [],
  );

  const averageConsumption = useMemo(() => {
    if (!chartData?.dataPoints?.length) return null;
    const total = chartData.dataPoints.reduce(
      (sum, dp) => sum + dp.consumption,
      0,
    );
    return total / chartData.dataPoints.length;
  }, [chartData]);

  const chartDataForRecharts = useMemo(() => {
    if (!chartData?.dataPoints) return [];
    return chartData.dataPoints.map((dp: ChartDataPoint) => ({
      name: dp.label,
      consumption: dp.consumption,
    }));
  }, [chartData]);

  return (
    <div className="rounded-2xl bg-surface-raised p-6 shadow-card">
      <div className="mb-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h3 className="text-lg font-semibold text-text-primary">
          Graf spotřeby
          {chartData?.houseName ? ` — ${chartData.houseName}` : ''}
        </h3>
        <div className="flex flex-wrap items-center gap-2">
          {showHouseFilter && isAdmin && houses && !fixedHouseId && (
            <select
              value={selectedHouseId ?? ''}
              onChange={(e) =>
                setSelectedHouseId(e.target.value || undefined)
              }
              className="rounded-xl border border-border bg-surface-raised px-3 py-2 text-sm transition-colors focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
            >
              <option value="">Celkem (všechny domy)</option>
              {houses.map((h) => (
                <option key={h.id} value={h.id}>
                  {h.name}
                </option>
              ))}
            </select>
          )}
          <div className="flex overflow-hidden rounded-xl border border-border">
            {([6, 12, 24] as TimeRange[]).map((range) => (
              <button
                key={range}
                onClick={() => setTimeRange(range)}
                className={`px-3.5 py-2 text-sm font-medium transition-all ${
                  timeRange === range
                    ? 'bg-accent text-white'
                    : 'bg-surface-raised text-text-secondary hover:bg-surface-sunken'
                }`}
              >
                {range} měs.
              </button>
            ))}
          </div>
        </div>
      </div>

      {loading && (
        <div className="flex items-center justify-center py-16">
          <Spinner size="lg" />
        </div>
      )}

      {error && (
        <div className="rounded-xl bg-danger-light p-4">
          <p className="text-sm text-danger">{error}</p>
        </div>
      )}

      {!loading && !error && chartDataForRecharts.length === 0 && (
        <div className="py-16 text-center">
          <p className="text-sm text-text-muted">
            Pro vybrané období nejsou k dispozici žádná data.
          </p>
        </div>
      )}

      {!loading && !error && chartDataForRecharts.length > 0 && (
        <ResponsiveContainer width="100%" height={300}>
          <LineChart
            data={chartDataForRecharts}
            margin={{ top: 5, right: 20, left: 10, bottom: 5 }}
          >
            <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
            <XAxis
              dataKey="name"
              tick={{ fontSize: 12, fill: '#94a3b8' }}
              angle={-45}
              textAnchor="end"
              height={60}
            />
            <YAxis
              tick={{ fontSize: 12, fill: '#94a3b8' }}
              tickFormatter={(value: number) => czNumber.format(value)}
              label={{
                value: 'm\u00B3',
                position: 'insideTopLeft',
                offset: -5,
                style: { fontSize: 12, fill: '#94a3b8' },
              }}
            />
            <Tooltip
              contentStyle={{
                borderRadius: '12px',
                border: '1px solid #e2e8f0',
                boxShadow: '0 4px 6px -1px rgb(0 0 0 / 0.06)',
                fontSize: '13px',
              }}
              formatter={(value) => [
                `${czNumber.format(Number(value))} m\u00B3`,
                'Spotřeba',
              ]}
              labelStyle={{ fontWeight: 600 }}
            />
            <Line
              type="monotone"
              dataKey="consumption"
              stroke="#0d9488"
              strokeWidth={2.5}
              dot={{ fill: '#0d9488', r: 4, strokeWidth: 0 }}
              activeDot={{ r: 6, strokeWidth: 2, stroke: '#fff' }}
            />
            {averageConsumption !== null && (
              <ReferenceLine
                y={averageConsumption}
                stroke="#94a3b8"
                strokeDasharray="5 5"
                label={{
                  value: `Prům. ${czNumber.format(averageConsumption)}`,
                  position: 'insideTopRight',
                  style: { fontSize: 11, fill: '#94a3b8' },
                }}
              />
            )}
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
