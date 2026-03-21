import { useMemo } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.tsx';
import { useApi } from '../hooks/useApi.ts';
import { getHouses } from '../api/houses.ts';
import { getReadings } from '../api/readings.ts';
import { getBillingPeriods } from '../api/billing.ts';
import { getFinanceSummary, getFinanceBalance, getFinanceRecords } from '../api/finance.ts';
import { getDocuments } from '../api/documents.ts';
import { getSettlements } from '../api/settlements.ts';
import { MetricCard } from '../components/MetricCard.tsx';
import { ConsumptionChart } from '../components/ConsumptionChart.tsx';
import { Spinner } from '../components/Spinner.tsx';
import type { FinanceResponse, DocumentResponse } from '../types/index.ts';

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
  zapisy: 'Z\u00E1pisy',
  smlouvy: 'Smlouvy',
  ostatni: 'Ostatn\u00ED',
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
            Hospoda\u0159en\u00ED
          </h3>
          <Link
            to="/finance"
            className="text-sm font-medium text-blue-600 hover:text-blue-800"
          >
            Zobrazit v\u0161e
          </Link>
        </div>
      </div>
      <div className="p-5">
        {financeSummaryBalance !== null && (
          <div className="mb-4">
            <p className="text-sm text-gray-500">Bilance aktu\u00E1ln\u00EDho roku</p>
            <p
              className={`text-xl font-bold ${financeSummaryBalance >= 0 ? 'text-green-600' : 'text-red-600'}`}
            >
              {czCurrency.format(financeSummaryBalance)} K\u010D
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
                  {czCurrency.format(record.amount)} K\u010D
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-gray-400">
            \u017D\u00E1dn\u00E9 finan\u010Dn\u00ED z\u00E1znamy
          </p>
        )}
        <button
          onClick={() => navigate('/finance')}
          className="mt-4 w-full rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50"
        >
          P\u0159ej\u00EDt na hospoda\u0159en\u00ED
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
            Zobrazit v\u0161e
          </Link>
        </div>
      </div>
      <div className="p-5">
        <div className="mb-4">
          <p className="text-sm text-gray-500">Celkem dokument\u016F</p>
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
                  {' \u2022 '}
                  {czDate.format(new Date(doc.uploadedAt))}
                </p>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-gray-400">
            \u017D\u00E1dn\u00E9 dokumenty
          </p>
        )}
        <button
          onClick={() => navigate('/documents')}
          className="mt-4 w-full rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50"
        >
          P\u0159ej\u00EDt na dokumenty
        </button>
      </div>
    </div>
  );
}

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
      <h1 className="text-2xl font-bold text-gray-900">P\u0159ehled</h1>
      <p className="mt-1 text-sm text-gray-500">
        {czDate.format(now)} — aktu\u00E1ln\u00ED stav
      </p>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <MetricCard
          title="Hlavn\u00ED vodom\u011Br"
          value={
            metrics.mainMeterValue !== null
              ? `${czNumber.format(metrics.mainMeterValue)} m\u00B3`
              : '\u2014'
          }
          subtitle="Aktu\u00E1ln\u00ED stav"
        />
        <MetricCard
          title="Celkov\u00E1 spot\u0159eba"
          value={
            metrics.totalConsumption !== null
              ? `${czNumber.format(metrics.totalConsumption)} m\u00B3`
              : '\u2014'
          }
          subtitle="Sou\u010Det dom\u00E1cnost\u00ED"
        />
        <MetricCard
          title="Ztr\u00E1ta na s\u00EDti"
          value={
            metrics.networkLoss !== null
              ? `${czNumber.format(metrics.networkLoss)} m\u00B3`
              : '\u2014'
          }
          subtitle="Rozd\u00EDl hlavn\u00ED - sou\u010Det"
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
          title="Aktivn\u00ED domy"
          value={
            metrics.activeHouses !== null
              ? String(metrics.activeHouses)
              : '\u2014'
          }
          subtitle="Registrovan\u00E9 dom\u00E1cnosti"
        />
        <MetricCard
          title="Stav \u00FA\u010Dtu spolku"
          value={
            financeBalance
              ? `${czCurrency.format(financeBalance.balance)} K\u010D`
              : '\u2014'
          }
          subtitle="Kumulativn\u00ED bilance"
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
                Ode\u010Dty za aktu\u00E1ln\u00ED m\u011Bs\u00EDc
              </h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-gray-50 text-xs uppercase text-gray-500">
                  <tr>
                    <th className="px-5 py-3">D\u016Fm</th>
                    <th className="px-5 py-3">Stav vodom\u011Bru</th>
                    <th className="px-5 py-3">Spot\u0159eba</th>
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
                            Kompletn\u00ED
                          </span>
                        ) : (
                          <span className="inline-flex rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800">
                            Nekompletn\u00ED
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
                        \u017D\u00E1dn\u00E9 ode\u010Dty pro tento m\u011Bs\u00EDc
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
              Rychl\u00E9 akce
            </h3>
            <div className="mt-3 space-y-2">
              <button
                onClick={() => navigate('/readings/import')}
                className="w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700"
              >
                Import ode\u010Dt\u016F
              </button>
              <button
                onClick={() => navigate('/billing')}
                className="w-full rounded-md bg-white px-4 py-2 text-sm font-medium text-gray-700 ring-1 ring-gray-300 transition-colors hover:bg-gray-50"
              >
                Nov\u00E9 vy\u00FA\u010Dtov\u00E1n\u00ED
              </button>
            </div>
          </div>

          {/* Open billing periods */}
          <div className="rounded-lg bg-white p-5 shadow-sm">
            <h3 className="text-sm font-semibold uppercase text-gray-500">
              Otev\u0159en\u00E1 obdob\u00ED
            </h3>
            {openPeriods.length === 0 ? (
              <p className="mt-3 text-sm text-gray-400">
                \u017D\u00E1dn\u00E1 otev\u0159en\u00E1 obdob\u00ED
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
      <h1 className="text-2xl font-bold text-gray-900">P\u0159ehled</h1>
      <p className="mt-1 text-sm text-gray-500">
        {czDate.format(now)} — v\u00E1\u0161 p\u0159ehled
      </p>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard
          title="Stav vodom\u011Bru"
          value={
            myReading
              ? `${czNumber.format(myReading.value)} m\u00B3`
              : '\u2014'
          }
          subtitle="Aktu\u00E1ln\u00ED stav"
        />
        <MetricCard
          title="Spot\u0159eba tento m\u011Bs\u00EDc"
          value={
            myReading?.consumption !== null &&
            myReading?.consumption !== undefined
              ? `${czNumber.format(myReading.consumption)} m\u00B3`
              : '\u2014'
          }
          subtitle="M\u011Bs\u00ED\u010Dn\u00ED spot\u0159eba"
        />
        <MetricCard
          title="Posledn\u00ED vy\u00FA\u010Dtov\u00E1n\u00ED"
          value={lastClosedPeriod ? lastClosedPeriod.name : '\u2014'}
          subtitle={
            lastClosedPeriod
              ? `Obdob\u00ED do ${czDate.format(new Date(lastClosedPeriod.dateTo))}`
              : '\u017D\u00E1dn\u00E9 uzav\u0159en\u00E9 obdob\u00ED'
          }
        />
        <MetricCard
          title="Stav \u00FA\u010Dtu"
          value={
            memberBalance !== null
              ? `${czCurrency.format(memberBalance)} K\u010D`
              : '\u2014'
          }
          subtitle={
            memberBalance !== null
              ? memberBalance >= 0
                ? 'P\u0159eplatek'
                : 'Nedoplatek'
              : 'Z\u00E1lohy vs. vy\u00FA\u010Dtov\u00E1n\u00ED'
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
          Posledn\u00ED vy\u00FA\u010Dtov\u00E1n\u00ED
        </h2>
        {lastClosedPeriod ? (
          <p className="mt-2 text-sm text-gray-600">
            Obdob\u00ED: {lastClosedPeriod.name} (
            {czDate.format(new Date(lastClosedPeriod.dateFrom))}
            {' \u2013 '}
            {czDate.format(new Date(lastClosedPeriod.dateTo))})
          </p>
        ) : (
          <p className="mt-2 text-sm text-gray-400">
            Zat\u00EDm nebylo provedeno \u017E\u00E1dn\u00E9 vy\u00FA\u010Dtov\u00E1n\u00ED.
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
