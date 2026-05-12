import React, { useMemo, useState } from 'react';
import { usePageCommand } from '@kentico/xperience-admin-base';
import { Button, ButtonColor, ButtonSize, ButtonType } from '@kentico/xperience-admin-components';

interface ScanOption {
    readonly runId: number;
    readonly label: string;
    readonly findingsCount: number;
}

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

interface FindingDetail {
    readonly findingId: number;
    readonly fingerprint: string;
    readonly ruleId: string;
    readonly ruleTitle: string;
    readonly category: string;
    readonly severity: string;
    readonly message: string;
    readonly location: string | null;
    readonly remediation: string | null;
    readonly quoteEligible: boolean;
    readonly ackState: string;
    readonly snoozeUntilUtc: string | null;
    readonly ackNote: string | null;
    readonly remediationTitle: string | null;
    readonly remediationSummary: string | null;
    readonly remediationSteps: string | null;
    readonly scanOccurrences: number;
    readonly firstSeenUtc: string | null;
}

interface ScanDetail {
    readonly run: ScanSummary | null;
    readonly findings: ReadonlyArray<FindingDetail>;
}

interface ScanDetailClientProperties {
    readonly availableScans: ReadonlyArray<ScanOption>;
    readonly detail: ScanDetail;
    readonly currentUserId: number;
}

interface ScanDetailResponse {
    readonly detail: ScanDetail;
}

interface AckMutationResponse {
    readonly success: boolean;
    readonly message: string;
    readonly fingerprint: string;
    readonly newState: string;
    readonly snoozeUntilUtc: string | null;
    readonly note: string | null;
}

interface BulkAckUpdate {
    readonly fingerprint: string;
    readonly newState: string;
    readonly snoozeUntilUtc: string | null;
    readonly note: string | null;
}

interface BulkAckResponse {
    readonly success: boolean;
    readonly message: string;
    readonly affectedCount: number;
    readonly requestedCount: number;
    readonly updates: ReadonlyArray<BulkAckUpdate>;
}

interface ExportFindingsResponse {
    readonly success: boolean;
    readonly message: string;
    readonly content: string;
    readonly mimeType: string;
    readonly fileName: string;
}

import { THEME_COLORS as COLORS } from '../theme';

const severityColor = (severity: string) =>
    severity === 'Error' ? COLORS.error : severity === 'Warning' ? COLORS.warning : COLORS.info;

