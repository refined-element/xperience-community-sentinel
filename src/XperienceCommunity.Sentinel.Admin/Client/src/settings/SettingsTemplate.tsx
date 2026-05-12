import React, { useState } from 'react';
import { usePageCommand } from '@kentico/xperience-admin-base';
import { Button, ButtonColor, ButtonSize, ButtonType } from '@kentico/xperience-admin-components';

import { THEME_COLORS as COLORS } from '../theme';

interface RuleDto {
    readonly ruleId: string;
    readonly title: string;
    readonly category: string;
}

interface SchedulePresetDto {
    readonly intervalRaw: string;
    readonly label: string;
}

interface SettingsClientProperties {
    readonly enabled: boolean;
    readonly excludedChecks: ReadonlyArray<string>;
    readonly staleDays: number;
    readonly eventLogDays: number;
    readonly emailDigestEnabled: boolean;
    readonly emailDigestRecipients: ReadonlyArray<string>;
    readonly emailDigestSeverityThreshold: string;
    readonly emailDigestOnlyWhenThresholdFindings: boolean;
    readonly eventLogEnabled: boolean;
    readonly eventLogSeverityThreshold: string;
    readonly eventLogMaxEntriesPerScan: number;

    readonly hasOverride: boolean;
    readonly currentUserId: number;

    readonly runtimeConnectionString: string;
    readonly contactEndpoint: string;
    readonly contactIncludeContextByDefault: boolean;
    readonly scheduledTasksUrl: string;

    readonly schedulePresets: ReadonlyArray<SchedulePresetDto>;
    readonly scheduleEnabled: boolean;
    readonly scheduleState: 'enabled' | 'disabled' | 'missing';
    readonly scheduleIntervalRaw: string;
    readonly scheduleIntervalHint: string;
    readonly scheduleLastRunUtc: string | null;
    readonly scheduleNextRunUtc: string | null;

    readonly knownRules: ReadonlyArray<RuleDto>;
}

interface SaveSettingsData {
    readonly enabled: boolean;
    readonly excludedChecks: ReadonlyArray<string>;
    readonly staleDays: number;
    readonly eventLogDays: number;
    readonly eventLogEnabled: boolean;
    readonly eventLogSeverityThreshold: string;
    readonly eventLogMaxEntriesPerScan: number;
    readonly emailDigestEnabled: boolean;
    readonly emailDigestRecipients: ReadonlyArray<string>;
    readonly emailDigestSeverityThreshold: string;
    readonly emailDigestOnlyWhenThresholdFindings: boolean;
    readonly scheduleEnabled: boolean;
    readonly scheduleIntervalRaw: string;
}

interface SaveSettingsResponse {
    readonly success: boolean;
    readonly message: string;
}

const SEVERITY_OPTIONS = ['Info', 'Warning', 'Error'] as const;

