import { useCallback, useMemo, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { useApi } from '../hooks/useApi';
import { getReadings } from '../api/readings';
import { getHouses } from '../api/houses';
import type { House, ReadingResponse } from '../types';

const formatNumber = (value: number, decimals = 1): string =>
  new Intl.NumberFormat('cs-CZ', {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);

const formatDate = (dateStr: string): string =>
  new Intl.DateTimeFormat('cs-CZ').format(new Date(dateStr));

const sourceLabel = (source: string): string => {
  switch (source) {
    case 'Import':
      return 'Import';
    case 'Manual':
      return 'Manuální';
    default:
      return source;
  }
};

type SortField = 'houseName' | 'readingDate' | 'value' | 'consumption';
type SortDir = 'asc' | 'desc';

function getMonthOptions(): Array<{ label: string; year: number; month: number }> {
  const now = new Date();
  const options: Array<{ label: string; year: number; month: number }> = [];
  for (let i = 0; i < 24; i++) {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    const label = new Intl.DateTimeFormat('cs-CZ', {
      year: 'numeric',
      month: 'long',
    }).format(d);
    options.push({
      label: label.charAt(0).toUpperCase() + label.slice(1),
      year: d.getFullYear(),
      month: d.getMonth() + 1,
    });
  }
  return options;
}

export function ReadingsOverviewPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';

  const now = new Date();
  const [selectedYear, setSelectedYear] = useState(now.getFullYear());
  const [selectedMonth, setSelectedMonth] = useState(now.getMonth() + 1);
  const [selectedHouseId, setSelectedHouseId] = useState<string>('all');
  const [sortField, setSortField] = useState<SortField>('houseName');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  const monthOptions = useMemo(() => getMonthOptions(), []);

  const fetchReadings = useCallback(
    () => getReadings(selectedYear, selectedMonth),
    [selectedYear, selectedMonth],
  );

  const fetchHouses = useCallback(() => getHouses(), []);

  const {
    data: readingsData,
    loading: readingsLoading,
    error: readingsError,
  } = useApi(fetchReadings, [selectedYear, selectedMonth]);

  const { data: houses } = useApi<House[]>(fetchHouses, []);

  const handleMonthChange = (value: string) => {
    const [y, m] = value.split('-').map(Number);
    setSelectedYear(y);
    setSelectedMonth(m);
  };

  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortDir((prev) => (prev === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortField(field);
      setSortDir('asc');
    }
  };

  const filteredReadings = useMemo(() => {
    if (!readingsData?.readings) return [];
    let readings = [...readingsData.readings];

    if (selectedHouseId !== 'all') {
      const selectedHouse = houses?.find((h) => h.id === selectedHouseId);
      if (selectedHouse) {
        readings = readings.filter((r) => r.houseName === selectedHouse.name);
      }
    }

    readings.sort((a, b) => {
      let cmp = 0;
      switch (sortField) {
        case 'houseName':
          cmp = (a.houseName ?? '').localeCompare(b.houseName ?? '', 'cs');
          break;
        case 'readingDate':
          cmp =
            new Date(a.readingDate).getTime() -
            new Date(b.readingDate).getTime();
          break;
        case 'value':
          cmp = a.value - b.value;
          break;
        case 'consumption':
          cmp = (a.consumption ?? 0) - (b.consumption ?? 0);
          break;
      }
      return sortDir === 'asc' ? cmp : -cmp;
    });

    return readings;
  }, [readingsData, selectedHouseId, sortField, sortDir, houses]);

  // Compute summary
  const summary = useMemo(() => {
    if (!readingsData?.readings) return null;
    const readings = readingsData.readings;
    const mainMeterReading = readings.find((r) => r.houseName === null);
    const houseReadings = readings.filter((r) => r.houseName !== null);
    const totalConsumption = houseReadings.reduce(
      (sum, r) => sum + (r.consumption ?? 0),
      0,
    );
    const mainConsumption = mainMeterReading?.consumption ?? null;
    const loss =
      mainConsumption !== null ? mainConsumption - totalConsumption : null;

    return {
      mainMeterValue: mainMeterReading?.value ?? null,
      mainConsumption,
      totalConsumption,
      loss,
    };
  }, [readingsData]);

  const sortIndicator = (field: SortField) => {
    if (sortField !== field) return null;
    return sortDir === 'asc' ? ' \u2191' : ' \u2193';
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Odečty</h1>
        <p className="mt-1 text-sm text-gray-600">
          Přehled odečtů vodoměrů za vybrané období
        </p>
      </div>

      {/* Filters */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div className="flex-1 sm:max-w-xs">
          <label
            htmlFor="month-select"
            className="block text-sm font-medium text-gray-700"
          >
            Období
          </label>
          <select
            id="month-select"
            value={`${selectedYear}-${selectedMonth}`}
            onChange={(e) => handleMonthChange(e.target.value)}
            className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          >
            {monthOptions.map((opt) => (
              <option key={`${opt.year}-${opt.month}`} value={`${opt.year}-${opt.month}`}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>

        {isAdmin && houses && (
          <div className="flex-1 sm:max-w-xs">
            <label
              htmlFor="house-select"
              className="block text-sm font-medium text-gray-700"
            >
              Dům
            </label>
            <select
              id="house-select"
              value={selectedHouseId}
              onChange={(e) => setSelectedHouseId(e.target.value)}
              className="mt-1 block w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="all">Všechny domy</option>
              {houses.map((h) => (
                <option key={h.id} value={h.id}>
                  {h.name}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>

      {/* Loading */}
      {readingsLoading && (
        <div className="flex items-center justify-center py-12">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-600 border-t-transparent" />
        </div>
      )}

      {/* Error */}
      {readingsError && (
        <div className="rounded-md bg-red-50 p-4">
          <p className="text-sm text-red-700">{readingsError}</p>
        </div>
      )}

      {/* Table */}
      {!readingsLoading && !readingsError && (
        <>
          {filteredReadings.length === 0 ? (
            <div className="rounded-md bg-gray-50 py-12 text-center">
              <p className="text-sm text-gray-500">
                Pro vybrané období nebyly nalezeny žádné odečty.
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th
                      scope="col"
                      className="cursor-pointer px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700"
                      onClick={() => handleSort('readingDate')}
                    >
                      Datum{sortIndicator('readingDate')}
                    </th>
                    <th
                      scope="col"
                      className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500"
                    >
                      Vodoměr
                    </th>
                    <th
                      scope="col"
                      className="cursor-pointer px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700"
                      onClick={() => handleSort('houseName')}
                    >
                      Dům{sortIndicator('houseName')}
                    </th>
                    <th
                      scope="col"
                      className="cursor-pointer px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700"
                      onClick={() => handleSort('value')}
                    >
                      Stav (m³){sortIndicator('value')}
                    </th>
                    <th
                      scope="col"
                      className="cursor-pointer px-4 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-500 hover:text-gray-700"
                      onClick={() => handleSort('consumption')}
                    >
                      Spotřeba (m³){sortIndicator('consumption')}
                    </th>
                    <th
                      scope="col"
                      className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500"
                    >
                      Zdroj
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {filteredReadings.map((reading) => (
                    <ReadingRow key={`${reading.meterId}-${reading.readingDate}`} reading={reading} />
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Summary */}
          {summary && (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <SummaryCard
                label="Celková spotřeba"
                value={
                  summary.totalConsumption !== null
                    ? `${formatNumber(summary.totalConsumption)} m³`
                    : '-'
                }
              />
              {summary.mainMeterValue !== null && (
                <SummaryCard
                  label="Hlavní vodoměr"
                  value={`${formatNumber(summary.mainMeterValue)} m³`}
                />
              )}
              {summary.mainConsumption !== null && (
                <SummaryCard
                  label="Spotřeba hlavní vodoměr"
                  value={`${formatNumber(summary.mainConsumption)} m³`}
                />
              )}
              {isAdmin && summary.loss !== null && (
                <SummaryCard
                  label="Ztráta"
                  value={`${formatNumber(summary.loss)} m³`}
                  variant={summary.loss > 0 ? 'warning' : 'normal'}
                />
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function ReadingRow({ reading }: { reading: ReadingResponse }) {
  return (
    <tr className="hover:bg-gray-50">
      <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-900">
        {formatDate(reading.readingDate)}
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-700">
        {reading.meterNumber}
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-700">
        {reading.houseName ?? (
          <span className="font-medium text-blue-700">Hlavní vodoměr</span>
        )}
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
        {formatNumber(reading.value)}
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-right text-sm font-mono text-gray-900">
        {reading.consumption !== null ? formatNumber(reading.consumption) : '-'}
      </td>
      <td className="whitespace-nowrap px-4 py-3 text-sm">
        <span
          className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
            reading.source === 'Import'
              ? 'bg-blue-100 text-blue-700'
              : 'bg-gray-100 text-gray-700'
          }`}
        >
          {sourceLabel(reading.source)}
        </span>
      </td>
    </tr>
  );
}

function SummaryCard({
  label,
  value,
  variant = 'normal',
}: {
  label: string;
  value: string;
  variant?: 'normal' | 'warning';
}) {
  return (
    <div
      className={`rounded-lg border p-4 ${
        variant === 'warning'
          ? 'border-amber-200 bg-amber-50'
          : 'border-gray-200 bg-white'
      }`}
    >
      <p className="text-sm font-medium text-gray-500">{label}</p>
      <p
        className={`mt-1 text-xl font-semibold ${
          variant === 'warning' ? 'text-amber-700' : 'text-gray-900'
        }`}
      >
        {value}
      </p>
    </div>
  );
}