export const ScanDetailTemplate = (initial: ScanDetailClientProperties) => {
    const [detail, setDetail] = useState<ScanDetail>(initial.detail);
    const [selectedRunId, setSelectedRunId] = useState<number | null>(initial.detail.run?.runId ?? null);
    const [ackStates, setAckStates] = useState<Record<string, { ackState: string; snoozeUntilUtc: string | null; ackNote: string | null }>>(() =>
        Object.fromEntries(
            initial.detail.findings.map((f) => [
                f.fingerprint,
                { ackState: f.ackState, snoozeUntilUtc: f.snoozeUntilUtc, ackNote: f.ackNote },
            ]),
        ),
    );
    const [filter, setFilter] = useState<'all' | 'active' | 'acked' | 'snoozed'>('active');
    const [feedback, setFeedback] = useState<string | null>(null);
    // Free-text search across rule ID / category / message / location. Seeded from the
    // `?rule=` query param so Dashboard top-rule clicks deep-link into the filtered view.
    const [search, setSearch] = useState<string>(() => {
        if (typeof window === 'undefined') return '';
        const params = new URLSearchParams(window.location.search);
        return params.get('rule')?.trim() ?? '';
    });

    // Kentico's Command<T> only exposes `execute` — track in-flight state locally.
    const [isLoading, setIsLoading] = useState(false);
    const { execute: loadScanCmd } = usePageCommand<ScanDetailResponse, { runId: number }>('LoadScanDetail', {
        after: (r) => {
            setIsLoading(false);
            if (r?.detail) {
                setDetail(r.detail);
                setAckStates(
                    Object.fromEntries(
                        r.detail.findings.map((f) => [
                            f.fingerprint,
                            { ackState: f.ackState, snoozeUntilUtc: f.snoozeUntilUtc, ackNote: f.ackNote },
                        ]),
                    ),
                );
                setFeedback(null);
            }
        },
    });

    const { execute: mutateAck } = usePageCommand<AckMutationResponse, { fingerprint: string; action: string; snoozeUntilUtc?: string; note?: string }>('SetAckState', {
        after: (r) => {
            if (r?.success) {
                setAckStates((prev) => ({
                    ...prev,
                    [r.fingerprint]: { ackState: r.newState, snoozeUntilUtc: r.snoozeUntilUtc, ackNote: r.note },
                }));
                setFeedback(`${r.newState === 'Active' ? 'Revoked' : r.newState === 'Acknowledged' ? 'Acknowledged' : 'Snoozed'} finding.`);
                window.setTimeout(() => setFeedback(null), 3000);
            } else if (r) {
                setFeedback(r.message || 'Ack update failed.');
            }
        },
    });

    // Selected-fingerprint set drives the bulk-action bar. Stored as Set for O(1) toggle/check
    // across the findings list; cleared after every successful bulk mutation so the admin
    // doesn't accidentally re-mutate the same set.
    const [selected, setSelected] = useState<Set<string>>(new Set());

    const { execute: bulkCmd } = usePageCommand<BulkAckResponse, { fingerprints: string[]; action: string; snoozeUntilUtc?: string; note?: string }>('SetAckStateMany', {
        after: (r) => {
            if (!r) return;
            if (r.success) {
                setAckStates((prev) => {
                    const next = { ...prev };
                    for (const u of r.updates) {
                        next[u.fingerprint] = {
                            ackState: u.newState,
                            snoozeUntilUtc: u.snoozeUntilUtc,
                            ackNote: u.note,
                        };
                    }
                    return next;
                });
                setSelected(new Set());
            }
            setFeedback(r.message);
            window.setTimeout(() => setFeedback(null), 4000);
        },
    });

    const loadScan = (args: { runId: number }) => { setIsLoading(true); loadScanCmd(args); };

    const { execute: exportCmd } = usePageCommand<ExportFindingsResponse, { runId: number; format: string }>('ExportFindings', {
        after: (r) => {
            if (!r?.success) {
                setFeedback(r?.message || 'Export failed.');
                window.setTimeout(() => setFeedback(null), 4000);
                return;
            }
            // Create a blob + transient anchor to trigger the browser's Save-As flow. Revoking
            // the object URL on the next tick avoids leaking memory while guaranteeing the
            // download has been handed off to the browser's download manager.
            const blob = new Blob([r.content], { type: r.mimeType });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = r.fileName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.setTimeout(() => URL.revokeObjectURL(url), 0);
            setFeedback(r.message);
            window.setTimeout(() => setFeedback(null), 4000);
        },
    });

    const onExport = (format: 'csv' | 'json') => {
        if (selectedRunId == null) return;
        exportCmd({ runId: selectedRunId, format });
    };

    const onScanChange = (runId: number) => {
        // Drop any pending selection + feedback — the findings we're about to load are a
        // different set, and mutating "hidden" fingerprints the user can no longer see is a
        // real footgun. Better UX to make the admin re-select in the new scan deliberately.
        setSelected(new Set());
        setFeedback(null);
        setSelectedRunId(runId);
        loadScan({ runId });
    };

    const visibleFindings = useMemo(() => {
        const needle = search.trim().toLowerCase();
        return detail.findings.filter((f) => {
            // State filter first — cheap O(1) check, usually eliminates the majority of rows.
            const state = ackStates[f.fingerprint]?.ackState ?? f.ackState;
            if (filter === 'active' && state !== 'Active') return false;
            if (filter === 'acked' && state !== 'Acknowledged') return false;
            if (filter === 'snoozed' && state !== 'Snoozed') return false;
            // Search is secondary; only does a scan when a needle is set.
            if (needle.length === 0) return true;
            return (
                f.ruleId.toLowerCase().includes(needle) ||
                f.category.toLowerCase().includes(needle) ||
                f.message.toLowerCase().includes(needle) ||
                (f.location ?? '').toLowerCase().includes(needle) ||
                f.ruleTitle.toLowerCase().includes(needle)
            );
        });
    }, [detail.findings, ackStates, filter, search]);

    const byCategory = useMemo(() => {
        const grouped = new Map<string, FindingDetail[]>();
        for (const f of visibleFindings) {
            const arr = grouped.get(f.category) ?? [];
            arr.push(f);
            grouped.set(f.category, arr);
        }
        return Array.from(grouped.entries()).sort((a, b) => a[0].localeCompare(b[0]));
    }, [visibleFindings]);

    if (initial.availableScans.length === 0) {
        return (
            <div style={{ padding: 48, maxWidth: 640, margin: '64px auto', textAlign: 'center', color: COLORS.textMuted }}>
                <h2 style={{ margin: '0 0 12px', color: COLORS.textPrimary }}>No scans recorded</h2>
                <p>Run Sentinel at least once to populate the detail view.</p>
            </div>
        );
    }

    return (
        <div style={{ padding: '24px 32px', maxWidth: 1200, margin: '0 auto' }}>
            <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16, flexWrap: 'wrap', gap: 12 }}>
                <div>
                    <h1 style={{ margin: 0, fontSize: 24, fontWeight: 600, color: COLORS.textPrimary }}>
                        Scan detail
                    </h1>
                    {detail.run && (
                        <p style={{ margin: '4px 0 0', color: COLORS.textMuted, fontSize: 14 }}>
                            #{detail.run.runId} · {new Date(detail.run.startedAt).toLocaleString()} · {detail.run.trigger} · {detail.run.durationSeconds.toFixed(1)}s
                        </p>
                    )}
                </div>
                <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
                    <div style={{ position: 'relative' }}>
                        <label htmlFor="sentinel-scan-search" style={{ position: 'absolute', left: -9999 }}>
                            Search findings
                        </label>
                        <input
                            id="sentinel-scan-search"
                            type="search"
                            placeholder="Search rule / category / message…"
                            value={search}
                            onChange={(e) => setSearch(e.target.value)}
                            style={{ padding: '6px 10px', border: `1px solid ${COLORS.border}`, borderRadius: 6, fontSize: 14, minWidth: 240 }}
                        />
                    </div>
                    <div>
                        <label htmlFor="sentinel-scan-picker" style={{ fontSize: 14, color: COLORS.textMuted, marginRight: 8 }}>
                            Scan:
                        </label>
                        <select
                            id="sentinel-scan-picker"
                            value={selectedRunId ?? ''}
                            onChange={(e) => onScanChange(Number(e.target.value))}
                            disabled={isLoading}
                            style={{ padding: '6px 10px', border: `1px solid ${COLORS.border}`, borderRadius: 6, fontSize: 14 }}
                        >
                            {initial.availableScans.map((s) => (
                                <option key={s.runId} value={s.runId}>
                                    {s.label}
                                </option>
                            ))}
                        </select>
                    </div>
                    <div style={{ display: 'flex', gap: 6 }}>
                        <Button
                            type={ButtonType.Button}
                            label="Export CSV"
                            size={ButtonSize.S}
                            color={ButtonColor.Secondary}
                            disabled={selectedRunId == null}
                            onClick={() => onExport('csv')}
                        />
                        <Button
                            type={ButtonType.Button}
                            label="Export JSON"
                            size={ButtonSize.S}
                            color={ButtonColor.Secondary}
                            disabled={selectedRunId == null}
                            onClick={() => onExport('json')}
                        />
                    </div>
                </div>
            </header>

            {feedback && (
                <div
                    style={{
                        padding: '8px 14px',
                        marginBottom: 12,
                        background: '#F0FDF4',
                        border: `1px solid ${COLORS.success}`,
                        borderRadius: 6,
                        color: '#14532D',
                        fontSize: 14,
                    }}
                >
                    {feedback}
                </div>
            )}

            {detail.run && <ScanHeader run={detail.run} />}

            <FilterBar
                filter={filter}
                onChange={setFilter}
                counts={{
                    all: detail.findings.length,
                    active: detail.findings.filter((f) => (ackStates[f.fingerprint]?.ackState ?? f.ackState) === 'Active').length,
                    acked: detail.findings.filter((f) => (ackStates[f.fingerprint]?.ackState ?? f.ackState) === 'Acknowledged').length,
                    snoozed: detail.findings.filter((f) => (ackStates[f.fingerprint]?.ackState ?? f.ackState) === 'Snoozed').length,
                }}
            />

            <BulkActionBar
                selected={selected}
                visibleFingerprints={visibleFindings.map((f) => f.fingerprint)}
                onToggleAll={(allVisible) => {
                    // Select-all toggles the entire visible set; clicking again clears it. Keeps
                    // the UX predictable even when the filter tabs change what "visible" means.
                    setSelected((prev) => {
                        const allSelected = allVisible.every((fp) => prev.has(fp));
                        if (allSelected) {
                            const next = new Set(prev);
                            for (const fp of allVisible) next.delete(fp);
                            return next;
                        }
                        return new Set([...prev, ...allVisible]);
                    });
                }}
                onClear={() => setSelected(new Set())}
                onBulk={(action, payload) => bulkCmd({ fingerprints: Array.from(selected), action, ...(payload ?? {}) })}
            />

            {byCategory.length === 0 ? (
                <div style={{ padding: 48, textAlign: 'center', color: COLORS.textMuted, background: COLORS.bg, border: `1px solid ${COLORS.border}`, borderRadius: 10 }}>
                    <div style={{ fontSize: 32, marginBottom: 8, opacity: 0.5 }}>✓</div>
                    No findings match the current filter.
                </div>
            ) : (
                byCategory.map(([category, items]) => (
                    <CategorySection
                        key={category}
                        category={category}
                        findings={items}
                        ackStates={ackStates}
                        selected={selected}
                        onToggleSelect={(fp) => setSelected((prev) => {
                            const next = new Set(prev);
                            if (next.has(fp)) next.delete(fp); else next.add(fp);
                            return next;
                        })}
                        onToggleCategory={(fps) => setSelected((prev) => {
                            const allSelected = fps.every((fp) => prev.has(fp));
                            const next = new Set(prev);
                            if (allSelected) {
                                for (const fp of fps) next.delete(fp);
                            } else {
                                for (const fp of fps) next.add(fp);
                            }
                            return next;
                        })}
                        onMutate={(payload) => mutateAck(payload)}
                    />
                ))
            )}
        </div>
    );
};