export const SettingsTemplate = (initial: SettingsClientProperties) => {
    // Every field mirrors a props value. On Save we POST the full snapshot — matching the
    // "all or nothing" invariant on the server side. Local state lets the admin edit freely
    // before committing.
    const [enabled, setEnabled] = useState(initial.enabled);
    const [staleDays, setStaleDays] = useState(initial.staleDays);
    const [eventLogDays, setEventLogDays] = useState(initial.eventLogDays);

    const [eventLogEnabled, setEventLogEnabled] = useState(initial.eventLogEnabled);
    const [eventLogSeverity, setEventLogSeverity] = useState(initial.eventLogSeverityThreshold);
    const [eventLogMaxEntries, setEventLogMaxEntries] = useState(initial.eventLogMaxEntriesPerScan);

    const [emailDigestEnabled, setEmailDigestEnabled] = useState(initial.emailDigestEnabled);
    const [emailDigestRecipientsText, setEmailDigestRecipientsText] = useState(
        initial.emailDigestRecipients.join(', '),
    );
    const [emailDigestSeverity, setEmailDigestSeverity] = useState(initial.emailDigestSeverityThreshold);
    const [emailDigestOnlyWhenThreshold, setEmailDigestOnlyWhenThreshold] = useState(
        initial.emailDigestOnlyWhenThresholdFindings,
    );

    const [excludedChecks, setExcludedChecks] = useState<ReadonlyArray<string>>(initial.excludedChecks);
    const [scheduleEnabled, setScheduleEnabled] = useState(initial.scheduleEnabled);
    const [scheduleIntervalRaw, setScheduleIntervalRaw] = useState(initial.scheduleIntervalRaw);

    const [isSaving, setIsSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ tone: 'success' | 'error'; text: string } | null>(null);
    const [hasOverride, setHasOverride] = useState(initial.hasOverride);

    const { execute: saveCmd } = usePageCommand<SaveSettingsResponse, SaveSettingsData>('SaveSettings', {
        after: (r) => {
            setIsSaving(false);
            if (!r) return;
            setFeedback({ tone: r.success ? 'success' : 'error', text: r.message });
            if (r.success) {
                setHasOverride(true);
                window.setTimeout(() => setFeedback(null), 6000);
            }
        },
    });

    const { execute: resetCmd } = usePageCommand<SaveSettingsResponse>('ResetToDefaults', {
        after: (r) => {
            setIsSaving(false);
            if (!r) return;
            setFeedback({ tone: r.success ? 'success' : 'error', text: r.message });
            if (r.success) {
                setHasOverride(false);
                // Don't auto-reload — form still shows last-saved values, which is fine. Admin
                // can hard-refresh the page to see the post-reset effective values if needed.
                window.setTimeout(() => setFeedback(null), 6000);
            }
        },
    });

    const onSave = () => {
        setIsSaving(true);
        setFeedback(null);
        saveCmd({
            enabled,
            staleDays,
            eventLogDays,
            eventLogEnabled,
            eventLogSeverityThreshold: eventLogSeverity,
            eventLogMaxEntriesPerScan: eventLogMaxEntries,
            emailDigestEnabled,
            emailDigestRecipients: parseRecipients(emailDigestRecipientsText),
            emailDigestSeverityThreshold: emailDigestSeverity,
            emailDigestOnlyWhenThresholdFindings: emailDigestOnlyWhenThreshold,
            excludedChecks,
            scheduleEnabled,
            scheduleIntervalRaw,
        });
    };

    const onReset = () => {
        if (!window.confirm('Clear the override and revert to appsettings values? Next scan will use the file-layer config.')) {
            return;
        }
        setIsSaving(true);
        setFeedback(null);
        resetCmd();
    };

    const toggleExcludedRule = (ruleId: string) => {
        setExcludedChecks((prev) =>
            prev.includes(ruleId) ? prev.filter((r) => r !== ruleId) : [...prev, ruleId],
        );
    };

    return (
        <div style={{ padding: '24px 32px', maxWidth: 960, margin: '0 auto', fontSize: 14 }}>
            <header style={{ marginBottom: 20 }}>
                <h1 style={{ margin: 0, fontSize: 26, fontWeight: 600, color: COLORS.textPrimary }}>
                    Settings
                </h1>
                <p style={{ margin: '6px 0 0', color: COLORS.textMuted, lineHeight: 1.6 }}>
                    Edit and save takes effect on the next scan — no app restart required. Locked fields
                    (connection string, contact endpoint) live only in <code>appsettings.json</code> /
                    env vars for security reasons.
                </p>
            </header>

            {hasOverride && (
                <div
                    style={{
                        padding: '10px 16px',
                        marginBottom: 20,
                        background: '#EEF6E0',
                        border: `1px solid ${COLORS.limeDark}`,
                        borderRadius: 8,
                        color: COLORS.limeText,
                        fontSize: 13,
                        fontWeight: 600,
                    }}
                >
                    Override active — these values win over <code>appsettings.json</code>. Reset to defaults
                    at the bottom of the page.
                </div>
            )}

            {feedback && (
                <div
                    role="alert"
                    style={{
                        padding: '10px 14px',
                        marginBottom: 16,
                        background: feedback.tone === 'success' ? '#F0FDF4' : '#FEF2F2',
                        border: `1px solid ${feedback.tone === 'success' ? COLORS.success : COLORS.error}`,
                        borderRadius: 8,
                        color: feedback.tone === 'success' ? '#14532D' : '#7F1D1D',
                    }}
                >
                    {feedback.text}
                </div>
            )}

            <MasterSwitch enabled={enabled} onChange={setEnabled} />

            <Section title="Scan behavior">
                <Row label="Stale content threshold (days)" hint="Sentinel:RuntimeChecks:StaleDays">
                    <NumberField value={staleDays} onChange={setStaleDays} min={1} max={3650} />
                </Row>
                <Row label="Event log recency window (days)" hint="Sentinel:RuntimeChecks:EventLogDays">
                    <NumberField value={eventLogDays} onChange={setEventLogDays} min={1} max={3650} />
                </Row>
            </Section>

            <Section title="Event log mirror">
                <Row label="Enabled" hint="Sentinel:EventLogIntegration:Enabled">
                    <Toggle value={eventLogEnabled} onChange={setEventLogEnabled} />
                </Row>
                <Row label="Severity threshold" hint="Sentinel:EventLogIntegration:SeverityThreshold">
                    <SeveritySelect value={eventLogSeverity} onChange={setEventLogSeverity} />
                </Row>
                <Row label="Max entries per scan" hint="Sentinel:EventLogIntegration:MaxEntriesPerScan">
                    <NumberField value={eventLogMaxEntries} onChange={setEventLogMaxEntries} min={0} max={10000} />
                </Row>
            </Section>

            <Section title="Email digest">
                <Row label="Enabled" hint="Sentinel:EmailDigest:Enabled">
                    <Toggle value={emailDigestEnabled} onChange={setEmailDigestEnabled} />
                </Row>
                <Row label="Recipients" hint="Comma- or newline-separated email addresses">
                    <textarea
                        value={emailDigestRecipientsText}
                        onChange={(e) => setEmailDigestRecipientsText(e.target.value)}
                        rows={3}
                        style={textareaStyle}
                        placeholder="ops@example.com, alerts@example.com"
                    />
                </Row>
                <Row label="Severity threshold" hint="Sentinel:EmailDigest:SeverityThreshold">
                    <SeveritySelect value={emailDigestSeverity} onChange={setEmailDigestSeverity} />
                </Row>
                <Row label="Only send when findings ≥ threshold" hint="Sentinel:EmailDigest:OnlyWhenThresholdFindings">
                    <Toggle value={emailDigestOnlyWhenThreshold} onChange={setEmailDigestOnlyWhenThreshold} />
                </Row>
            </Section>

            <Section title="Scan cadence">
                <Row label="Enabled" hint="Runs automatically on the cadence below">
                    <Toggle value={scheduleEnabled} onChange={setScheduleEnabled} />
                </Row>
                <Row label="Cadence" hint="Kentico scheduled task interval">
                    <SchedulePresetSelect
                        value={scheduleIntervalRaw}
                        onChange={setScheduleIntervalRaw}
                        presets={initial.schedulePresets}
                    />
                </Row>
                <div style={scheduleMetaStyle}>
                    Status: <strong>{initial.scheduleState}</strong>
                    {initial.scheduleState === 'missing' && ' — Save below to create the default row, or create one manually in Scheduled tasks.'}
                    {initial.scheduleLastRunUtc && (
                        <> · last run {new Date(initial.scheduleLastRunUtc).toLocaleString()}</>
                    )}
                    {initial.scheduleNextRunUtc && (
                        <> · next run {new Date(initial.scheduleNextRunUtc).toLocaleString()}</>
                    )}
                    <> · </>
                    <a href={initial.scheduledTasksUrl} style={{ color: COLORS.limeText, fontWeight: 600 }}>
                        Edit in Scheduled tasks →
                    </a>
                </div>
            </Section>

            <Section title="Excluded checks">
                <p style={{ margin: '4px 0 12px', color: COLORS.textMuted, fontSize: 13 }}>
                    Check the rules you want Sentinel to skip. Source: Sentinel:Checks:Excluded.
                </p>
                <ExcludedChecksList
                    knownRules={initial.knownRules}
                    excluded={excludedChecks}
                    onToggle={toggleExcludedRule}
                />
            </Section>

            <div style={{ display: 'flex', gap: 12, marginTop: 24, justifyContent: 'space-between', flexWrap: 'wrap' }}>
                <Button
                    type={ButtonType.Button}
                    label={isSaving ? 'Saving…' : 'Save settings'}
                    size={ButtonSize.M}
                    color={ButtonColor.Primary}
                    disabled={isSaving}
                    onClick={onSave}
                />
                {hasOverride && (
                    <Button
                        type={ButtonType.Button}
                        label="Reset to appsettings defaults"
                        size={ButtonSize.M}
                        color={ButtonColor.Secondary}
                        disabled={isSaving}
                        onClick={onReset}
                    />
                )}
            </div>

            <ReadOnlyFooter
                runtimeConnectionString={initial.runtimeConnectionString}
                contactEndpoint={initial.contactEndpoint}
                contactIncludeContextByDefault={initial.contactIncludeContextByDefault}
            />
        </div>
    );
};

