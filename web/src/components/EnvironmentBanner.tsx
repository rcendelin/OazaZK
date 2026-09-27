import { useCallback } from 'react';
import { useApi } from '../hooks/useApi';
import { apiClient } from '../api/client';

type DeploymentEnvironment = 'dev' | 'test' | 'prod' | 'unknown';

function normalize(value: string | undefined | null): DeploymentEnvironment {
  switch (value?.trim().toLowerCase()) {
    case 'dev':
    case 'development':
      return 'dev';
    case 'test':
    case 'testing':
      return 'test';
    case 'prod':
    case 'production':
      return 'prod';
    default:
      return 'unknown';
  }
}

/** Build-time environment (set per workflow); a local `vite` dev server without it counts as dev. */
const buildEnvironment: DeploymentEnvironment =
  import.meta.env.VITE_ENVIRONMENT
    ? normalize(import.meta.env.VITE_ENVIRONMENT)
    : import.meta.env.DEV ? 'dev' : 'unknown';

const label: Record<DeploymentEnvironment, string> = {
  dev: 'VÝVOJOVÉ PROSTŘEDÍ',
  test: 'TESTOVACÍ PROSTŘEDÍ — data nejsou ostrá',
  prod: 'PRODUKCE',
  unknown: 'NEZNÁMÉ PROSTŘEDÍ — ověřte, že nepracujete s ostrými daty',
};

/**
 * Full-width strip shown everywhere except production, so nobody mistakes the
 * test instance for the real one. The API's own `Environment` setting wins over
 * the build-time value (it decides which data you touch); if the two disagree
 * the strip says so.
 */
export function EnvironmentBanner() {
  const { data } = useApi<{ environment: string }>(
    useCallback(() => apiClient.get<{ environment: string }>('/environment'), []),
  );
  const apiEnvironment = data ? normalize(data.environment) : null;
  const effective = apiEnvironment ?? buildEnvironment;
  const mismatch = apiEnvironment !== null && buildEnvironment !== 'unknown' && apiEnvironment !== buildEnvironment;

  if (effective === 'prod' && !mismatch) return null;

  const tone = mismatch || effective === 'unknown'
    ? 'bg-danger text-white'
    : effective === 'test'
      ? 'bg-warning text-white'
      : 'bg-accent text-white';

  return (
    <div role="status" className={`w-full px-4 py-1.5 text-center text-xs font-bold tracking-wide ${tone}`}>
      {mismatch
        ? `NESOULAD PROSTŘEDÍ — aplikace je sestavená pro „${buildEnvironment}“, ale API hlásí „${apiEnvironment}“`
        : label[effective]}
    </div>
  );
}
