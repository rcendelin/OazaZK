import type { ReactNode } from 'react';

interface MetricCardProps {
  title: string;
  value: string;
  subtitle?: string;
  icon?: ReactNode;
  trend?: 'up' | 'down' | 'neutral';
}

const trendConfig = {
  up: { color: 'text-danger', bg: 'bg-danger-light', arrow: '\u2191' },
  down: { color: 'text-success', bg: 'bg-success-light', arrow: '\u2193' },
  neutral: { color: 'text-text-muted', bg: 'bg-surface-sunken', arrow: '\u2192' },
};

export function MetricCard({
  title,
  value,
  subtitle,
  icon,
  trend,
}: MetricCardProps) {
  return (
    <div className="group rounded-2xl bg-surface-raised p-5 shadow-card transition-shadow duration-200 hover:shadow-card-hover">
      <div className="flex items-start justify-between">
        <div className="min-w-0 flex-1">
          <p className="text-[13px] font-medium text-text-muted">{title}</p>
          <div className="mt-2 flex items-baseline gap-2">
            <p className="text-2xl font-bold tracking-tight text-text-primary">{value}</p>
            {trend && (
              <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${trendConfig[trend].bg} ${trendConfig[trend].color}`}>
                {trendConfig[trend].arrow}
              </span>
            )}
          </div>
          {subtitle && (
            <p className="mt-1.5 text-[13px] text-text-muted">{subtitle}</p>
          )}
        </div>
        {icon && (
          <div className="ml-3 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-accent-light text-accent">
            {icon}
          </div>
        )}
      </div>
    </div>
  );
}
