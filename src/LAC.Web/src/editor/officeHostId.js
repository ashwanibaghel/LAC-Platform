// This intentionally lives only for the lifetime of the loaded web module.
// An ONLYOFFICE host id needs DOM uniqueness, not cryptographic randomness.
let officeHostSequence = 0;

export function createOfficeHostId(draftId) {
  return `office-${draftId}-${++officeHostSequence}`;
}