const BulkActionBar = ({
    selected,
    visibleFingerprints,
    onToggleAll,
    onClear,
    onBulk,
}: {
    selected: Set<string>;
    visibleFingerprints: string[];
    onToggleAll: (visible: string[]) => void;
    onClear: () => void;
    onBulk: (action: 'acknowledge' | 'snooze' | 'revoke', payload?: { snoozeUntilUtc?: string; note?: string }) => void;
}) => {
    const [noteDraft, setNoteDraft] = useState('');
    const [snoozeOpen, setSnoozeOpen] = useState(false);
    const [snoozeDays, setSnoozeDays] = useState(7);

    const allVisibleSelected = visibleFingerprints.length > 0 && visibleFingerprints.every((fp) => selected.has(fp));
    const count = selected.size;

    return (
        <div
            style={{
                position: 'sticky',
                top: 0,
                zIndex: 2,
                background: count > 0 ? COLORS.lime : COLORS.bg,
                border: `1px solid ${COLORS.border}`,
                borderRadius: 10,
                padding: '10px 16px',
                marginBottom: 16,
                display: 'flex',
                alignItems: 'center',
                gap: 12,
                flexWrap: 'wrap',
                transition: 'background 120ms ease',
            }}
        >
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 14, color: COLORS.textPrimary, cursor: visibleFingerprints.length > 0 ? 'pointer' : 'default' }}>
                <input
                    type="checkbox"
                    disabled={visibleFingerprints.length === 0}
                    checked={allVisibleSelected}
                    onChange={() => onToggleAll(visibleFingerprints)}
                    aria-label="Select all visible findings"
                />
                {count > 0 ? `${count} selected` : 'Select all visible'}
            </label>
            {count > 0 && (
                <>
                    <input
                        type="text"
                        placeholder="Optional note (applied to all)"
                        value={noteDraft}
                        onChange={(e) => setNoteDraft(e.target.value)}
                        style={{ flex: '1 1 240px', minWidth: 200, padding: '6px 10px', border: `1px solid ${COLORS.border}`, borderRadius: 6, fontSize: 14 }}
                    />
                    <Button
                        type={ButtonType.Button}
                        label="Acknowledge"
                        size={ButtonSize.S}
                        color={ButtonColor.Primary}
                        onClick={() => onBulk('acknowledge', { note: noteDraft.trim() || undefined })}
                    />
                    <Button
                        type={ButtonType.Button}
                        label={snoozeOpen ? 'Cancel snooze' : 'Snooze…'}
                        size={ButtonSize.S}
                        color={ButtonColor.Secondary}
                        onClick={() => setSnoozeOpen(!snoozeOpen)}
                    />
                    <Button
                        type={ButtonType.Button}
                        label="Revoke"
                        size={ButtonSize.S}
                        color={ButtonColor.Secondary}
                        onClick={() => onBulk('revoke')}
                    />
                    <Button
                        type={ButtonType.Button}
                        label="Clear selection"
                        size={ButtonSize.S}
                        color={ButtonColor.Secondary}
                        onClick={() => { onClear(); setNoteDraft(''); setSnoozeOpen(false); }}
                    />
                    {snoozeOpen && (
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexBasis: '100%', paddingTop: 8, borderTop: `1px solid ${COLORS.border}` }}>
                            <label style={{ fontSize: 12, color: COLORS.textMuted }}>
                                Snooze for
                                <select
                                    value={snoozeDays}
                                    onChange={(e) => setSnoozeDays(Number(e.target.value))}
                                    style={{ marginLeft: 6, padding: '4px 8px', border: `1px solid ${COLORS.border}`, borderRadius: 4, fontSize: 12 }}
                                >
                                    <option value={1}>1 day</option>
                                    <option value={7}>7 days</option>
                                    <option value={30}>30 days</option>
                                    <option value={90}>90 days</option>
                                </select>
                            </label>
                            <Button
                                type={ButtonType.Button}
                                label={`Snooze all ${count} for ${snoozeDays}d`}
                                size={ButtonSize.S}
                                color={ButtonColor.Primary}
                                onClick={() => {
                                    const until = new Date();
                                    until.setUTCDate(until.getUTCDate() + snoozeDays);
                                    onBulk('snooze', { snoozeUntilUtc: until.toISOString(), note: noteDraft.trim() || undefined });
                                    setSnoozeOpen(false);
                                }}
                            />
                        </div>
                    )}
                </>
            )}
        </div>
    );
};