// Parse a free-form "emails" field into a clean list: splits on commas/newlines, trims,
// drops blanks. Validation lives server-side (MailAddress.TryCreate); we just prep the array.
const parseRecipients = (raw: string): string[] =>
    raw
        .split(/[,\n]/)
        .map((s) => s.trim())
        .filter((s) => s.length > 0);

const inputStyle: React.CSSProperties = {
    padding: '6px 10px',
    border: `1px solid ${COLORS.border}`,
    borderRadius: 6,
    fontSize: 14,
    background: COLORS.bg,
    color: COLORS.textPrimary,
    fontFamily: 'inherit',
};

const textareaStyle: React.CSSProperties = {
    ...inputStyle,
    width: '100%',
    fontFamily: 'inherit',
    resize: 'vertical',
    minHeight: 60,
    boxSizing: 'border-box',
};

const scheduleMetaStyle: React.CSSProperties = {
    padding: '8px 0 0',
    fontSize: 12,
    color: COLORS.textMuted,
};

const MasterSwitch = ({ enabled, onChange }: { enabled: boolean; onChange: (v: boolean) => void }) => (
    <div
        style={{
            padding: 14,
            marginBottom: 20,
            background: enabled ? '#F0FDF4' : '#FEF2F2',
            border: `1px solid ${enabled ? COLORS.success : COLORS.error}`,
            borderRadius: 10,
            display: 'flex',
            alignItems: 'center',
            gap: 16,
        }}
    >
        <Toggle value={enabled} onChange={onChange} />
        <div style={{ color: enabled ? '#14532D' : '#7F1D1D' }}>
            <div style={{ fontWeight: 600, fontSize: 15 }}>Sentinel is {enabled ? 'enabled' : 'disabled'}</div>
            <div style={{ fontSize: 13 }}>
                {enabled
                    ? 'Scheduled and manual scans run normally.'
                    : 'Scheduled scans are skipped. Flip back on to re-activate.'}
            </div>
        </div>
    </div>
);

