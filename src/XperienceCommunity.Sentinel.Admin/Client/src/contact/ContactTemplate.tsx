import React, { useState } from 'react';
import { usePageCommand } from '@kentico/xperience-admin-base';
import { Button, ButtonColor, ButtonSize, ButtonType } from '@kentico/xperience-admin-components';

// Contact form → Refined Element quote intake. Payload mirrors
// SentinelContactPage.ContactSubmitData on the C# side; keep in sync on changes.

interface ScanOption {
    readonly runId: number;
    readonly label: string;
    readonly findingsCount: number;
}

interface ContactClientProperties {
    readonly availableScans: ReadonlyArray<ScanOption>;
    readonly defaultIncludeContext: boolean;
    readonly prefilledEmail: string;
    readonly contactEndpoint: string;
}

interface SubmitData {
    readonly scanRunId: number;
    readonly contactEmail: string;
    readonly contactName: string;
    readonly company: string;
    readonly message: string;
    readonly includeContext: boolean;
}

interface SubmitResult {
    readonly success: boolean;
    readonly statusCode: number;
    readonly message: string;
    readonly errorMessage: string | null;
}

import { THEME_COLORS as COLORS } from '../theme';

const inputStyle: React.CSSProperties = {
    width: '100%',
    padding: '10px 12px',
    fontSize: 14,
    border: `1px solid ${COLORS.border}`,
    borderRadius: 6,
    background: COLORS.bg,
    color: COLORS.textPrimary,
    fontFamily: 'inherit',
    boxSizing: 'border-box',
};

const labelStyle: React.CSSProperties = {
    display: 'block',
    marginBottom: 6,
    fontSize: 14,
    fontWeight: 600,
    color: COLORS.textPrimary,
};

