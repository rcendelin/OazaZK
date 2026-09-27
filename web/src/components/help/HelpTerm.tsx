import { Link } from 'react-router-dom';
import type { TermId } from '../../content/help';
import { terms } from '../../content/help';

interface HelpTermProps {
  id: TermId;
}

export function HelpTerm({ id }: HelpTermProps) {
  const term = terms[id];

  return (
    <Link
      to={`/jak-to-funguje#${id}`}
      title={term.short}
      aria-label={`Nápověda k pojmu ${term.label}`}
      className="ml-1 inline-block rounded-full border border-border px-1.5 align-middle text-xs font-normal normal-case tracking-normal text-text-muted hover:border-accent hover:text-accent"
    >
      ?
    </Link>
  );
}
