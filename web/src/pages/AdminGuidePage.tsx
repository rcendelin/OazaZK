import { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { adminProcedures } from '../content/adminGuide';

/** Screenshot of a step; a missing file just disappears instead of showing a broken image. */
function GuideImage({ file, alt }: { file: string; alt: string }) {
  const [failed, setFailed] = useState(false);
  if (failed) return null;
  return (
    <img
      src={`/navod/${file}`}
      alt={alt}
      loading="lazy"
      onError={() => setFailed(true)}
      className="mt-2 max-w-full rounded-xl border border-border shadow-card"
    />
  );
}

/** „Návod pro správce“ (T12): step-by-step procedures with screenshots; content in `content/adminGuide.ts`. */
export function AdminGuidePage() {
  const { hash } = useLocation();

  // React Router does not scroll to an anchor by itself.
  useEffect(() => {
    if (!hash) return;
    document.getElementById(hash.slice(1))?.scrollIntoView({ behavior: 'smooth' });
  }, [hash]);

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Návod pro správce</h1>
        <p className="mt-1 max-w-3xl text-sm text-text-secondary">
          Nejčastější postupy krok za krokem. Názvy tlačítek a polí v uvozovkách jsou přesně tak, jak je uvidíte na
          obrazovce. Obrázky jsou z ukázkových dat. Zapisovat může jen správce; účetní si postupy může přečíst.
        </p>
      </div>

      <nav aria-label="Obsah návodu" className="rounded-2xl border border-border bg-surface-sunken p-4">
        <ol className="list-decimal space-y-1 pl-5 text-sm">
          {adminProcedures.map((p) => (
            <li key={p.id}>
              <a href={`#${p.id}`} className="text-accent hover:underline">{p.title}</a>
            </li>
          ))}
        </ol>
      </nav>

      {adminProcedures.map((p, index) => (
        <section key={p.id} id={p.id} aria-labelledby={`${p.id}-title`} className="scroll-mt-8 space-y-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h2 id={`${p.id}-title`} className="text-lg font-semibold text-text-primary">
              {index + 1}. {p.title}
            </h2>
            <Link to={p.path} className="text-sm font-medium text-accent hover:text-accent-hover">
              Otevřít stránku →
            </Link>
          </div>
          <p className="max-w-3xl text-sm text-text-secondary">{p.intro}</p>
          <ol className="space-y-5">
            {p.steps.map((step, i) => (
              <li key={i} className="flex gap-3">
                <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent text-sm font-semibold text-white">
                  {i + 1}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="max-w-3xl pt-0.5 text-sm text-text-primary">{step.text}</p>
                  {step.screenshot && <GuideImage file={step.screenshot} alt={`${p.title} — krok ${i + 1}`} />}
                </div>
              </li>
            ))}
          </ol>
        </section>
      ))}
    </div>
  );
}