const Section = ({ title, children }: { title: string; children: React.ReactNode }) => (
    <section
        style={{
            background: COLORS.bg,
            border: `1px solid ${COLORS.border}`,
            borderRadius: 10,
            overflow: 'hidden',
            marginBottom: 16,
        }}
    >
        <header style={{ padding: '10px 20px', background: COLORS.bgMuted, borderBottom: `1px solid ${COLORS.border}` }}>
            <h2 style={{ margin: 0, fontSize: 15, fontWeight: 700, color: COLORS.textPrimary }}>{title}</h2>
        </header>
        <div style={{ padding: '8px 20px 16px' }}>{children}</div>
    </section>
);

const Row = ({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) => (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: 16, padding: '10px 0', borderBottom: `1px dashed ${COLORS.border}` }}>
        <div>
            <div style={{ fontWeight: 600, color: COLORS.textPrimary }}>{label}</div>
            {hint && <div style={{ fontSize: 12, color: COLORS.textMuted, marginTop: 2 }}>{hint}</div>}
        </div>
        <div>{children}</div>
    </div>
);

const Toggle = ({ value, onChange }: { value: boolean; onChange: (v: boolean) => void }) => (
    <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
        <input type="checkbox" checked={value} onChange={(e) => onChange(e.target.checked)} />
        <span style={{ color: COLORS.textMuted, fontSize: 13 }}>{value ? 'On' : 'Off'}</span>
    </label>
);

const NumberField = ({ value, onChange, min, max }: { value: number; onChange: (v: number) => void; min: number; max: number }) => (
    <input
        type="number"
        value={value}
        min={min}
        max={max}
        onChange={(e) => onChange(Number(e.target.value))}
        style={{ ...inputStyle, width: 140 }}
    />
);

const SeveritySelect = ({ value, onChange }: { value: string; onChange: (v: string) => void }) => (
    <select value={value} onChange={(e) => onChange(e.target.value)} style={{ ...inputStyle, width: 180 }}>
        {SEVERITY_OPTIONS.map((s) => (
            <option key={s} value={s}>{s}</option>
        ))}
    </select>
);

