import { useMemo } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';
import { useApi } from '../hooks/useApi.ts';
import { getHouses } from '../api/houses.ts';
import { getAllReadings } from '../api/readings.ts';
import { getBillingPeriods } from '../api/billing.ts';
import { getFinanceSummary, getFinanceBalance, getFinanceRecords } from '../api/finance.ts';
import { getDocuments } from '../api/documents.ts';
import { getSettlements } from '../api/settlements.ts';
import { MetricCard } from '../components/MetricCard.tsx';
import { ConsumptionChart } from '../components/ConsumptionChart.tsx';
import { Spinner } from '../components/Spinner.tsx';
import type { FinanceResponse, DocumentResponse, ReadingResponse } from '../types/index.ts';

const czNumber = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const czCurrency = new Intl.NumberFormat('cs-CZ', {
  minimumFractionDigits: 0,
  maximumFractionDigits: 0,
});

const czDate = new Intl.DateTimeFormat('cs-CZ');

const categoryLabels: Record<string, string> = {
  stanovy: 'Stanovy',
  zapisy: 'Zápisy',
  smlouvy: 'Smlouvy',
  ostatni: 'Ostatní',
};

function FinanceCard({
  financeRecords,
  financeSummaryBalance,
}: {
  financeRecords: FinanceResponse[] | null;
  financeSummaryBalance: number | null;
}) {
  const navigate = useNavigate();
  const last3 = useMemo(() => {
    if (!financeRecords) return [];
    return [...financeRecords]
      .sort(
        (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime(),
      )
      .slice(0, 3);
  }, [financeRecords]);

  return (
    <div className="rounded-lg bg-white shadow-sm">
      <div className="border-b border-gray-200 px-5 py-4">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-semibold text-gray-900">
            Hospodaření
          </h3>
          <Link
            to="/finance"
            className="text-sm font-medium text-blue-600 hover:text-blue-800"
          >
            Zobrazit vše
          </Link>
        </div>
      </div>
      <div className="p-5">
        {financeSummaryBalance !== null && (
          <div className="mb-4">
            <p className="text-sm text-gray-500">Bilance aktuálního roku</p>
            <p
              className={`text-xl font-bold ${financeSummaryBalance >= 0 ? 'text-green-600' : 'text-red-600'}`}
            >
              {czCurrency.format(financeSummaryBalance)} Kč
            </p>
          </div>
        )}
        {last3.length > 0 ? (
          <ul className="space-y-3">
            {last3.map((record) => (
              <li
                key={record.id}
                className="flex items-center justify-between text-sm"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium text-gray-900">
                    {record.description}
                  </p>
                  <p className="text-xs text-gray-500">
                    {czDate.format(new Date(record.date))}
                  </p>
                </div>
                <span
                  className={`ml-3 shrink-0 font-medium ${record.type === 'Income' ? 'text-green-600' : 'text-red-600'}`}
                >
                  {record.type === 'Income' ? '+' : '-'}
                  {czCurrency.format(record.amount)} Kč
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-gray-400">
            Žádné finanční záznamy
          </p>
        )}
        <button
          onClick={() => navigate('/finance')}
          className="mt-4 w-full rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50"
        >
          Přejít na hospodaření
        </button>
      </div>
    </div>
  );
}

function DocumentsCard({
  documents,
}: {
  documents: DocumentResponse[] | null;
}) {
  const navigate = useNavigate();
  const totalCount = documents?.length ?? 0;
  const last3 = useMemo(() => {
    if (!documents) return [];
    return [...documents]
      .sort(
        (a, b) =>
          new Date(b.uploadedAt).getTime() - new Date(a.uploadedAt).getTime(),
      )
      .slice(0, 3);
  }, [documents]);

  return (
    <div className="rounded-lg bg-white shadow-sm">
      <div className="border-b border-gray-200 px-5 py-4">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-semibold text-gray-900">Dokumenty</h3>
          <Link
            to="/documents"
            className="text-sm font-medium text-blue-600 hover:text-blue-800"
          >
            Zobrazit vše
          </Link>
        </div>
      </div>
      <div className="p-5">
        <div className="mb-4">
          <p className="text-sm text-gray-500">Celkem dokumentů</p>
          <p className="text-xl font-bold text-gray-900">{totalCount}</p>
        </div>
        {last3.length > 0 ? (
          <ul className="space-y-3">
            {last3.map((doc) => (
              <li key={doc.id} className="text-sm">
                <p className="truncate font-medium text-gray-900">
                  {doc.name}
                </p>
                <p className="text-xs text-gray-500">
                  {categoryLabels[doc.category] ?? doc.category}
                  {' \• '}
                  {czDate.format(new Date(doc.uploadedAt))}
                </p>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-gray-400">
            Žádné dokumenty
          </p>
        )}
        <button
          onClick={() => navigate('/documents')}
          className="mt-4 w-full rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50"
        >
          Přejít na dokumenty
        </button>
      </div>
    </div>
  );
}

function AdminDashboard() {
  const navigate = useNavigate();
  const now = new Date();
  const year = now.getFullYear();

  const { data: houses, loading: housesLoading } = useApi(
    () => getHouses(),
    [],
  );

  const { data: allReadings, loading: readingsLoading } = useApi(
    () => getAllReadings(),
    [],
  );

  const { data: periods, loading: periodsLoading } = useApi(
    () => getBillingPeriods(),
    [],
  );

  const { data: financeSummary, loading: financeSummaryLoading } = useApi(
    () => getFinanceSummary(year),
    [year],
  );

  const { data: financeBalance, loading: financeBalanceLoading } = useApi(
    () => getFinanceBalance(),
    [],
  );

  const { data: financeRecords, loading: financeRecordsLoading } = useApi(
    () => getFinanceRecords(year),
    [year],
  );

  const { data: documents, loading: documentsLoading } = useApi(
    () => getDocuments(),
    [],
  );

  const loading =
    housesLoading ||
    readingsLoading ||
    periodsLoading ||
    financeSummaryLoading ||
    financeBalanceLoading ||
    financeRecordsLoading ||
    documentsLoading;

  // Get latest reading per meter from all readings
  const latestByMeter = useMemo(() => {
    if (!allReadings) return new Map<string, ReadingResponse>();
    const map = new Map<string, ReadingResponse>();
    for (const r of allReadings) {
      const existing = map.get(r.meterId);
      if (!existing || new Date(r.readingDate) > new Date(existing.readingDate)) {
        map.set(r.meterId, r);
      }
    }
    return map;
  }, [allReadings]);

  const metrics = useMemo(() => {
    if (!allReadings || !houses) {
      return {
        mainMeterValue: null,
        totalConsumption: null,
        networkLoss: null,
        activeHouses: null,
      };
    }

    const latestReadings = [...latestByMeter.values()];
    const mainReading = latestReadings.find((r) => r.houseName === null);
    const individualReadings = latestReadings.filter((r) => r.houseName !== null);

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
  }, [allReadings, houses, latestByMeter]);

  const openPeriods = useMemo(
    () => periods?.filter((p) => p.status === 'Open') ?? [],
    [periods],
  );

  const houseReadingsMap = useMemo(() => {
    if (!allReadings || !houses) return [];
    const activeHouses = houses.filter((h) => h.isActive);
    return activeHouses.map((house) => {
      const reading = [...latestByMeter.values()].find(
        (r) => r.houseName === house.name && r.houseName !== null,
      );
      return {
        house,
        reading: reading ?? null,
      };
    });
  }, [allReadings, houses, latestByMeter]);

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
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <MetricCard
          title="Hlavní vodoměr"
          value={
            metrics.mainMeterValue !== null
              ? `${czNumber.format(metrics.mainMeterValue)} m\³`
              : '\—'
          }
          subtitle="Aktuální stav"
        />
        <MetricCard
          title="Celková spotřeba"
          value={
            metrics.totalConsumption !== null
              ? `${czNumber.format(metrics.totalConsumption)} m\³`
              : '\—'
          }
          subtitle="Součet domácností"
        />
        <MetricCard
          title="Ztráta na síti"
          value={
            metrics.networkLoss !== null
              ? `${czNumber.format(metrics.networkLoss)} m\³`
              : '\—'
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
              : '\—'
          }
          subtitle="Registrované domácnosti"
        />
        <MetricCard
          title="Stav účtu spolku"
          value={
            financeBalance
              ? `${czCurrency.format(financeBalance.balance)} Kč`
              : '\—'
          }
          subtitle="Kumulativní bilance"
          trend={
            financeBalance
              ? financeBalance.balance > 0
                ? 'up'
                : financeBalance.balance < 0
                  ? 'down'
                  : 'neutral'
              : undefined
          }
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
                          ? `${czNumber.format(reading.value)} m\³`
                          : '\—'}
                      </td>
                      <td className="px-5 py-3 text-gray-600">
                        {reading?.consumption !== null &&
                        reading?.consumption !== undefined
                          ? `${czNumber.format(reading.consumption)} m\³`
                          : '\—'}
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
                      {' \– '}
                      {czDate.format(new Date(period.dateTo))}
                    </p>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>

      {/* Finance & Documents cards */}
      <div className="mt-8 grid grid-cols-1 gap-6 lg:grid-cols-2">
        <FinanceCard
          financeRecords={financeRecords}
          financeSummaryBalance={financeSummary?.balance ?? null}
        />
        <DocumentsCard documents={documents} />
      </div>
    </div>
  );
}

function MemberDashboard() {
  const { user } = useAuth();

  const { data: allReadings, loading: readingsLoading } = useApi(
    () => getAllReadings(),
    [],
  );

  const { data: periods, loading: periodsLoading } = useApi(
    () => getBillingPeriods(),
    [],
  );

  const loading = readingsLoading || periodsLoading;

  const myReading = useMemo(() => {
    if (!allReadings) return null;
    // Find the latest reading for the member's house (non-main meter)
    const houseReadings = allReadings
      .filter((r) => r.houseName !== null)
      .sort((a, b) => new Date(b.readingDate).getTime() - new Date(a.readingDate).getTime());
    return houseReadings[0] ?? null;
  }, [allReadings]);

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

  // Compute member's cumulative advances vs. settlements balance
  const memberBalance = useMemberBalance(user?.houseId ?? null, periods ?? []);

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
        {czDate.format(new Date())} — váš přehled
      </p>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard
          title="Stav vodoměru"
          value={
            myReading
              ? `${czNumber.format(myReading.value)} m\³`
              : '\—'
          }
          subtitle="Aktuální stav"
        />
        <MetricCard
          title="Spotřeba tento měsíc"
          value={
            myReading?.consumption !== null &&
            myReading?.consumption !== undefined
              ? `${czNumber.format(myReading.consumption)} m\³`
              : '\—'
          }
          subtitle="Měsíční spotřeba"
        />
        <MetricCard
          title="Poslední vyúčtování"
          value={lastClosedPeriod ? lastClosedPeriod.name : '\—'}
          subtitle={
            lastClosedPeriod
              ? `Období do ${czDate.format(new Date(lastClosedPeriod.dateTo))}`
              : 'Žádné uzavřené období'
          }
        />
        <MetricCard
          title="Stav účtu"
          value={
            memberBalance !== null
              ? `${czCurrency.format(memberBalance)} Kč`
              : '\—'
          }
          subtitle={
            memberBalance !== null
              ? memberBalance >= 0
                ? 'Přeplatek'
                : 'Nedoplatek'
              : 'Zálohy vs. vyúčtování'
          }
          trend={
            memberBalance !== null
              ? memberBalance > 0
                ? 'up'
                : memberBalance < 0
                  ? 'down'
                  : 'neutral'
              : undefined
          }
        />
      </div>

      {/* Consumption chart */}
      <div className="mt-8">
        <ConsumptionChart
          fixedHouseId={user?.houseId ?? undefined}
          defaultRange={12}
        />
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
            {' \– '}
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

/**
 * Custom hook to compute a member's cumulative balance:
 * total advances paid - total settlement amounts across all closed periods.
 * Positive = overpayment (good for member), negative = underpayment.
 */
function useMemberBalance(
  houseId: string | null,
  periods: { id: string; status: string }[],
): number | null {
  const closedPeriodIds = useMemo(
    () => periods.filter((p) => p.status === 'Closed').map((p) => p.id),
    [periods],
  );

  // Fetch settlements for all closed periods to find this house's balance
  const { data: allSettlements, loading } = useApi(
    async () => {
      if (!houseId || closedPeriodIds.length === 0) return null;
      const results = await Promise.all(
        closedPeriodIds.map((periodId) => getSettlements(periodId)),
      );
      return results.flat();
    },
    [houseId, closedPeriodIds.join(',')],
  );

  return useMemo(() => {
    if (loading || !allSettlements || !houseId) return null;
    // Each settlement has balance: positive = underpayment, negative = overpayment
    // We invert: positive = overpayment (good), negative = underpayment (owes)
    const mySettlements = allSettlements.filter((s) => s.houseId === houseId);
    if (mySettlements.length === 0) return null;
    const totalBalance = mySettlements.reduce((sum, s) => sum + s.balance, 0);
    // balance in Settlement is: positive = underpayment, negative = overpayment
    // Invert so positive = overpayment for the member's perspective
    return -totalBalance;
  }, [allSettlements, houseId, loading]);
}

export function DashboardPage() {
  const { user } = useAuth();

  if (user?.role === 'Admin') {
    return <AdminDashboard />;
  }

  return <MemberDashboard />;
}
