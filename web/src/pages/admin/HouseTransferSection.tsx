import { useState } from 'react';
import type { FormEvent } from 'react';
import { ApiError } from '../../api/client';
import { previewHouseTransfer, transferHouse } from '../../api/houseTransfer';
import type { HouseTransferPreview } from '../../api/houseTransfer';
import type { House } from '../../types';
import { formatIsoDay, todayIso } from '../../utils/date';
import { invalidNumberMessage, parseCzechNumber } from '../../utils/number';
import { HelpTerm } from '../../components/help/HelpTerm';

const inputCls = 'border border-border rounded-lg px-2 py-1.5 text-sm bg-surface-raised focus:border-accent focus:ring-2 focus:ring-accent/20';
const primaryBtn = 'rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-50';
const kc = (v: number) => `${v.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč`;

function reasons(err: unknown): string[] {
  if (err instanceof ApiError && err.details.length > 0) return err.details;
  return [err instanceof Error ? err.message : 'Nastala neočekávaná chyba'];
}

/**
 * „Převod domu“ (T03, R7): closes the old owner at the day before the handover and starts the new owner
 * with their own opening values. Shows the impact before anything is written.
 */
export function HouseTransferSection({ houses, onDone }: { houses: House[]; onDone: () => void }) {
  const [houseId, setHouseId] = useState('');
  const [date, setDate] = useState(todayIso());
  const [preview, setPreview] = useState<HouseTransferPreview | null>(null);
  const [owner, setOwner] = useState('');
  const [contact, setContact] = useState('');
  const [meter, setMeter] = useState('');
  const [meterEstimate, setMeterEstimate] = useState(false);
  const [meterSource, setMeterSource] = useState('');
  const [fund, setFund] = useState('0');
  const [updateContact, setUpdateContact] = useState(true);
  const [errors, setErrors] = useState<string[]>([]);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const loadPreview = async () => {
    setBusy(true);
    setErrors([]);
    setInfo(null);
    try {
      const result = await previewHouseTransfer(houseId, date);
      setPreview(result);
      if (result.suggestedMeterValue !== null) setMeter(String(result.suggestedMeterValue).replace('.', ','));
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setErrors([]);
    const meterValue = preview?.meterId ? parseCzechNumber(meter) : undefined;
    const fundShare = fund.trim() === '' ? 0 : parseCzechNumber(fund);
    if (meterValue === null || fundShare === null) {
      setErrors([
        ...(meterValue === null ? [invalidNumberMessage('Stav vodoměru')] : []),
        ...(fundShare === null ? [invalidNumberMessage('Podíl ve fondu')] : []),
      ]);
      return;
    }
    setBusy(true);
    try {
      const result = await transferHouse(houseId, {
        transferDate: date,
        newOwnerName: owner,
        newOwnerContact: contact || undefined,
        meterValue,
        meterIsEstimate: meterEstimate,
        meterSource: meterSource || undefined,
        fundShare,
        updateHouseContact: updateContact,
      });
      setInfo(`Dům převeden. Závěrečné saldo původního vlastníka: ${kc(result.closingSaldo)} (${result.closingSaldo >= 0 ? 'přeplatek' : 'nedoplatek'}).`);
      setPreview(null);
      onDone();
    } catch (err) {
      setErrors(reasons(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="space-y-3 rounded-2xl border border-border bg-surface-raised p-4 shadow-card" aria-label="Převod domu">
      <div>
        <h2 className="text-lg font-semibold text-text-primary">Převod domu na nového majitele<HelpTerm id="prevodDomu" /></h2>
        <p className="text-xs text-text-muted">
          Den před předáním se uzavře saldo původního vlastníka (mezizávěrka domu). Nový vlastník nedědí historii — začíná
          se stavem vodoměru při předání a s nulovým podílem ve fondu (nebo se svým vkladem).
        </p>
      </div>
      <div className="flex flex-wrap items-end gap-2">
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Dům</span>
          <select value={houseId} onChange={(e) => { setHouseId(e.target.value); setPreview(null); }} className={inputCls}>
            <option value="">Vyberte…</option>
            {houses.map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
          </select>
        </label>
        <label className="text-xs text-text-secondary">
          <span className="mb-1 block">Nový vlastník od</span>
          <input type="date" value={date} onChange={(e) => { setDate(e.target.value); setPreview(null); }} className={inputCls} />
        </label>
        <button type="button" onClick={() => void loadPreview()} disabled={busy || !houseId || !date} className={primaryBtn}>Náhled dopadů</button>
      </div>

      {preview && (
        <div className="space-y-3" aria-label="Náhled převodu">
          <dl className="grid grid-cols-2 gap-x-6 gap-y-1 rounded-xl bg-surface-sunken p-3 text-sm sm:grid-cols-4">
            <div><dt className="text-xs text-text-muted">Původní vlastník</dt><dd>{preview.currentOwnerName ?? '—'}</dd></div>
            <div><dt className="text-xs text-text-muted">Uzavře se k</dt><dd>{formatIsoDay(preview.closingDate)}</dd></div>
            <div><dt className="text-xs text-text-muted">Platby / náklady</dt><dd>{kc(preview.closingPayments)} / {kc(preview.closingCosts)}</dd></div>
            <div>
              <dt className="text-xs text-text-muted">Závěrečné saldo</dt>
              <dd className={preview.closingSaldo >= 0 ? 'font-semibold text-success' : 'font-semibold text-danger'}>
                {kc(Math.abs(preview.closingSaldo))} {preview.closingSaldo >= 0 ? 'přeplatek' : 'nedoplatek'}
              </dd>
            </div>
          </dl>
          {preview.problems.length > 0 ? (
            <ul role="alert" className="space-y-1 rounded-lg bg-danger-light p-2 text-sm text-danger">
              {preview.problems.map((p) => <li key={p}>{p}</li>)}
            </ul>
          ) : (
            <form onSubmit={(e) => void submit(e)} className="space-y-2" aria-label="Údaje nového vlastníka">
              <div className="flex flex-wrap items-end gap-2">
                <label className="text-xs text-text-secondary">
                  <span className="mb-1 block">Nový vlastník</span>
                  <input value={owner} onChange={(e) => setOwner(e.target.value)} className={inputCls} required />
                </label>
                <label className="text-xs text-text-secondary">
                  <span className="mb-1 block">Kontakt (e-mail)</span>
                  <input value={contact} onChange={(e) => setContact(e.target.value)} className={inputCls} />
                </label>
                {preview.meterId && (
                  <>
                    <label className="text-xs text-text-secondary">
                      <span className="mb-1 block">Stav vodoměru {preview.meterNumber} (m³)</span>
                      <input value={meter} onChange={(e) => setMeter(e.target.value)} inputMode="decimal" className={`${inputCls} w-32 text-right`} required />
                    </label>
                    <label className="flex items-center gap-1 pb-2 text-xs text-text-secondary">
                      <input type="checkbox" checked={meterEstimate} onChange={(e) => setMeterEstimate(e.target.checked)} />
                      odhad
                    </label>
                    <label className="text-xs text-text-secondary">
                      <span className="mb-1 block">Zdroj stavu</span>
                      <input value={meterSource} onChange={(e) => setMeterSource(e.target.value)} placeholder="předávací protokol" className={inputCls} />
                    </label>
                  </>
                )}
                <label className="text-xs text-text-secondary">
                  <span className="mb-1 block">Podíl ve fondu (Kč)</span>
                  <input value={fund} onChange={(e) => setFund(e.target.value)} inputMode="decimal" className={`${inputCls} w-24 text-right`} />
                </label>
                <label className="flex items-center gap-1 pb-2 text-xs text-text-secondary">
                  <input type="checkbox" checked={updateContact} onChange={(e) => setUpdateContact(e.target.checked)} />
                  změnit kontakt domu
                </label>
                <button type="submit" disabled={busy || !owner.trim()} className={primaryBtn}>Převést dům</button>
              </div>
              {preview.suggestedMeterNote && <p className="text-xs text-text-muted">Návrh stavu vodoměru: {preview.suggestedMeterNote}</p>}
            </form>
          )}
        </div>
      )}
      {info && <p role="status" className="text-sm text-success">{info}</p>}
      {errors.length > 0 && (
        <ul role="alert" className="space-y-1 rounded-lg bg-danger-light p-2 text-sm text-danger">
          {errors.map((m) => <li key={m}>{m}</li>)}
        </ul>
      )}
    </section>
  );
}
