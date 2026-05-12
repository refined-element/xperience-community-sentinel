// Kentico's admin loader scans this file for named exports whose names end in "Template".
// A C# page with `templateName: "@xperiencecommunity/sentinel-admin/Dashboard"` resolves to the
// exported member `DashboardTemplate` — the runtime appends "Template" automatically.
//
// Use named re-exports (not default) because the loader reads them from the module namespace.
export * from './dashboard/DashboardTemplate';
export * from './contact/ContactTemplate';
export * from './settings/SettingsTemplate';
export * from './scan-detail/ScanDetailTemplate';
export * from './diff/DiffTemplate';
