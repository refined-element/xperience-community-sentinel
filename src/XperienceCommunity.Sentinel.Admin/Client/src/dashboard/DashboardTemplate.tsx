import React, { useState } from 'react';
import { usePageCommand } from '@kentico/xperience-admin-base';
import { Button, ButtonColor, ButtonSize, ButtonType } from '@kentico/xperience-admin-components';

// Keep this file server-contract-shaped: the exported interfaces mirror
// SentinelDashboardPage.DashboardClientProperties on the C# side. If you add a
// field server-side, mirror it here and recompile both halves.

interface ScanSummary {
    readonly runId: number;
    readonly startedAt: string;
    readonly status: string;
    readonly trigger: string;
    readonly totalFindings: number;
    readonly errorCount: number;
    readonly warningCount: number;
    readonly infoCount: number;
    readonly durationSeconds: number;
    readonly sentinelVersion: string;
}

interface RuleCount {
    readonly ruleId: string;
    readonly category: string;
    readonly totalCount: number;
    readonly activeCount: number;
    readonly remediationTitle: string | null;
    readonly remediationSummary: string | null;
}

interface TrendPoint {
    readonly date: string;
    readonly errors: number;
    readonly warnings: number;
    readonly info: number;
}

interface DashboardClientProperties {
    readonly hasScans: boolean;
    readonly latestScan: ScanSummary | null;
    readonly previousScan: ScanSummary | null;
    readonly recentScans: ReadonlyArray<ScanSummary>;
    readonly topRules: ReadonlyArray<RuleCount>;
    readonly trend: ReadonlyArray<TrendPoint>;
    readonly scheduledTasksUrl: string;
    readonly findingsUrl: string;
    readonly scanHistoryUrl: string;
}

interface DashboardRefreshResult {
    readonly data: DashboardClientProperties;
}

interface RunNowResult {
    readonly success: boolean;
    readonly scanRunId: number;
    readonly totalFindings: number;
    readonly message: string;
}

// Palette — Refined Element lime (#D6F08D) as the accent, muted neutrals for the rest so the
// admin shell's chrome stays visually dominant. Error red / warning orange / info grey are
// Kentico-adjacent so the dashboard reads at a glance without a legend.
import { THEME_COLORS as COLORS } from '../theme';

