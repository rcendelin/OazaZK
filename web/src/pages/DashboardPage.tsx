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
import {
  Droplets,
  Gauge,
  AlertTriangle,
  Home,
  Wallet,
  Upload,
  Receipt,
  Calendar,
  FileText,
  ArrowRight,
  TrendingUp,
  TrendingDown,
} from 'lucide-react';
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
    <div className="rounded-2xl bg-surface-raised shadow-card">
      <div className="flex items-center justify-between px-6 py-5">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-accent-light text-accent">
            <Wallet size={18} />
          </div>
          <h3 className="text-base font-semibold text-text-primary">Hospodaření</h3>
        </div>
        <Link
          to="/finance"
          className="flex items-center gap-1 text-sm font-medium text-accent hover:text-accent-hover"
        >
          Zobrazit <ArrowRight size={14} />
        </Link>
      </div>
      <div className="border-t border-border px-6 py-5">
        {financeSummaryBalance !== null && (
          <div className="mb-5">
            <p className="text-[13px] text-text-muted">Bilance aktuálního roku</p>
            <p
              className={`mt-1 text-2xl font-bold ${financeSummaryBalance >= 0 ? 'text-success' : 'text-danger'}`}
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
                className="flex items-center justify-between"
              >
                <div className="flex items-center gap-3 min-w-0 flex-1">
                  <div className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${record.type === 'Income' ? 'bg-success-light text-success' : 'bg-danger-light text-danger'}`}>
                    {record.type === 'Income' ? <TrendingUp size={14} /> : <TrendingDown size={14} />}
                  </div>
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-text-primary">
                      {record.description}
                    </p>
                    <p className="text-xs text-text-muted">
                      {czDate.format(new Date(record.date))}
                    </p>
                  </div>
                </div>
                <span
                  className={`ml-3 shrink-0 text-sm font-semibold ${record.type === 'Income' ? 'text-success' : 'text-danger'}`}
                >
                  {record.type === 'Income' ? '+' : '-'}
                  {czCurrency.format(record.amount)} Kč
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-text-muted">
            Žádné finanční záznamy
          </p>
        )}
        <button
          onClick={() => navigate('/finance')}
          className="mt-5 w-full rounded-xl border border-border bg-surface-raised py-2.5 text-sm font-medium text-text-secondary transition-colors hover:bg-surface-sunken"
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
    <div className="rounded-2xl bg-surface-raised shadow-card">
      <div className="flex items-center justify-between px-6 py-5">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-info-light text-info">
            <FileText size={18} />
          </div>
          <h3 className="text-base font-semibold text-text-primary">Dokumenty</h3>
        </div>
        <Link
          to="/documents"
          className="flex items-center gap-1 text-sm font-medium text-accent hover:text-accent-hover"
        >
          Zobrazit <ArrowRight size={14} />
        </Link>
      </div>
      <div className="border-t border-border px-6 py-5">
        <div className="mb-5">
          <p className="text-[13px] text-text-muted">Celkem dokumentů</p>
          <p className="mt-1 text-2xl font-bold text-text-primary">{totalCount}</p>
        </div>
        {last3.length > 0 ? (
          <ul className="space-y-3">
            {last3.map((doc) => (
              <li key={doc.id} className="flex items-center gap-3">
                <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-surface-sunken text-text-muted">
                  <FileText size={14} />
                </div>
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-text-primary">
                    {doc.name}
                  </p>
                  <p className="text-xs text-text-muted">
                    {categoryLabels[doc.category] ?? doc.category}
                    {' \u00B7 '}
                    {czDate.format(new Date(doc.uploadedAt))}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-text-muted">
            Žádné dokumenty
          </p>
        )}
        <button
          onClick={() => navigate('/documents')}
          className="mt-5 w-full rounded-xl border border-border bg-surface-raised py-2.5 text-sm font-medium text-text-secondary transition-colors hover:bg-surface-sunken"
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
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Přehled</h1>
          <p className="mt-1 text-sm text-text-muted">
            {czDate.format(now)} — aktuální stav
          </p>
        </div>
      </div>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <MetricCard
          title="Hlavní vodoměr"
          value={
            metrics.mainMeterValue !== null
              ? `${czNumber.format(metrics.mainMeterValue)} m\u00B3`
              : '\u2014'
          }
          subtitle="Aktuální stav"
          icon={<Droplets size={20} />}
        />
        <MetricCard
          title="Celková spotřeba"
          value={
            metrics.totalConsumption !== null
              ? `${czNumber.format(metrics.totalConsumption)} m\u00B3`
              : '\u2014'
          }
          subtitle="Součet domácností"
          icon={<Gauge size={20} />}
        />
        <MetricCard
          title="Ztráta na síti"
          value={
            metrics.networkLoss !== null
              ? `${czNumber.format(metrics.networkLoss)} m\u00B3`
              : '\u2014'
          }
          subtitle="Rozdíl hlavní - součet"
          icon={<AlertTriangle size={20} />}
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
          icon={<Home size={20} />}
        />
        <MetricCard
          title="Stav účtu spolku"
          value={
            financeBalance
              ? `${czCurrency.format(financeBalance.balance)} Kč`
              : '\u2014'
          }
          subtitle="Kumulativní bilance"
          icon={<Wallet size={20} />}
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
          <div className="rounded-2xl bg-surface-raised shadow-card">
            <div className="px-6 py-5">
              <h2 className="text-base font-semibold text-text-primary">
                Poslední odečty
              </h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead>
                  <tr className="border-t border-b border-border">
                    <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Dům</th>
                    <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Stav vodoměru</th>
                    <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Spotřeba</th>
                    <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-text-muted">Status</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {houseReadingsMap.map(({ house, reading }) => (
                    <tr key={house.id} className="transition-colors hover:bg-surface-sunken/50">
                      <td className="px-6 py-3.5 font-medium text-text-primary">
                        {house.name}
                      </td>
                      <td className="px-6 py-3.5 text-text-secondary">
                        {reading
                          ? `${czNumber.format(reading.value)} m\u00B3`
                          : '\u2014'}
                      </td>
                      <td className="px-6 py-3.5 text-text-secondary">
                        {reading?.consumption !== null &&
                        reading?.consumption !== undefined
                          ? `${czNumber.format(reading.consumption)} m\u00B3`
                          : '\u2014'}
                      </td>
                      <td className="px-6 py-3.5">
                        {reading ? (
                          <span className="inline-flex items-center rounded-full bg-success-light px-2.5 py-1 text-xs font-medium text-success">
                            Kompletní
                          </span>
                        ) : (
                          <span className="inline-flex items-center rounded-full bg-warning-light px-2.5 py-1 text-xs font-medium text-warning">
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
                        className="px-6 py-10 text-center text-text-muted"
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
          <div className="rounded-2xl bg-surface-raised p-6 shadow-card">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-text-muted">
              Rychlé akce
            </h3>
            <div className="mt-4 space-y-2.5">
              <button
                onClick={() => navigate('/readings/import')}
                className="flex w-full items-center gap-3 rounded-xl bg-accent px-4 py-3 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
              >
                <Upload size={18} />
                Import odečtů
              </button>
              <button
                onClick={() => navigate('/billing')}
                className="flex w-full items-center gap-3 rounded-xl border border-border bg-surface-raised px-4 py-3 text-sm font-medium text-text-secondary transition-colors hover:bg-surface-sunken"
              >
                <Receipt size={18} />
                Nové vyúčtování
              </button>
            </div>
          </div>

          {/* Open billing periods */}
          <div className="rounded-2xl bg-surface-raised p-6 shadow-card">
            <h3 className="text-xs font-semibold uppercase tracking-wider text-text-muted">
              Otevřená období
            </h3>
            {openPeriods.length === 0 ? (
              <p className="mt-4 text-sm text-text-muted">
                Žádná otevřená období
              </p>
            ) : (
              <ul className="mt-4 space-y-3">
                {openPeriods.map((period) => (
                  <li
                    key={period.id}
                    className="rounded-xl border border-border p-3.5"
                  >
                    <div className="flex items-center gap-2">
                      <Calendar size={14} className="text-accent" />
                      <p className="text-sm font-medium text-text-primary">
                        {period.name}
                      </p>
                    </div>
                    <p className="mt-1 text-xs text-text-muted">
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
      <h1 className="text-2xl font-bold text-text-primary">Přehled</h1>
      <p className="mt-1 text-sm text-text-muted">
        {czDate.format(new Date())} — váš přehled
      </p>

      {/* Metric cards */}
      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <MetricCard
          title="Stav vodoměru"
          value={
            myReading
              ? `${czNumber.format(myReading.value)} m\u00B3`
              : '\u2014'
          }
          subtitle="Aktuální stav"
          icon={<Droplets size={20} />}
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
          icon={<Gauge size={20} />}
        />
        <MetricCard
          title="Poslední vyúčtování"
          value={lastClosedPeriod ? lastClosedPeriod.name : '\u2014'}
          subtitle={
            lastClosedPeriod
              ? `Období do ${czDate.format(new Date(lastClosedPeriod.dateTo))}`
              : 'Žádné uzavřené období'
          }
          icon={<Receipt size={20} />}
        />
        <MetricCard
          title="Stav účtu"
          value={
            memberBalance !== null
              ? `${czCurrency.format(memberBalance)} Kč`
              : '\u2014'
          }
          subtitle={
            memberBalance !== null
              ? memberBalance >= 0
                ? 'Přeplatek'
                : 'Nedoplatek'
              : 'Zálohy vs. vyúčtování'
          }
          icon={<Wallet size={20} />}
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
      <div className="mt-6 rounded-2xl bg-surface-raised p-6 shadow-card">
        <h2 className="text-base font-semibold text-text-primary">
          Poslední vyúčtování
        </h2>
        {lastClosedPeriod ? (
          <p className="mt-2 text-sm text-text-secondary">
            Období: {lastClosedPeriod.name} (
            {czDate.format(new Date(lastClosedPeriod.dateFrom))}
            {' \u2013 '}
            {czDate.format(new Date(lastClosedPeriod.dateTo))})
          </p>
        ) : (
          <p className="mt-2 text-sm text-text-muted">
            Zatím nebylo provedeno žádné vyúčtování.
          </p>
        )}
      </div>
    </div>
  );
}

function useMemberBalance(
  houseId: string | null,
  periods: { id: string; status: string }[],
): number | null {
  const closedPeriodIds = useMemo(
    () => periods.filter((p) => p.status === 'Closed').map((p) => p.id),
    [periods],
  );

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
    const mySettlements = allSettlements.filter((s) => s.houseId === houseId);
    if (mySettlements.length === 0) return null;
    const totalBalance = mySettlements.reduce((sum, s) => sum + s.balance, 0);
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