const ScanHeader = ({ run }: { run: ScanSummary }) => (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12, marginBottom: 16 }}>
        <StatTile label="Total" value={run.totalFindings} color={COLORS.textPrimary} />
        <StatTile label="Errors" value={run.errorCount} color={run.errorCount > 0 ? COLORS.error : COLORS.info} />
        <StatTile label="Warnings" value={run.warningCount} color={run.warningCount > 0 ? COLORS.warning : COLORS.info} />
        <StatTile label="Info" value={run.infoCount} color={COLORS.info} />
    </div>
);

const StatTile = ({ label, value, color }: { label: string; value: number; color: string }) => (
    <div style={{ padding: 14, background: COLORS.bg, border: `1px solid ${COLORS.border}`, borderRadius: 8, borderLeft: `3px solid ${color}` }}>
        <div style={{ fontSize: 11, color: COLORS.textMuted, textTransform: 'uppercase', letterSpacing: 0.4 }}>{label}</div>
        <div style={{ fontSize: 24, fontWeight: 700, color, lineHeight: 1, marginTop: 4 }}>{value.toLocaleString()}</div>
    </div>
);

const FilterBar = ({
    filter,
    onChange,
    counts,
}: {
    filter: 'all' | 'active' | 'acked' | 'snoozed';
    onChange: (f: 'all' | 'active' | 'acked' | 'snoozed') => void;
    counts: { all: number; active: number; acked: number; snoozed: number };
}) => {
    const tab = (id: 'all' | 'active' | 'acked' | 'snoozed', label: string, count: number) => (
        <button
            key={id}
            type="button"
            onClick={() => onChange(id)}
            style={{
                padding: '6px 14px',
                border: `1px solid ${COLORS.border}`,
                borderRadius: 20,
                background: filter === id ? COLORS.lime : COLORS.bg,
                color: COLORS.textPrimary,
                fontSize: 14,
                fontWeight: filter === id ? 600 : 500,
                cursor: 'pointer',
            }}
        >
            {label} <span style={{ color: COLORS.textMuted, marginLeft: 4 }}>({count})</span>
        </button>
    );
    return (
        <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
            {tab('active', 'Active', counts.active)}
            {tab('acked', 'Acknowledged', counts.acked)}
            {tab('snoozed', 'Snoozed', counts.snoozed)}
            {tab('all', 'All', counts.all)}
        </div>
    );
};

