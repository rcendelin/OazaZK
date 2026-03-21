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
import type { ChartDataPoint, House } from '../types/index.ts';

const czNumber = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

type TimeRange = 6 | 12 | 24;

interface ConsumptionChartProps {
  /** If set, locks chart to a specific house (no house selector shown) */
  fixedHouseId?: string;
  /** Default time range in months */
  defaultRange?: TimeRange;
  /** Whether to show house filter dropdown (admin only) */
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
    <div className="rounded-lg bg-white p-5 shadow-sm">
      <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h3 className="text-lg font-semibold text-gray-900">
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
              className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="">Celkem (všechny domy)</option>
              {houses.map((h) => (
                <option key={h.id} value={h.id}>
                  {h.name}
                </option>
              ))}
            </select>
          )}
          <div className="flex rounded-md border border-gray-300">
            {([6, 12, 24] as TimeRange[]).map((range) => (
              <button
                key={range}
                onClick={() => setTimeRange(range)}
                className={`px-3 py-1.5 text-sm font-medium transition-colors ${
                  timeRange === range
                    ? 'bg-blue-600 text-white'
                    : 'bg-white text-gray-700 hover:bg-gray-50'
                } ${range === 6 ? 'rounded-l-md' : ''} ${range === 24 ? 'rounded-r-md' : ''}`}
              >
                {range} měs.
              </button>
            ))}
          </div>
        </div>
      </div>

      {loading && (
        <div className="flex items-center justify-center py-12">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
        </div>
      )}

      {error && (
        <div className="rounded-md bg-red-50 p-4">
          <p className="text-sm text-red-700">{error}</p>
        </div>
      )}

      {!loading && !error && chartDataForRecharts.length === 0 && (
        <div className="py-12 text-center">
          <p className="text-sm text-gray-400">
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
            <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
            <XAxis
              dataKey="name"
              tick={{ fontSize: 12 }}
              angle={-45}
              textAnchor="end"
              height={60}
            />
            <YAxis
              tick={{ fontSize: 12 }}
              tickFormatter={(value: number) => czNumber.format(value)}
              label={{
                value: 'm\u00B3',
                position: 'insideTopLeft',
                offset: -5,
                style: { fontSize: 12 },
              }}
            />
            <Tooltip
              formatter={(value) => [
                `${czNumber.format(Number(value))} m\u00B3`,
                'Spotřeba',
              ]}
              labelStyle={{ fontWeight: 'bold' }}
            />
            <Line
              type="monotone"
              dataKey="consumption"
              stroke="#2563eb"
              strokeWidth={2}
              dot={{ fill: '#2563eb', r: 4 }}
              activeDot={{ r: 6 }}
            />
            {averageConsumption !== null && (
              <ReferenceLine
                y={averageConsumption}
                stroke="#9ca3af"
                strokeDasharray="5 5"
                label={{
                  value: `Prům. ${czNumber.format(averageConsumption)}`,
                  position: 'insideTopRight',
                  style: { fontSize: 11, fill: '#9ca3af' },
                }}
              />
            )}
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