const SchedulePresetSelect = ({
    value,
    onChange,
    presets,
}: {
    value: string;
    onChange: (v: string) => void;
    presets: ReadonlyArray<SchedulePresetDto>;
}) => {
    // Three shapes this select has to handle without ambiguity between "what the <select>
    // shows" and "what's in state":
    //   1. Value matches a preset → render the preset list, that option selected
    //   2. Value is a custom DB cadence (hand-tuned in Scheduled Tasks) → pin a "Custom: X"
    //      option so Save preserves it rather than clobbering the user's hand-tune
    //   3. Value is empty (no task row yet) → pin a "Keep current / pick a cadence" sentinel
    //      option with value="" so the <select> visually matches state. Server-side this means
    //      "don't change the cadence" — and since there's no row yet, Save will create one
    //      using the daily default (see TryApplySchedulePreset).
    const isEmpty = !value;
    const isCustom = !!value && !presets.some((p) => p.intervalRaw === value);
    return (
        <select value={value} onChange={(e) => onChange(e.target.value)} style={{ ...inputStyle, width: 260 }}>
            {isEmpty && (
                <option value="">Keep current / pick a cadence…</option>
            )}
            {isCustom && (
                <option value={value}>Custom: {value}</option>
            )}
            {presets.map((p) => (
                <option key={p.intervalRaw} value={p.intervalRaw}>{p.label}</option>
            ))}
        </select>
    );
};

const ExcludedChecksList = ({
    knownRules,
    excluded,
    onToggle,
}: {
    knownRules: ReadonlyArray<RuleDto>;
    excluded: ReadonlyArray<string>;
    onToggle: (ruleId: string) => void;
}) => {
    // Group by category so the admin scans a familiar hierarchy (Configuration / Content Model /
    // Dependencies / Observability / Versioning) rather than a flat list of cryptic IDs.
    const byCategory = knownRules.reduce<Record<string, RuleDto[]>>((acc, r) => {
        (acc[r.category] ??= []).push(r);
        return acc;
    }, {});
    return (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 16 }}>
            {Object.entries(byCategory).map(([cat, rules]) => (
                <div key={cat}>
                    <div style={{ fontSize: 12, textTransform: 'uppercase', letterSpacing: 0.4, color: COLORS.textMuted, marginBottom: 6 }}>
                        {cat}
                    </div>
                    {rules.map((r) => (
                        <label key={r.ruleId} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', cursor: 'pointer' }}>
                            <input
                                type="checkbox"
                                checked={excluded.includes(r.ruleId)}
                                onChange={() => onToggle(r.ruleId)}
                            />
                            <code style={{ fontSize: 12, color: COLORS.textPrimary, fontWeight: 700 }}>{r.ruleId}</code>
                            <span style={{ fontSize: 13, color: COLORS.textPrimary }}>{r.title}</span>
                        </label>
                    ))}
                </div>
            ))}
        </div>
    );
};

const ReadOnlyFooter = ({
    runtimeConnectionString,
    contactEndpoint,
    contactIncludeContextByDefault,
}: {
    runtimeConnectionString: string;
    contactEndpoint: string;
    contactIncludeContextByDefault: boolean;
}) => (
    <div
        style={{
            marginTop: 32,
            padding: 16,
            background: COLORS.bgMuted,
            borderRadius: 8,
            fontSize: 13,
            color: COLORS.textMuted,
            lineHeight: 1.6,
        }}
    >
        <div style={{ fontWeight: 600, color: COLORS.textPrimary, marginBottom: 6 }}>
            Locked settings (config-file only)
        </div>
        <ul style={{ margin: '0 0 0 18px', padding: 0 }}>
            <li>
                <strong>Runtime connection string</strong> — <code>{runtimeConnectionString}</code>. Editing this
                from the admin UI would be a privilege-escalation vector.
            </li>
            <li>
                <strong>Contact endpoint</strong> — <code>{contactEndpoint}</code>, include context by default:{' '}
                <code>{contactIncludeContextByDefault ? 'yes' : 'no'}</code>. Changing mid-flight breaks outstanding
                quote submissions.
            </li>
        </ul>
        <div style={{ marginTop: 10, fontSize: 12 }}>
            Edit these in <code>appsettings.json</code> → key path <code>Sentinel:RuntimeChecks:ConnectionString</code> /{' '}
            <code>Sentinel:Contact:Endpoint</code>.
        </div>
    </div>
);
