import { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';
import { useApi } from '../hooks/useApi.ts';
import { getHouses } from '../api/houses.ts';
import { getReadings } from '../api/readings.ts';
import { getBillingPeriods } from '../api/billing.ts';
import { MetricCard } from '../components/MetricCard.tsx';
import { Spinner } from '../components/Spinner.tsx';

const czNumber = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const czDate = new Intl.DateTimeFormat('cs-CZ');

function AdminDashboard() {
  const navigate = useNavigate();
  const now = new Date();
  const year = now.getFullYear();
  const month = now.getMonth() + 1;

  const { data: houses, loading: housesLoading } = useApi(
    () => getHouses(),
    [],
  );

  const { data: readingsData, loading: readingsLoading } = useApi(
    () => getReadings(year, month),
    [year, month],
  );

  const { data: periods, loading: periodsLoading } = useApi(
    () => getBillingPeriods(),
    [],
  );

  const loading = housesLoading || readingsLoading || periodsLoading;

  const metrics = useMemo(() => {
    if (!readingsData || !houses) {
      return {
        mainMeterValue: null,
        totalConsumption: null,
        networkLoss: null,
        activeHouses: null,
      };
    }

    const mainReading = readingsData.readings.find(
      (r) => r.houseName === null,
    );
    const individualReadings = readingsData.readings.filter(
      (r) => r.houseName !== null,
    );

    const totalIndividualConsumption = individualReadings.reduce(
      (sum, r) => sum + (r.consumption ?? 0),
      0,
    );

    const mainConsumption = mainReading?.consumption ?? 0;
    const loss = mainConsumption - totalIndividualConsumption;

    const activeHouses = houses.filter((h) => h.isActive).length;

    return {
      mainMeterValue: mainReading?.value ?? null,
      totalConsumption: totalIndividualConsumption,
      networkLoss: loss,
      activeHouses,
    };
  }, [readingsData, houses]);

  const openPeriods = useMemo(
    () => periods?.filter((p) => p.status === 'Open') ?? [],
    [periods],
  );

  const houseReadingsMap = useMemo(() => {
    if (!readingsData || !houses) return [];
    const activeHouses = houses.filter((h) => h.isActive);
    return activeHouses.map((house) => {
      const reading = readingsData.readings.find(
        (r) => r.houseName === house.name && r.houseName !== null,
      );
      return {
        house,
        reading: reading ?? null,
      };
    });
  }, [readingsData, houses]);

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <Spinner size="lg" />
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Přehled</h1>
      <p className="mt-1 text-sm text-gray-500">
        {czDate.format(now)} — aktuální stav
      </p>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard
          title="Hlavní vodoměr"
          value={
            metrics.mainMeterValue !== null
              ? `${czNumber.format(metrics.mainMeterValue)} m\u00B3`
              : '\u2014'
          }
          subtitle="Aktuální stav"
        />
        <MetricCard
          title="Celková spotřeba"
          value={
            metrics.totalConsumption !== null
              ? `${czNumber.format(metrics.totalConsumption)} m\u00B3`
              : '\u2014'
          }
          subtitle="Součet domácností"
        />
        <MetricCard
          title="Ztráta na síti"
          value={
            metrics.networkLoss !== null
              ? `${czNumber.format(metrics.networkLoss)} m\u00B3`
              : '\u2014'
          }
          subtitle="Rozdíl hlavní - součet"
          trend={
            metrics.networkLoss !== null
              ? metrics.networkLoss > 0
                ? 'up'
                : metrics.networkLoss < 0
                  ? 'down'
                  : 'neutral'
              : undefined
          }
        />
        <MetricCard
          title="Aktivní domy"
          value={
            metrics.activeHouses !== null
              ? String(metrics.activeHouses)
              : '\u2014'
          }
          subtitle="Registrované domácnosti"
        />
      </div>

      {/* Readings table + sidebar */}
      <div className="mt-8 grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Readings table */}
        <div className="lg:col-span-2">
          <div className="rounded-lg bg-white shadow-sm">
            <div className="border-b border-gray-200 px-5 py-4">
              <h2 className="text-lg font-semibold text-gray-900">
                Odečty za aktuální měsíc
              </h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-gray-50 text-xs uppercase text-gray-500">
                  <tr>
                    <th className="px-5 py-3">Dům</th>
                    <th className="px-5 py-3">Stav vodoměru</th>
                    <th className="px-5 py-3">Spotřeba</th>
                    <th className="px-5 py-3">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {houseReadingsMap.map(({ house, reading }) => (
                    <tr key={house.id} className="hover:bg-gray-50">
                      <td className="px-5 py-3 font-medium text-gray-900">
                        {house.name}
                      </td>
                      <td className="px-5 py-3 text-gray-600">
                        {reading
                          ? `${czNumber.format(reading.value)} m\u00B3`
                          : '\u2014'}
                      </td>
                      <td className="px-5 py-3 text-gray-600">
                        {reading?.consumption !== null &&
                        reading?.consumption !== undefined
                          ? `${czNumber.format(reading.consumption)} m\u00B3`
                          : '\u2014'}
                      </td>
                      <td className="px-5 py-3">
                        {reading ? (
                          <span className="inline-flex rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                            Kompletní
                          </span>
                        ) : (
                          <span className="inline-flex rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800">
                            Nekompletní
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                  {houseReadingsMap.length === 0 && (
                    <tr>
                      <td
                        colSpan={4}
                        className="px-5 py-8 text-center text-gray-400"
                      >
                        Žádné odečty pro tento měsíc
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>

        {/* Right sidebar */}
        <div className="space-y-6">
          {/* Quick actions */}
          <div className="rounded-lg bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold uppercase text-gray-500">
              Rychlé akce
            </h3>
            <div className="mt-3 space-y-2">
              <button
                onClick={() => navigate('/readings/import')}
                className="w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700"
              >
                Import odečtů
              </button>
              <button
                onClick={() => navigate('/billing')}
                className="w-full rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50"
              >
                Nové vyúčtování
              </button>
            </div>
          </div>

          {/* Open billing periods */}
          <div className="rounded-lg bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold uppercase text-gray-500">
              Otevřená období
            </h3>
            {openPeriods.length === 0 ? (
              <p className="mt-3 text-sm text-gray-400">
                Žádná otevřená období
              </p>
            ) : (
              <ul className="mt-3 space-y-3">
                {openPeriods.map((period) => (
                  <li
                    key={period.id}
                    className="rounded-md border border-gray-200 p-3"
                  >
                    <p className="text-sm font-medium text-gray-900">
                      {period.name}
                    </p>
                    <p className="mt-0.5 text-xs text-gray-500">
                      {czDate.format(new Date(period.dateFrom))}
                      {' \u2013 '}
                      {czDate.format(new Date(period.dateTo))}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function MemberDashboard() {
  const now = new Date();
  const year = now.getFullYear();
  const month = now.getMonth() + 1;

  const { data: readingsData, loading: readingsLoading } = useApi(
    () => getReadings(year, month),
    [year, month],
  );

  const { data: periods, loading: periodsLoading } = useApi(
    () => getBillingPeriods(),
    [],
  );

  const loading = readingsLoading || periodsLoading;

  const myReading = useMemo(() => {
    if (!readingsData) return null;
    // For members, the API already filters to their house's readings.
    // Pick the individual (non-main) reading.
    return readingsData.readings.find(
      (r) => r.houseName !== null,
    ) ?? null;
  }, [readingsData]);

  const lastClosedPeriod = useMemo(
    () =>
      periods
        ?.filter((p) => p.status === 'Closed')
        .sort(
          (a, b) =>
            new Date(b.dateTo).getTime() - new Date(a.dateTo).getTime(),
        )[0] ?? null,
    [periods],
  );

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <Spinner size="lg" />
      </div>
    );
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Přehled</h1>
      <p className="mt-1 text-sm text-gray-500">
        {czDate.format(now)} — váš přehled
      </p>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <MetricCard
          title="Stav vodoměru"
          value={
            myReading
              ? `${czNumber.format(myReading.value)} m\u00B3`
              : '\u2014'
          }
          subtitle="Aktuální stav"
        />
        <MetricCard
          title="Spotřeba tento měsíc"
          value={
            myReading?.consumption !== null &&
            myReading?.consumption !== undefined
              ? `${czNumber.format(myReading.consumption)} m\u00B3`
              : '\u2014'
          }
          subtitle="Měsíční spotřeba"
        />
        <MetricCard
          title="Poslední vyúčtování"
          value={lastClosedPeriod ? lastClosedPeriod.name : '\u2014'}
          subtitle={
            lastClosedPeriod
              ? `Období do ${czDate.format(new Date(lastClosedPeriod.dateTo))}`
              : 'Žádné uzavřené období'
          }
        />
      </div>

      {/* Consumption chart placeholder */}
      <div className="mt-8 rounded-lg bg-white p-8 text-center shadow-sm">
        <p className="text-gray-400">
          Graf spotřeby — připravujeme
        </p>
      </div>

      {/* Last settlement info */}
      <div className="mt-6 rounded-lg bg-white p-5 shadow-sm">
        <h2 className="text-lg font-semibold text-gray-900">
          Poslední vyúčtování
        </h2>
        {lastClosedPeriod ? (
          <p className="mt-2 text-sm text-gray-600">
            Období: {lastClosedPeriod.name} (
            {czDate.format(new Date(lastClosedPeriod.dateFrom))}
            {' \u2013 '}
            {czDate.format(new Date(lastClosedPeriod.dateTo))})
          </p>
        ) : (
          <p className="mt-2 text-sm text-gray-400">
            Zatím nebylo provedeno žádné vyúčtování.
          </p>
        )}
      </div>
    </div>
  );
}

export function DashboardPage() {
  const { user } = useAuth();

  if (user?.role === 'Admin') {
    return <AdminDashboard />;
  }

  return <MemberDashboard />;
}