const CategorySection = ({
    category,
    findings,
    ackStates,
    selected,
    onToggleSelect,
    onToggleCategory,
    onMutate,
}: {
    category: string;
    findings: FindingDetail[];
    ackStates: Record<string, { ackState: string; snoozeUntilUtc: string | null; ackNote: string | null }>;
    selected: Set<string>;
    onToggleSelect: (fingerprint: string) => void;
    onToggleCategory: (fingerprints: string[]) => void;
    onMutate: (data: { fingerprint: string; action: string; snoozeUntilUtc?: string; note?: string }) => void;
}) => {
    const categoryFingerprints = findings.map((f) => f.fingerprint);
    const allCategorySelected = categoryFingerprints.length > 0 && categoryFingerprints.every((fp) => selected.has(fp));
    const someCategorySelected = !allCategorySelected && categoryFingerprints.some((fp) => selected.has(fp));

    return (
        <section
            style={{
                background: COLORS.bg,
                border: `1px solid ${COLORS.border}`,
                borderRadius: 10,
                overflow: 'hidden',
                marginBottom: 16,
            }}
        >
            <header style={{ padding: '12px 20px', background: COLORS.bgMuted, borderBottom: `1px solid ${COLORS.border}`, display: 'flex', alignItems: 'center', gap: 10 }}>
                <input
                    type="checkbox"
                    checked={allCategorySelected}
                    // indeterminate doesn't round-trip via the checked attribute — set it via ref
                    // so a partial category selection visually reads as "some, not all".
                    ref={(el) => { if (el) el.indeterminate = someCategorySelected; }}
                    onChange={() => onToggleCategory(categoryFingerprints)}
                    aria-label={`Select all findings in ${category}`}
                />
                <h3 style={{ margin: 0, fontSize: 14, fontWeight: 700, color: COLORS.textPrimary, textTransform: 'uppercase', letterSpacing: 0.3 }}>
                    {category} <span style={{ color: COLORS.textMuted, fontWeight: 500, marginLeft: 8 }}>{findings.length}</span>
                </h3>
            </header>
            <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
                {findings.map((f) => {
                    const currentAck = ackStates[f.fingerprint]?.ackState ?? f.ackState;
                    return (
                        <FindingRow
                            key={f.findingId}
                            finding={f}
                            ackState={currentAck}
                            snoozeUntil={ackStates[f.fingerprint]?.snoozeUntilUtc ?? f.snoozeUntilUtc}
                            isSelected={selected.has(f.fingerprint)}
                            onToggleSelect={() => onToggleSelect(f.fingerprint)}
                            onMutate={onMutate}
                        />
                    );
                })}
            </ul>
        </section>
    );
};