export const DashboardTemplate = (initial: DashboardClientProperties) => {
    const [data, setData] = useState<DashboardClientProperties>(initial);
    const [runNowFeedback, setRunNowFeedback] = useState<{ tone: 'success' | 'error'; text: string } | null>(null);

    // Kentico's Command<T> only exposes `execute` — no built-in pending flag. Track in-flight
    // state locally via useState; wrap execute in handlers that toggle around the call so the
    // buttons disable each other and the "…ing" labels work.
    const [isRefreshing, setIsRefreshing] = useState(false);
    const [isRunning, setIsRunning] = useState(false);

    const { execute: refreshCmd } = usePageCommand<DashboardRefreshResult>('GetDashboardData', {
        after: (result) => {
            setIsRefreshing(false);
            if (result?.data) {
                setData(result.data);
            }
        },
    });
    const refresh = () => { setIsRefreshing(true); refreshCmd(); };

    const { execute: runNowCmd } = usePageCommand<RunNowResult>('RunScanNow', {
        after: (result) => {
            setIsRunning(false);
            if (!result) return;
            setRunNowFeedback({
                tone: result.success ? 'success' : 'error',
                text: result.message,
            });
            // Auto-refresh the dashboard when a manual scan succeeded so the new scan-row
            // lands at the top of the "Recent scans" list without a page reload.
            if (result.success) {
                refresh();
            }
        },
    });
    const runNow = () => { setIsRunning(true); runNowCmd(); };

    if (!data.hasScans) {
        return <EmptyState scheduledTasksUrl={data.scheduledTasksUrl} onRunNow={() => runNow()} isRunning={isRunning} runNowFeedback={runNowFeedback} />;
    }

    return (
        <div style={{ padding: '24px 32px', maxWidth: 1200, margin: '0 auto' }}>
            <header style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 24, gap: 12, flexWrap: 'wrap' }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: 24, fontWeight: 600, color: COLORS.textPrimary }}>
                        Sentinel dashboard
                    </h1>
                    <p style={{ margin: '4px 0 0', color: COLORS.textMuted, fontSize: 14 }}>
                        {data.latestScan ? `Last scan: #${data.latestScan.runId} · ${formatRelative(data.latestScan.startedAt)}` : 'No scans yet.'}
                    </p>
                </div>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                    <Button
                        type={ButtonType.Button}
                        label={isRunning ? 'Running…' : 'Run scan now'}
                        onClick={() => { setRunNowFeedback(null); runNow(); }}
                        size={ButtonSize.S}
                        color={ButtonColor.Primary}
                        disabled={isRunning || isRefreshing}
                    />
                    <Button
                        type={ButtonType.Button}
                        label={isRefreshing ? 'Refreshing…' : 'Refresh'}
                        onClick={() => refresh()}
                        size={ButtonSize.S}
                        color={ButtonColor.Secondary}
                        disabled={isRefreshing || isRunning}
                    />
                </div>
            </header>

            {runNowFeedback && <FeedbackBanner tone={runNowFeedback.tone} text={runNowFeedback.text} onDismiss={() => setRunNowFeedback(null)} />}

            <KpiRow scan={data.latestScan} previous={data.previousScan} />

            <Panel title="30-day severity trend" style={{ marginTop: 24 }}>
                <TrendChart trend={data.trend} />
            </Panel>

            <div style={{ display: 'grid', gridTemplateColumns: '3fr 2fr', gap: 24, marginTop: 24 }}>
                <Panel title="Recent scans" linkText="View all" linkHref={data.scanHistoryUrl}>
                    <RecentScansList scans={data.recentScans} />
                </Panel>
                <Panel title="Top rule offenders" linkText="View findings" linkHref={data.findingsUrl}>
                    <TopRulesList rules={data.topRules} />
                </Panel>
            </div>

            <footer style={{ marginTop: 24, padding: 16, background: COLORS.bgMuted, borderRadius: 8, fontSize: 14, color: COLORS.textMuted }}>
                Cadence is configured in <a href={data.scheduledTasksUrl} style={{ color: COLORS.limeText, fontWeight: 600 }}>Scheduled tasks</a>.
                Sentinel runs on whatever interval you set there — edit the row named <code>XperienceCommunity.SentinelScan</code> to change it or
                click <em>Run scan now</em> above for an on-demand run.
            </footer>
        </div>
    );
};

const EmptyState = ({
    scheduledTasksUrl,
    onRunNow,
    isRunning,
    runNowFeedback,
}: {
    scheduledTasksUrl: string;
    onRunNow: () => void;
    isRunning: boolean;
    runNowFeedback: { tone: 'success' | 'error'; text: string } | null;
}) => (
    <div style={{ padding: 48, maxWidth: 640, margin: '64px auto', textAlign: 'center' }}>
        <div style={{ fontSize: 48, marginBottom: 16, opacity: 0.4 }}>◎</div>
        <h2 style={{ margin: '0 0 12px', fontSize: 20, color: COLORS.textPrimary }}>No scans recorded yet</h2>
        <p style={{ margin: '0 0 24px', color: COLORS.textMuted, fontSize: 14, lineHeight: 1.6 }}>
            Sentinel's tables are provisioned and the scheduled task is registered. Run your first scan
            right now, or configure a cadence in Scheduled tasks and wait for the next tick.
        </p>
        <div style={{ display: 'flex', gap: 12, justifyContent: 'center' }}>
            <Button
                type={ButtonType.Button}
                label={isRunning ? 'Running first scan…' : 'Run scan now'}
                onClick={onRunNow}
                size={ButtonSize.M}
                color={ButtonColor.Primary}
                disabled={isRunning}
            />
            <a
                href={scheduledTasksUrl}
                style={{
                    display: 'inline-block',
                    padding: '10px 20px',
                    background: COLORS.bg,
                    color: COLORS.textPrimary,
                    border: `1px solid ${COLORS.border}`,
                    borderRadius: 6,
                    textDecoration: 'none',
                    fontWeight: 600,
                    fontSize: 14,
                }}
            >
                Open Scheduled tasks
            </a>
        </div>
        {runNowFeedback && (
            <div style={{ marginTop: 20, fontSize: 14, color: runNowFeedback.tone === 'success' ? COLORS.success : COLORS.error }}>
                {runNowFeedback.text}
            </div>
        )}
    </div>
);

