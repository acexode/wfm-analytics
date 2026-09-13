export const categories = ['active_seconds', 'inactive_seconds', 'approved_non_desktop_seconds', 'conflict_seconds', 'unknown_seconds'] as const;
export type Category = typeof categories[number];
export type Employee = {
  employee_id: string; display_name: string; eligible_seconds: number;
  categories: Record<Category, number>;
  telemetry_coverage: {value: number | null; unavailable_reason: string | null};
  output: {completed_count: number | null; unavailable_reason: string | null};
};
export type DailyReport = {
  schema_version: '1.0'; synthetic: boolean; team_id: string; team_name: string;
  report_date: string; timezone: string; report_revision: number; definition_version: '1.0';
  computed_at: string; units: 'seconds';
  freshness: {status: 'fresh' | 'stale' | 'unknown'; last_received_at: string | null};
  employees: Employee[];
};
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);
const nonempty = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0;
const seconds = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
const timestamp = (v: unknown) => typeof v === 'string' && /T.*(?:Z|[+-]\d\d:\d\d)$/.test(v) && Number.isFinite(Date.parse(v));
const available = (value: unknown, reason: unknown, valid: (v: unknown) => boolean) => value === null ? nonempty(reason) : valid(value) && reason === null;
export function parseReport(value: unknown, date: string, team = 'team-synthetic'): DailyReport {
  const fail = () => { throw new Error('The report response is invalid. No totals have been displayed.'); };
  if (!object(value)) return fail();
  if (value.schema_version !== '1.0' || value.definition_version !== '1.0' || value.units !== 'seconds' || typeof value.synthetic !== 'boolean' || value.team_id !== team || value.report_date !== date || !nonempty(value.team_name) || !nonempty(value.timezone) || !seconds(value.report_revision) || value.report_revision < 1 || !timestamp(value.computed_at)) return fail();
  try { new Intl.DateTimeFormat('en', {timeZone: value.timezone}); } catch { return fail(); }
  const freshness = value.freshness;
  if (!object(freshness) || !['fresh', 'stale', 'unknown'].includes(String(freshness.status)) || (freshness.last_received_at !== null && !timestamp(freshness.last_received_at))) return fail();
  if (!Array.isArray(value.employees)) return fail();
  const ids = new Set();
  for (const employee of value.employees) {
    if (!object(employee) || !nonempty(employee.employee_id) || ids.has(employee.employee_id) || !nonempty(employee.display_name) || !seconds(employee.eligible_seconds) || !object(employee.categories) || !object(employee.telemetry_coverage) || !object(employee.output)) return fail();
    ids.add(employee.employee_id);
    let total = 0;
    for (const category of categories) { const duration = employee.categories[category]; if (!seconds(duration)) return fail(); total += duration; }
    if (total !== employee.eligible_seconds || !available(employee.telemetry_coverage.value, employee.telemetry_coverage.unavailable_reason, v => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= 1) || !available(employee.output.completed_count, employee.output.unavailable_reason, seconds)) return fail();
    if (employee.eligible_seconds === 0 && employee.telemetry_coverage.value !== null) return fail();
  }
  return value as DailyReport;
}
export const duration = (value: number) => value >= 3600 ? `${Math.floor(value / 3600)}h ${Math.floor(value % 3600 / 60)}m` : value >= 60 ? `${Math.floor(value / 60)}m ${value % 60}s` : `${value}s`;
export const percent = (value: number | null) => value === null ? 'Unavailable' : `${(value * 100).toFixed(1)}%`;
export const reason = (value: string | null) => value === 'source_not_connected' ? 'Operational output source is not connected.' : value?.replaceAll('_', ' ') ?? '';
export async function fetchReport(date: string, signal: AbortSignal): Promise<DailyReport> {
  const response = await fetch(`/api/v1/teams/team-synthetic/daily?date=${encodeURIComponent(date)}`, {signal, credentials: 'same-origin'});
  if (!response.ok) {
    const errors: Record<number, string> = {401: 'Your session is unavailable. Check the local development server.', 403: 'You do not have access to this team.', 404: 'No report is available for this date. The synthetic reference report is dated 13 September 2026.', 503: 'The report database is unavailable. Start the backend and database, then retry.'};
    throw new Error(errors[response.status] ?? `The report could not be loaded (HTTP ${response.status}).`);
  }
  return parseReport(await response.json(), date);
}
