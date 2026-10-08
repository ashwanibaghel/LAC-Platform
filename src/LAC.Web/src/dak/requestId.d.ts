export function createDakRequestId(cryptoSource?: Pick<Crypto, "getRandomValues"> & Partial<Pick<Crypto, "randomUUID">>): string;
