import type { SectionId } from '../../content/help';
import { sections } from '../../content/help';

interface HelpNoteProps {
  sectionId: SectionId;
}

export function HelpNote({ sectionId }: HelpNoteProps) {
  const note = sections[sectionId].note;
  if (!note) return null;

  return <p className="mt-1 text-sm text-text-secondary">{note}</p>;
}
