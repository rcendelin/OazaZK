/**
 * Marks a meter reading that is an estimate, not a physical reading (T04).
 * The tooltip says how the estimate was obtained.
 */
export function EstimateMark({ note }: { note: string | null }) {
  return (
    <span
      className="ml-0.5 cursor-help font-sans text-xs font-bold text-warning"
      title={`Odhad${note ? ` — ${note}` : ''}`}
      aria-label="odhad"
    >
      ≈
    </span>
  );
}
