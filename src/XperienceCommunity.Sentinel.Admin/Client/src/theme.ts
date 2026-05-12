// Shared visual tokens for every Sentinel admin template. One source of truth beats five
// drifting COLORS objects — a palette change used to mean touching Dashboard, Scan detail,
// Diff, Settings, and Contact in lockstep.
//
// Contrast calibration (2026-04-22): the "lime" accent was #D6F08D which renders with poor
// contrast against Kentico admin's light chrome — operators reported text in lime blocks
// was hard to read. Darkened the background lime to #B8D870 (formerly limeDark) and promoted
// an even darker #5A7D1F for text/interactive elements that need to pass AA against white.
// Neutral text bumped one step darker so the default body copy reads against bgMuted panels.

export const THEME_COLORS = {
    // Accent family. Use `lime` for soft fills (selected states, highlights), `limeDark` for
    // hover / pressed states, and `limeText` for any colored text / link / icon that needs
    // actual contrast.
    lime: '#B8D870',
    limeDark: '#9BBC53',
    limeText: '#5A7D1F',

    // Neutral family.
    bg: '#FFFFFF',
    bgMuted: '#F4F4F6',
    border: '#D9DBE1',
    textPrimary: '#111827',
    textMuted: '#4B5563',

    // Severity / state family (kept Kentico-adjacent so dashboards read at a glance).
    error: '#B91C1C',
    warning: '#B45309',
    info: '#4B5563',
    success: '#15803D',

    // Diff-specific.
    introduced: '#B45309',
    resolved: '#15803D',
} as const;

// Typography scale. Admin body copy was 13px which runs small against Kentico's 15px
// chrome; bumping to 14px everywhere adds readability without making the dashboard feel
// cramped. Headings and section labels scale proportionally.
export const THEME_FONT = {
    body: 14,
    bodySmall: 13,
    label: 12,
    code: 13,
    h1: 26,
    h2: 20,
    h3: 15,
} as const;
