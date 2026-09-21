/**
 * companies.js — tiny inlined dataset shared by the directory and detail
 * pages. Inlined (rather than fetched) so the whole demo works when opened
 * directly over file:// with no server for the static files.
 *
 * IDs are intentionally sequential ("company-1001", "company-1002", …) so the
 * directory shows organic browsing while the Scenario Lab can demonstrate the
 * sequential-ID enumeration signal by walking a nearby range the UI never
 * links to.
 */
window.COMPANIES = [
  { id: 'company-1001', name: 'Aurora Logistics Kft.', sector: 'Freight & Warehousing', employees: 420, city: 'Budapest' },
  { id: 'company-1002', name: 'Béke Manufacturing Zrt.', sector: 'Industrial Equipment', employees: 1180, city: 'Győr' },
  { id: 'company-1003', name: 'Concord Energy Nyrt.', sector: 'Renewable Energy', employees: 260, city: 'Debrecen' },
  { id: 'company-1004', name: 'Danube Foods Kft.', sector: 'Food & Beverage', employees: 815, city: 'Szeged' },
  { id: 'company-1005', name: 'Elmwood Software Zrt.', sector: 'Software', employees: 145, city: 'Budapest' },
  { id: 'company-1006', name: 'Faragó Construction Kft.', sector: 'Construction', employees: 970, city: 'Pécs' },
];