const FindingRow = ({
    finding,
    ackState,
    snoozeUntil,
    isSelected,
    onToggleSelect,
    onMutate,
}: {
    finding: FindingDetail;
    ackState: string;
    snoozeUntil: string | null;
    isSelected: boolean;
    onToggleSelect: () => void;
    onMutate: (data: { fingerprint: string; action: string; snoozeUntilUtc?: string; note?: string }) => void;
}) => {
    const [expanded, setExpanded] = useState(false);
    const color = severityColor(finding.severity);

    return (
        <li style={{
            padding: '14px 20px',
            borderBottom: `1px solid ${COLORS.border}`,
            background: isSelected ? `${COLORS.lime}33` : 'transparent',
            transition: 'background 100ms ease',
        }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto auto 1fr auto', gap: 12, alignItems: 'start' }}>
                <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={onToggleSelect}
                    aria-label={`Select finding ${finding.ruleId}`}
                    style={{ marginTop: 4 }}
                />
                <SeverityBadge severity={finding.severity} color={color} />
                <div>
                    <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                        <code style={{ color: COLORS.textPrimary, fontWeight: 700 }}>{finding.ruleId}</code>
                        <span style={{ color: COLORS.textPrimary, fontWeight: 500 }}>{finding.ruleTitle}</span>
                        <AckBadge state={ackState} until={snoozeUntil} />
                        <AgeBadge scanCount={finding.scanOccurrences} firstSeenUtc={finding.firstSeenUtc} />
                    </div>
                    <div style={{ color: COLORS.textMuted, fontSize: 14, marginTop: 4, lineHeight: 1.5 }}>
                        {finding.message}
                    </div>
                    {finding.location && (
                        <div style={{ color: COLORS.textMuted, fontSize: 12, marginTop: 4, fontFamily: 'monospace' }}>
                            → {finding.location}
                        </div>
                    )}
                    {(finding.remediation || finding.remediationTitle) && (
                        <button
                            type="button"
                            onClick={() => setExpanded(!expanded)}
                            style={{
                                marginTop: 8,
                                padding: '4px 10px',
                                border: `1px solid ${COLORS.border}`,
                                borderRadius: 4,
                                background: 'transparent',
                                color: COLORS.limeText,
                                fontSize: 12,
                                fontWeight: 600,
                                cursor: 'pointer',
                            }}
                        >
                            {expanded ? 'Hide' : 'Show'} remediation
                        </button>
                    )}
                </div>
                <AckActions fingerprint={finding.fingerprint} ackState={ackState} onMutate={onMutate} />
            </div>
            {expanded && (
                <div
                    style={{
                        marginTop: 12,
                        padding: 14,
                        background: COLORS.bgMuted,
                        borderRadius: 6,
                        fontSize: 14,
                        color: COLORS.textPrimary,
                        lineHeight: 1.6,
                    }}
                >
                    {finding.remediationTitle && (
                        <div style={{ fontWeight: 600, marginBottom: 4 }}>{finding.remediationTitle}</div>
                    )}
                    {finding.remediationSummary && <div style={{ marginBottom: 8, color: COLORS.textMuted }}>{finding.remediationSummary}</div>}
                    {finding.remediationSteps && (
                        <div>
                            <strong style={{ fontSize: 12, color: COLORS.textMuted, textTransform: 'uppercase', letterSpacing: 0.3 }}>Steps</strong>
                            <div style={{ marginTop: 4 }}>{finding.remediationSteps}</div>
                        </div>
                    )}
                    {finding.remediation && (
                        <div style={{ marginTop: 8, paddingTop: 8, borderTop: `1px solid ${COLORS.border}` }}>
                            <strong style={{ fontSize: 12, color: COLORS.textMuted, textTransform: 'uppercase', letterSpacing: 0.3 }}>From scan</strong>
                            <div style={{ marginTop: 4 }}>{finding.remediation}</div>
                        </div>
                    )}
                </div>
            )}
        </li>
    );
};

const SeverityBadge = ({ severity, color }: { severity: string; color: string }) => (
    <span
        style={{
            display: 'inline-block',
            padding: '2px 8px',
            fontSize: 11,
            fontWeight: 700,
            color: '#FFF',
            background: color,
            borderRadius: 4,
            textTransform: 'uppercase',
            letterSpacing: 0.3,
            minWidth: 60,
            textAlign: 'center',
        }}
    >
        {severity}
    </span>
);

const AckBadge = ({ state, until }: { state: string; until: string | null }) => {
    if (state === 'Active') return null;
    const isSnooze = state === 'Snoozed';
    return (
        <span
            style={{
                fontSize: 11,
                padding: '2px 8px',
                borderRadius: 10,
                background: isSnooze ? '#FEF3C7' : '#E0E7FF',
                color: isSnooze ? '#78350F' : '#3730A3',
                fontWeight: 600,
            }}
        >
            {isSnooze && until ? `Snoozed until ${new Date(until).toLocaleDateString()}` : 'Acknowledged'}
        </span>
    );
};

// "New" vs "seen in N scans, first X ago" triage chip. Operator values: NEW = fresh regression,
// worth investigating; long-running = known/accepted debt, ack candidate. Findings that only
// appear in the current scan (or have no first-seen history) render the green NEW chip —
// helpful in both a first-time install AND on a subsequent scan where something actually is new.
const AgeBadge = ({ scanCount, firstSeenUtc }: { scanCount: number; firstSeenUtc: string | null }) => {
    if (scanCount <= 1 || !firstSeenUtc) {
        return (
            <span
                style={{
                    fontSize: 11,
                    padding: '2px 8px',
                    borderRadius: 10,
                    background: '#DCFCE7',
                    color: '#15803D',
                    fontWeight: 600,
                }}
                title="First appeared in this scan"
            >
                NEW
            </span>
        );
    }
    const firstSeen = new Date(firstSeenUtc);
    const days = Math.max(1, Math.round((Date.now() - firstSeen.getTime()) / (1000 * 60 * 60 * 24)));
    const age = days < 7 ? `${days}d` : days < 30 ? `${Math.round(days / 7)}w` : days < 365 ? `${Math.round(days / 30)}mo` : `${Math.round(days / 365)}y`;
    return (
        <span
            style={{
                fontSize: 11,
                padding: '2px 8px',
                borderRadius: 10,
                background: '#F1F5F9',
                color: '#475569',
                fontWeight: 600,
            }}
            title={`Seen in ${scanCount} scans, first detected ${firstSeen.toLocaleDateString()}`}
        >
            {scanCount}× · {age} old
        </span>
    );
};

const AckActions = ({
    fingerprint,
    ackState,
    onMutate,
}: {
    fingerprint: string;
    ackState: string;
    onMutate: (data: { fingerprint: string; action: string; snoozeUntilUtc?: string; note?: string }) => void;
}) => {
    // Inline duration dropdown + single "Snooze Xd" button. Picking a different duration from
    // the dropdown changes both state AND the button label in the same update — when they
    // click, they submit whatever the dropdown says. No separate "Customize" button (it
    // confused operators: felt like a mode switch for an ostensibly-default action).
    const [snoozeDays, setSnoozeDays] = useState(7);
    const [note, setNote] = useState('');

    if (ackState !== 'Active') {
        return (
            <Button
                type={ButtonType.Button}
                label="Revoke"
                size={ButtonSize.S}
                color={ButtonColor.Secondary}
                onClick={() => onMutate({ fingerprint, action: 'revoke' })}
            />
        );
    }

    const submitSnooze = () => {
        const until = new Date();
        until.setUTCDate(until.getUTCDate() + snoozeDays);
        onMutate({
            fingerprint,
            action: 'snooze',
            snoozeUntilUtc: until.toISOString(),
            note: note.trim() || undefined,
        });
        setNote('');
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6, alignItems: 'flex-end' }}>
            <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
                <Button
                    type={ButtonType.Button}
                    label="Acknowledge"
                    size={ButtonSize.S}
                    color={ButtonColor.Secondary}
                    onClick={() => onMutate({ fingerprint, action: 'acknowledge', note: note.trim() || undefined })}
                />
                {/*
                  Snooze group: dropdown + button wrapped in a shared container so the
                  duration selector visually "belongs" to the Snooze action, not Acknowledge.
                  Zero gap between dropdown and button + matching heights + subtle bgMuted
                  background — reads as a paired control. Gap:16 outside the group separates
                  it from Acknowledge so there's no ambiguity about which button owns the
                  dropdown.
                */}
                <div
                    style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        background: COLORS.bgMuted,
                        border: `1px solid ${COLORS.border}`,
                        borderRadius: 4,
                        padding: '3px 4px 3px 8px',
                        gap: 8,
                    }}
                    aria-label="Snooze action group"
                >
                    <select
                        value={snoozeDays}
                        onChange={(e) => setSnoozeDays(Number(e.target.value))}
                        aria-label="Snooze duration"
                        style={{
                            padding: '3px 6px',
                            border: `1px solid ${COLORS.border}`,
                            background: COLORS.bg,
                            color: COLORS.textPrimary,
                            fontSize: 13,
                            borderRadius: 3,
                            outline: 'none',
                        }}
                    >
                        <option value={1}>1 day</option>
                        <option value={7}>7 days</option>
                        <option value={30}>30 days</option>
                        <option value={90}>90 days</option>
                    </select>
                    <Button
                        type={ButtonType.Button}
                        label={`Snooze ${snoozeDays}d`}
                        size={ButtonSize.S}
                        color={ButtonColor.Secondary}
                        onClick={submitSnooze}
                    />
                </div>
            </div>
            <input
                type="text"
                placeholder="Optional note (applied to this action)"
                value={note}
                onChange={(e) => setNote(e.target.value)}
                style={{
                    padding: '4px 8px',
                    border: `1px solid ${COLORS.border}`,
                    borderRadius: 4,
                    fontSize: 12,
                    minWidth: 240,
                    background: COLORS.bg,
                    color: COLORS.textPrimary,
                }}
            />
        </div>
    );
};

