import type { SectionId } from '../../content/help';
import { sections, splitBold } from '../../content/help';

interface HelpDisclosureProps {
  sectionId: SectionId;
}

export function HelpDisclosure({ sectionId }: HelpDisclosureProps) {
  const section = sections[sectionId];
  if (!section.disclosure) return null;

  return (
    <details className="mt-2 rounded-xl border border-border bg-surface-sunken px-3 py-2">
      <summary className="cursor-pointer text-sm font-medium text-accent">
        {section.disclosureTitle ?? 'Jak to funguje'}
      </summary>
      <div className="mt-2 space-y-2 text-sm text-text-secondary">
        {section.disclosure.split('\n\n').map((para, i) => (
          <p key={i}>
            {splitBold(para).map((segment, j) =>
              segment.bold ? <strong key={j}>{segment.text}</strong> : <span key={j}>{segment.text}</span>,
            )}
          </p>
        ))}
      </div>
    </details>
  );
}