export const ContactTemplate = (initial: ContactClientProperties) => {
    const [email, setEmail] = useState(initial.prefilledEmail);
    const [name, setName] = useState('');
    const [company, setCompany] = useState('');
    const [message, setMessage] = useState('');
    const [includeContext, setIncludeContext] = useState(initial.defaultIncludeContext);
    const [scanRunId, setScanRunId] = useState<number | null>(
        initial.availableScans.length > 0 ? initial.availableScans[0].runId : null,
    );
    const [result, setResult] = useState<SubmitResult | null>(null);

    // Kentico's Command<T> only exposes `execute` — no built-in pending flag. Track in-flight
    // state with a local useState; toggle true around the execute call and false in `after`.
    const [isPending, setIsPending] = useState(false);
    const { execute } = usePageCommand<SubmitResult, SubmitData>('SubmitContact', {
        after: (r) => {
            setIsPending(false);
            if (r) {
                setResult(r);
                // Clear the form on success so the admin doesn't accidentally re-submit by
                // clicking twice. Persist the email — the same operator will likely submit
                // follow-up quotes and shouldn't retype it every time.
                if (r.success) {
                    setMessage('');
                }
            }
        },
    });

    const canSubmit = scanRunId !== null && email.trim().length > 0 && !isPending;

    // Submit path — Kentico's <Button> fires this directly rather than relying on the enclosing
    // form's native submit, because Button doesn't consistently propagate as type="submit"
    // across admin versions. The form wrapper is kept for semantics + native Enter-key handling.
    const submitQuote = () => {
        if (!canSubmit || scanRunId === null) return;
        setResult(null);
        setIsPending(true);
        execute({
            scanRunId,
            contactEmail: email.trim(),
            contactName: name.trim(),
            company: company.trim(),
            message: message.trim(),
            includeContext,
        });
    };

    const onSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        submitQuote();
    };

    if (initial.availableScans.length === 0) {
        return (
            <div style={{ padding: 48, maxWidth: 640, margin: '64px auto', textAlign: 'center', color: COLORS.textMuted }}>
                <h2 style={{ margin: '0 0 12px', color: COLORS.textPrimary }}>No completed scans to reference</h2>
                <p style={{ margin: 0, fontSize: 14, lineHeight: 1.6 }}>
                    Run at least one Sentinel scan before requesting a quote — the submission includes the
                    findings that need remediation, so there has to be a scan to point at.
                </p>
            </div>
        );
    }

    return (
        <div style={{ padding: '24px 32px', maxWidth: 720, margin: '0 auto' }}>
            <header style={{ marginBottom: 24 }}>
                <h1 style={{ margin: 0, fontSize: 24, fontWeight: 600, color: COLORS.textPrimary }}>
                    Request a remediation quote
                </h1>
                <p style={{ margin: '6px 0 0', color: COLORS.textMuted, fontSize: 14, lineHeight: 1.6 }}>
                    Send a sanitized scan summary to Refined Element. We reply with a fixed-price quote to
                    remediate the findings. Nothing from your database is transmitted — only the scan-run
                    metadata plus sanitized finding rows.
                </p>
            </header>

            <form
                onSubmit={onSubmit}
                style={{
                    background: COLORS.bg,
                    border: `1px solid ${COLORS.border}`,
                    borderRadius: 10,
                    padding: 24,
                }}
            >
                <Field id="sentinel-contact-scan" label="Scan to reference *">
                    <select
                        id="sentinel-contact-scan"
                        style={inputStyle}
                        value={scanRunId ?? ''}
                        onChange={(e) => setScanRunId(Number(e.target.value))}
                        required
                    >
                        {initial.availableScans.map((s) => (
                            <option key={s.runId} value={s.runId}>
                                {s.label} ({s.findingsCount} finding{s.findingsCount === 1 ? '' : 's'})
                            </option>
                        ))}
                    </select>
                </Field>

                <Field id="sentinel-contact-email" label="Email *">
                    <input
                        id="sentinel-contact-email"
                        type="email"
                        style={inputStyle}
                        value={email}
                        onChange={(e) => setEmail(e.target.value)}
                        required
                        placeholder="you@company.com"
                        autoComplete="email"
                    />
                </Field>

                <Row>
                    <Field id="sentinel-contact-name" label="Name">
                        <input
                            id="sentinel-contact-name"
                            type="text"
                            style={inputStyle}
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            placeholder="Optional"
                            autoComplete="name"
                        />
                    </Field>
                    <Field id="sentinel-contact-company" label="Company">
                        <input
                            id="sentinel-contact-company"
                            type="text"
                            style={inputStyle}
                            value={company}
                            onChange={(e) => setCompany(e.target.value)}
                            placeholder="Optional"
                            autoComplete="organization"
                        />
                    </Field>
                </Row>

                <Field id="sentinel-contact-message" label="Message">
                    <textarea
                        id="sentinel-contact-message"
                        style={{ ...inputStyle, minHeight: 100, resize: 'vertical' }}
                        value={message}
                        onChange={(e) => setMessage(e.target.value)}
                        placeholder="Optional context — deadlines, specific rules you care about, stack constraints, etc."
                    />
                </Field>

                <div style={{ display: 'flex', alignItems: 'center', gap: 10, margin: '16px 0 20px', padding: 12, background: COLORS.bgMuted, borderRadius: 6 }}>
                    <input
                        type="checkbox"
                        id="includeContext"
                        checked={includeContext}
                        onChange={(e) => setIncludeContext(e.target.checked)}
                        style={{ cursor: 'pointer' }}
                    />
                    <label htmlFor="includeContext" style={{ fontSize: 14, color: COLORS.textPrimary, cursor: 'pointer', flex: 1 }}>
                        <strong>Include finding context</strong>
                        <span style={{ display: 'block', color: COLORS.textMuted, fontSize: 12, marginTop: 2 }}>
                            Sends full finding messages + locations so the quote is more accurate. Off by default because
                            finding text can reference internal paths and class names.
                        </span>
                    </label>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12 }}>
                    <Button
                        type={ButtonType.Button}
                        label={isPending ? 'Sending…' : 'Request quote'}
                        onClick={submitQuote}
                        size={ButtonSize.M}
                        color={ButtonColor.Primary}
                        disabled={!canSubmit}
                    />
                </div>
            </form>

            {result && <ResultPanel result={result} />}

            <footer style={{ marginTop: 20, fontSize: 12, color: COLORS.textMuted, textAlign: 'center' }}>
                Submissions go to <code>{initial.contactEndpoint}</code>.
            </footer>
        </div>
    );
};

// Accessible label/input pairing: the child must use htmlFor={id}, and the input/select/textarea
// the child is wrapping must carry the matching id. Avoids a div wrapper breaking the label ->
// for -> id chain and keeps screen readers announcing the field name when the control focuses.
const Field = ({ id, label, children }: { id: string; label: string; children: React.ReactNode }) => (
    <div style={{ marginBottom: 16 }}>
        <label htmlFor={id} style={labelStyle}>{label}</label>
        {children}
    </div>
);

const Row = ({ children }: { children: React.ReactNode }) => (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>{children}</div>
);

const ResultPanel = ({ result }: { result: SubmitResult }) => (
    <div
        style={{
            marginTop: 20,
            padding: 16,
            background: result.success ? '#F0FDF4' : '#FEF2F2',
            border: `1px solid ${result.success ? COLORS.success : COLORS.error}`,
            borderRadius: 8,
            color: result.success ? '#14532D' : '#7F1D1D',
            fontSize: 14,
        }}
    >
        <strong>{result.success ? 'Quote request sent' : 'Submission failed'}</strong>
        <div style={{ marginTop: 6, fontSize: 14 }}>
            {result.success
                ? `Server returned HTTP ${result.statusCode}. We'll be in touch at the email you provided within one business day.`
                : result.errorMessage ?? `Server returned HTTP ${result.statusCode}.`}
        </div>
        {result.message && <pre style={{ marginTop: 10, fontSize: 12, whiteSpace: 'pre-wrap', opacity: 0.85 }}>{result.message}</pre>}
    </div>
);