const FeedbackBanner = ({ tone, text, onDismiss }: { tone: 'success' | 'error'; text: string; onDismiss: () => void }) => (
    <div
        style={{
            padding: '10px 16px',
            marginBottom: 16,
            background: tone === 'success' ? '#F0FDF4' : '#FEF2F2',
            border: `1px solid ${tone === 'success' ? COLORS.success : COLORS.error}`,
            borderRadius: 8,
            color: tone === 'success' ? '#14532D' : '#7F1D1D',
            fontSize: 14,
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: 12,
        }}
    >
        <span>{text}</span>
        <button
            type="button"
            onClick={onDismiss}
            style={{ background: 'transparent', border: 'none', color: 'inherit', cursor: 'pointer', fontSize: 16, lineHeight: 1, padding: 0 }}
            aria-label="Dismiss"
        >
            ×
        </button>
    </div>
);

const KpiRow = ({ scan, previous }: { scan: ScanSummary | null; previous: ScanSummary | null }) => {
    if (!scan) return null;
    // For severity tiles (Errors/Warnings/Info) a DECREASE is a good thing, so we invert the
    // delta tone — dropping errors should read green, adding errors red. Total is tone-neutral
    // since "more findings" could mean the scanner got smarter, not the site got worse.
    return (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16 }}>
            <KpiTile label="Total findings" value={scan.totalFindings} delta={delta(scan.totalFindings, previous?.totalFindings)} />
            <KpiTile label="Errors" value={scan.errorCount} color={scan.errorCount > 0 ? COLORS.error : COLORS.info} delta={delta(scan.errorCount, previous?.errorCount)} deltaInvertTone />
            <KpiTile label="Warnings" value={scan.warningCount} color={scan.warningCount > 0 ? COLORS.warning : COLORS.info} delta={delta(scan.warningCount, previous?.warningCount)} deltaInvertTone />
            <KpiTile label="Info" value={scan.infoCount} color={COLORS.info} delta={delta(scan.infoCount, previous?.infoCount)} deltaInvertTone />
        </div>
    );
};

const delta = (current: number, previous: number | undefined): number | null =>
    previous === undefined || previous === null ? null : current - previous;

const KpiTile = ({
    label,
    value,
    color = COLORS.textPrimary,
    delta,
    deltaInvertTone,
}: {
    label: string;
    value: number;
    color?: string;
    delta?: number | null;
    deltaInvertTone?: boolean;
}) => {
    // Hide the chip entirely when there's no previous scan (delta=null) OR no change (delta=0).
    // Rendering "±0" adds visual noise without conveying anything — an absent chip says
    // "nothing to highlight here" more clearly than a muted zero.
    const showDelta = delta !== null && delta !== undefined && delta !== 0;
    // Severity metrics (deltaInvertTone=true): decrease is good (green), increase is bad (red).
    // Non-severity metrics (Total findings, etc.) stay neutral — the direction arrow alone
    // carries the signal without loading color semantics onto it.
    const deltaColor = !showDelta || !deltaInvertTone
        ? COLORS.textMuted
        : delta! < 0
            ? COLORS.success
            : COLORS.error;
    const sign = !showDelta ? '' : delta! > 0 ? '+' : '−';
    return (
        <div
            style={{
                padding: 20,
                background: COLORS.bg,
                border: `1px solid ${COLORS.border}`,
                borderRadius: 10,
                borderLeft: `4px solid ${color}`,
            }}
        >
            <div style={{ fontSize: 14, color: COLORS.textMuted, marginBottom: 6 }}>{label}</div>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
                <div style={{ fontSize: 32, fontWeight: 700, color, lineHeight: 1 }}>{value.toLocaleString()}</div>
                {showDelta && (
                    <div
                        style={{ fontSize: 14, color: deltaColor, fontWeight: 600 }}
                        title="Change since the previous scan"
                    >
                        {sign}{Math.abs(delta!).toLocaleString()}
                    </div>
                )}
            </div>
        </div>
    );
};

