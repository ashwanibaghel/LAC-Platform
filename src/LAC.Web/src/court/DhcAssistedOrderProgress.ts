type OrderProgressItem = { status: string; failureCode: string | null };

// Status-phase reviews never prove an order search. These order-specific codes
// are set only after the official order response is processed by the backend.
export const countCompletedOrderSearches = (items: OrderProgressItem[]): number =>
  items.filter(item => item.status === "Completed" ||
    item.status === "NeedsReview" &&
    ["OrderIdentityMismatch", "OrderDateNeedsReview"].includes(item.failureCode ?? "")).length;
