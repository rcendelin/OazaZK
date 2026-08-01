import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { guides, terms, splitBold } from '../content/help';
import type { TermId } from '../content/help';

export function JakToFungujePage() {
  const { hash } = useLocation();
  const termIds = Object.keys(terms) as TermId[];

  // React Router na kotvu sám neskroluje — bez tohoto by odkaz „?" z tabulek
  // otevřel stránku na začátku místo u pojmu.
  useEffect(() => {
    if (!hash) return;
    document.getElementById(hash.slice(1))?.scrollIntoView({ behavior: 'smooth' });
  }, [hash]);

  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Jak to funguje</h1>
        <p className="mt-1 text-sm text-text-secondary">
          Výklad toho, co aplikace dělá a jak počítá čísla, která vidíte na obrazovkách.
        </p>
      </div>

      <nav className="rounded-2xl border border-border bg-surface-sunken p-4">
        <ul className="space-y-1 text-sm">
          {guides.map((g) => (
            <li key={g.id}>
              <a href={`#${g.id}`} className="text-accent hover:underline">{g.title}</a>
            </li>
          ))}
          <li><a href="#slovnik" className="text-accent hover:underline">Slovník pojmů</a></li>
        </ul>
      </nav>

      {guides.map((g) => (
        <section key={g.id} id={g.id} className="scroll-mt-8">
          <h2 className="text-lg font-semibold text-text-primary">{g.title}</h2>
          <div className="mt-2 space-y-2 text-sm text-text-secondary">
            {g.body.flatMap((para, i) =>
              para.split('\n\n').map((p, j) => (
                <p key={`${i}-${j}`}>
                  {splitBold(p).map((segment, k) =>
                    segment.bold ? <strong key={k}>{segment.text}</strong> : <span key={k}>{segment.text}</span>,
                  )}
                </p>
              )),
            )}
          </div>
        </section>
      ))}

      <section id="slovnik" className="scroll-mt-8">
        <h2 className="text-lg font-semibold text-text-primary">Slovník pojmů</h2>
        <dl className="mt-2 space-y-3">
          {termIds.map((id) => (
            <div key={id} id={id} className="scroll-mt-8">
              <dt className="text-sm font-semibold text-text-primary">{terms[id].label}</dt>
              <dd className="mt-1 space-y-2 text-sm text-text-secondary">
                {(terms[id].long ?? terms[id].short).split('\n\n').map((p, i) => (
                  <p key={i}>
                    {splitBold(p).map((segment, j) =>
                      segment.bold ? <strong key={j}>{segment.text}</strong> : <span key={j}>{segment.text}</span>,
                    )}
                  </p>
                ))}
              </dd>
            </div>
          ))}
        </dl>
      </section>
    </div>
  );
}