const Panel = ({
    title,
    linkText,
    linkHref,
    children,
    style,
}: {
    title: string;
    linkText?: string;
    linkHref?: string;
    children: React.ReactNode;
    style?: React.CSSProperties;
}) => (
    <section
        style={{
            background: COLORS.bg,
            border: `1px solid ${COLORS.border}`,
            borderRadius: 10,
            overflow: 'hidden',
            ...style,
        }}
    >
        <header
            style={{
                padding: '14px 20px',
                borderBottom: `1px solid ${COLORS.border}`,
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                background: COLORS.bgMuted,
            }}
        >
            <h3 style={{ margin: 0, fontSize: 14, fontWeight: 600, color: COLORS.textPrimary, textTransform: 'uppercase', letterSpacing: 0.3 }}>
                {title}
            </h3>
            {linkText && linkHref && (
                <a href={linkHref} style={{ fontSize: 14, color: COLORS.limeText, textDecoration: 'none', fontWeight: 600 }}>
                    {linkText} →
                </a>
            )}
        </header>
        <div>{children}</div>
    </section>
);

// 30-day stacked-area-ish sparkline, hand-rolled SVG so we don't depend on a chart library and
// the bundle stays sub-100KB. Each day stacks error on top of warning on top of info — operators
// can eyeball when severity spiked and whether it's composed of noisy infos or real errors.
const TrendChart = ({ trend }: { trend: ReadonlyArray<TrendPoint> }) => {
    if (trend.length === 0) {
        return <div style={{ padding: 20, color: COLORS.textMuted, fontSize: 14 }}>No scan history in the last 30 days.</div>;
    }
    const maxTotal = Math.max(1, ...trend.map((p) => p.errors + p.warnings + p.info));
    const width = 1000; // internal SVG coordinate — scales via viewBox / width:100%
    const height = 120;
    const padding = { top: 8, right: 8, bottom: 24, left: 8 };
    const innerWidth = width - padding.left - padding.right;
    const innerHeight = height - padding.top - padding.bottom;
    const step = trend.length > 1 ? innerWidth / (trend.length - 1) : innerWidth;

    const project = (p: TrendPoint, stackAbove: number) => {
        const value = stackAbove;
        return innerHeight - (value / maxTotal) * innerHeight + padding.top;
    };

    const buildArea = (valueOf: (p: TrendPoint) => number, stackBelowOf: (p: TrendPoint) => number) => {
        const top = trend.map((p, i) => `${padding.left + i * step},${project(p, stackBelowOf(p) + valueOf(p))}`).join(' L ');
        const bottom = [...trend]
            .reverse()
            .map((p, idx) => {
                const i = trend.length - 1 - idx;
                return `${padding.left + i * step},${project(p, stackBelowOf(p))}`;
            })
            .join(' L ');
        return `M ${top} L ${bottom} Z`;
    };

    const infoArea = buildArea((p) => p.info, () => 0);
    const warningArea = buildArea((p) => p.warnings, (p) => p.info);
    const errorArea = buildArea((p) => p.errors, (p) => p.info + p.warnings);

    // Axis labels — just the oldest and newest dates, plus a midpoint, to keep the sparkline
    // uncluttered. Operators who want precise dates can hover a scan row in the list below.
    const firstLabel = trend[0]?.date ?? '';
    const lastLabel = trend[trend.length - 1]?.date ?? '';
    const midLabel = trend[Math.floor(trend.length / 2)]?.date ?? '';

    return (
        <div style={{ padding: '16px 20px' }}>
            <svg viewBox={`0 0 ${width} ${height}`} style={{ width: '100%', height: 'auto', display: 'block' }} role="img" aria-label="30-day severity trend">
                <path d={infoArea} fill={COLORS.info} opacity={0.25} />
                <path d={warningArea} fill={COLORS.warning} opacity={0.55} />
                <path d={errorArea} fill={COLORS.error} opacity={0.85} />
                {/* X-axis tick labels */}
                <text x={padding.left} y={height - 6} fontSize="10" fill={COLORS.textMuted}>{firstLabel}</text>
                <text x={width / 2} y={height - 6} fontSize="10" fill={COLORS.textMuted} textAnchor="middle">{midLabel}</text>
                <text x={width - padding.right} y={height - 6} fontSize="10" fill={COLORS.textMuted} textAnchor="end">{lastLabel}</text>
            </svg>
            <div style={{ display: 'flex', gap: 16, justifyContent: 'center', marginTop: 8, fontSize: 11, color: COLORS.textMuted }}>
                <LegendDot color={COLORS.error} label="Errors" />
                <LegendDot color={COLORS.warning} label="Warnings" />
                <LegendDot color={COLORS.info} label="Info" />
            </div>
        </div>
    );
};

