import { StrictMode, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { categories, duration, fetchReport, percent, reason, type Category, type DailyReport } from './report.ts';
import './style.css';

const labels: Record<Category, string> = {active_seconds: 'Observed active', inactive_seconds: 'Locked / inactive', approved_non_desktop_seconds: 'Approved non-desktop', conflict_seconds: 'Conflicting evidence', unknown_seconds: 'Unknown'};
function App() {
  const [date, setDate] = useState('2026-09-13');
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<{report?: DailyReport; error?: string; loading: boolean}>({loading: true});
  useEffect(() => {
    const controller = new AbortController();
    setState({loading: true});
    fetchReport(date, controller.signal).then(report => {if (!controller.signal.aborted) setState({report, loading: false});}).catch(error => {if (!controller.signal.aborted) setState({error: error instanceof Error ? error.message : 'The report could not be loaded.', loading: false});});
    return () => controller.abort();
  }, [date, attempt]);
  const report = state.report;
  const totals = Object.fromEntries(categories.map(key => [key, report?.employees.reduce((sum, employee) => sum + employee.categories[key], 0) ?? 0])) as Record<Category, number>;
  const eligible = report?.employees.reduce((sum, employee) => sum + employee.eligible_seconds, 0) ?? 0;
  // Weight coverage by its eligible-time denominator; never average employee percentages.
  const coverage = !report || eligible === 0 || report.employees.some(e => e.eligible_seconds > 0 && e.telemetry_coverage.value === null) ? null : report.employees.reduce((sum, e) => sum + (e.telemetry_coverage.value ?? 0) * e.eligible_seconds, 0) / eligible;
  return <div className="shell">
    <a className="skip" href="#main">Skip to overview</a>
    <aside><a className="brand" href="#main"><span className="mark">W</span> workforce<span className="brand-dot">.</span></a><p className="eyebrow">OPERATIONS WORKSPACE</p><nav aria-label="Main"><a className="selected" href="#main" aria-current="page"><span aria-hidden="true">▦</span> Daily overview</a><a href="#definitions"><span aria-hidden="true">≡</span> Metric definitions</a></nav><div className="sidebar-foot"><span className="status-dot"/> Local development<p>Internal analytics foundation<br/>Version 0.1 · Synthetic environment</p></div></aside>
    <div className="workspace"><header className="topbar"><span>Operations / <strong>Daily overview</strong></span><span className="identity">DM <span>Demo manager</span></span></header>
    <main id="main"><div className="heading"><div><p className="eyebrow">DAILY OPERATIONS</p><h1>Understand the day.</h1><p className="intro">Scheduled capacity, observed evidence, and the gaps between.</p></div><label className="date">Report date<input type="date" value={date} required onChange={e => {if (e.target.value) setDate(e.target.value);}}/></label></div>
      <div className="notice"><strong>{report?.synthetic === false ? 'Live development evidence' : 'Synthetic development environment'}</strong><span>{report?.synthetic === false ? 'This view includes locally ingested collector evidence for a controlled development enrollment.' : 'This view uses reference data until local collector evidence is ingested for the selected day.'}</span></div>
      <div className="report-heading"><h2>{report?.team_name ?? 'Synthetic Operations'}</h2><button disabled={state.loading} onClick={() => setAttempt(n => n + 1)}>{state.loading ? 'Loading…' : 'Refresh report'}</button></div>
      <div aria-live="polite" aria-busy={state.loading}>
      {state.loading && <section className="panel loading" role="status"><span className="loader"/> Loading the daily report…</section>}
      {state.error && <section className="panel error" role="alert"><h3>Report unavailable</h3><p>{state.error}</p><p>Displayed totals have been cleared to avoid showing an earlier date.</p><button onClick={() => setAttempt(n => n + 1)}>Try again</button></section>}
      {report && <>
        <section className={`quality ${report.freshness.status}`}><div><p className="eyebrow">SOURCE QUALITY FIRST</p><h3>{report.freshness.status === 'fresh' ? 'Source reports fresh evidence' : report.freshness.status === 'stale' ? 'Source evidence is stale' : 'Source freshness is unknown'}</h3><p>{report.freshness.last_received_at ? `Last received ${new Date(report.freshness.last_received_at).toLocaleString('en-GB', {timeZone: report.timezone})} (${report.timezone}).` : 'No source receipt is available. Coverage describes this report only.'}</p></div><span className="badge">{report.freshness.status}</span></section>
        {report.employees.length === 0 ? <section className="panel empty"><h3>No employee data for this day</h3><p>The report contains no employee rows. No zero totals are implied.</p></section> : <>
        <div className="cards"><section className="panel"><p className="card-label">Telemetry coverage</p><strong className="stat">{percent(coverage)}</strong><p>Valid desktop evidence / eligible time</p></section><section className="panel"><p className="card-label">Scheduled eligible time</p><strong className="stat">{duration(eligible)}</strong><p>Across {report.employees.length} employee{report.employees.length === 1 ? '' : 's'} in this report</p></section><section className="panel"><p className="card-label">Unknown time</p><strong className="stat">{duration(totals.unknown_seconds)}</strong><p>Missing evidence requiring context</p></section></div>
        <section className="panel breakdown"><div className="section-title"><h3>How eligible time is explained</h3><span>Five exclusive categories</span></div><div className="bar" aria-hidden="true">{categories.map(key => <span key={key} className={key} style={{width: `${eligible ? totals[key] / eligible * 100 : 0}%`}}/>)}</div><div className="legend">{categories.map(key => <div key={key}><span className={`swatch ${key}`}/><span>{labels[key]}</span><strong>{duration(totals[key])}</strong></div>)}</div><p className="footnote">Categories reconcile to scheduled eligible time. Desktop activity does not establish output, effort, or billable hours.</p></section>
        <section className="panel employees"><div className="section-title"><h3>Employee evidence</h3><span>{report.employees.length} in report</span></div><div className="table-wrap"><table><caption className="sr-only">Employee daily evidence and operational output</caption><thead><tr><th scope="col">Employee</th><th scope="col">Eligible</th><th scope="col">Coverage</th><th scope="col">Active</th><th scope="col">Unknown</th><th scope="col">Conflict</th><th scope="col">Completed work</th></tr></thead><tbody>{report.employees.map(employee => <tr key={employee.employee_id}><th scope="row">{employee.display_name}<small>{employee.employee_id}</small></th><td>{duration(employee.eligible_seconds)}</td><td>{percent(employee.telemetry_coverage.value)}{employee.telemetry_coverage.value === null && <small>{reason(employee.telemetry_coverage.unavailable_reason)}</small>}</td><td>{duration(employee.categories.active_seconds)}</td><td>{duration(employee.categories.unknown_seconds)}</td><td>{duration(employee.categories.conflict_seconds)}</td><td>{employee.output.completed_count === null ? <><span className="unavailable">Unavailable</span><small>{reason(employee.output.unavailable_reason)}</small></> : employee.output.completed_count}</td></tr>)}</tbody></table></div></section></>}
        <p className="metadata">Report revision {report.report_revision} · Definition {report.definition_version} · {report.timezone} · Computed {new Date(report.computed_at).toLocaleString('en-GB', {timeZone: report.timezone})}</p>
      </>}
      </div><section id="definitions" className="definitions"><h3>Read the evidence with context</h3><p><strong>Coverage</strong> measures valid observed desktop intervals within eligible time. Annotations do not increase it. <strong>Unknown</strong> preserves missing evidence; <strong>conflict</strong> preserves unresolved overlaps. Completed work requires an authoritative operational source. These activity categories are not productivity scores.</p></section>
    </main><footer>Workforce Operations Analytics <span>Internal · Development foundation</span></footer></div>
  </div>;
}
createRoot(document.getElementById('root')!).render(<StrictMode><App/></StrictMode>);