const LegendDot = ({ color, label }: { color: string; label: string }) => (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
        <span style={{ display: 'inline-block', width: 8, height: 8, borderRadius: 2, background: color }} />
        {label}
    </span>
);

const RecentScansList = ({ scans }: { scans: ReadonlyArray<ScanSummary> }) => {
    if (scans.length === 0) {
        return <div style={{ padding: 20, color: COLORS.textMuted, fontSize: 14 }}>No recent scans.</div>;
    }
    return (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {scans.map((s, i) => (
                <li
                    key={s.runId}
                    style={{
                        padding: '12px 20px',
                        borderBottom: i < scans.length - 1 ? `1px solid ${COLORS.border}` : 'none',
                        display: 'grid',
                        gridTemplateColumns: '60px 1fr auto',
                        gap: 16,
                        alignItems: 'center',
                        fontSize: 14,
                    }}
                >
                    <span style={{ color: COLORS.textMuted, fontFamily: 'monospace' }}>#{s.runId}</span>
                    <div>
                        <div style={{ color: COLORS.textPrimary, fontWeight: 500 }}>
                            {formatRelative(s.startedAt)} · {s.trigger}
                        </div>
                        <div style={{ color: COLORS.textMuted, fontSize: 12 }}>
                            {s.durationSeconds.toFixed(1)}s · {s.status} · v{s.sentinelVersion}
                        </div>
                    </div>
                    <SeverityPills scan={s} />
                </li>
            ))}
        </ul>
    );
};

const SeverityPills = ({ scan }: { scan: ScanSummary }) => (
    <div style={{ display: 'flex', gap: 6 }}>
        {scan.errorCount > 0 && <Pill count={scan.errorCount} color={COLORS.error} label="E" />}
        {scan.warningCount > 0 && <Pill count={scan.warningCount} color={COLORS.warning} label="W" />}
        {scan.infoCount > 0 && <Pill count={scan.infoCount} color={COLORS.info} label="I" />}
        {scan.totalFindings === 0 && <Pill count={0} color={COLORS.success} label="✓" />}
    </div>
);

const Pill = ({ count, color, label }: { count: number; color: string; label: string }) => (
    <span
        style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: 3,
            padding: '2px 8px',
            fontSize: 12,
            fontWeight: 600,
            color: '#FFF',
            background: color,
            borderRadius: 10,
            minWidth: 28,
            justifyContent: 'center',
        }}
    >
        {count > 0 ? count : ''}
        {label}
    </span>
);

const TopRulesList = ({ rules }: { rules: ReadonlyArray<RuleCount> }) => {
    const [expanded, setExpanded] = useState<string | null>(null);

    if (rules.length === 0) {
        return <div style={{ padding: 20, color: COLORS.textMuted, fontSize: 14 }}>No findings across recent scans.</div>;
    }
    const max = Math.max(...rules.map((r) => r.totalCount));
    return (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {rules.map((r, i) => {
                const key = `${r.ruleId}::${r.category}`;
                const isOpen = expanded === key;
                const suppressed = r.totalCount - r.activeCount;
                return (
                    <li
                        key={key}
                        style={{
                            borderBottom: i < rules.length - 1 ? `1px solid ${COLORS.border}` : 'none',
                            fontSize: 14,
                        }}
                    >
                        <button
                            type="button"
                            onClick={() => setExpanded(isOpen ? null : key)}
                            style={{
                                width: '100%',
                                padding: '12px 20px',
                                background: 'transparent',
                                border: 'none',
                                textAlign: 'left',
                                cursor: 'pointer',
                                color: 'inherit',
                                fontSize: 'inherit',
                                fontFamily: 'inherit',
                            }}
                            aria-expanded={isOpen}
                        >
                            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4, alignItems: 'baseline' }}>
                                <span style={{ fontFamily: 'monospace', color: COLORS.textPrimary, fontWeight: 600 }}>
                                    <span style={{ color: COLORS.limeText, marginRight: 6 }}>{isOpen ? '▾' : '▸'}</span>
                                    {r.ruleId}
                                </span>
                                <span style={{ color: COLORS.textMuted, fontSize: 12 }}>
                                    {r.activeCount}
                                    {suppressed > 0 && <span style={{ color: COLORS.textMuted }}> ({suppressed} acked)</span>}
                                    {' · '}
                                    {r.category}
                                </span>
                            </div>
                            <div style={{ background: COLORS.bgMuted, height: 4, borderRadius: 2, overflow: 'hidden' }}>
                                <div
                                    style={{
                                        width: `${Math.round((r.totalCount / max) * 100)}%`,
                                        height: '100%',
                                        background: r.activeCount === 0 ? COLORS.success : COLORS.lime,
                                    }}
                                />
                            </div>
                        </button>
                        {isOpen && (
                            <div style={{ padding: '0 20px 16px', fontSize: 14, color: COLORS.textMuted, lineHeight: 1.55 }}>
                                {r.remediationTitle && (
                                    <>
                                        <div style={{ fontWeight: 600, color: COLORS.textPrimary, marginBottom: 4 }}>{r.remediationTitle}</div>
                                        <div style={{ marginBottom: 8 }}>{r.remediationSummary}</div>
                                    </>
                                )}
                                <a
                                    href={`/admin/sentinel/scan-detail?rule=${encodeURIComponent(r.ruleId)}`}
                                    style={{ color: COLORS.limeText, fontSize: 12, fontWeight: 600, textDecoration: 'none' }}
                                >
                                    View {r.activeCount} active finding{r.activeCount === 1 ? '' : 's'} across recent scans →
                                </a>
                            </div>
                        )}
                    </li>
                );
            })}
        </ul>
    );
};

// Relative time is friendlier than absolute for the recency cases operators actually care about.
// For anything older than a week we fall back to the locale date so the label stays informative.
const formatRelative = (iso: string): string => {
    const then = new Date(iso);
    const now = new Date();
    const diffMs = now.getTime() - then.getTime();
    const diffMin = Math.round(diffMs / 60000);
    if (diffMin < 1) return 'just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.round(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    const diffDay = Math.round(diffHr / 24);
    if (diffDay < 7) return `${diffDay}d ago`;
    return then.toLocaleDateString();
};
