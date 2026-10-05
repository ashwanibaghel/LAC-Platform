import test from "node:test";
import assert from "node:assert/strict";
import { officeCalendarDate } from "../src/dak/officeDate.js";

test("Dak entry uses Delhi date across the IST midnight boundary", () => {
  assert.equal(officeCalendarDate(new Date("2026-10-05T18:29:59Z")), "2026-10-05");
  assert.equal(officeCalendarDate(new Date("2026-10-05T18:30:00Z")), "2026-10-06");
  assert.equal(officeCalendarDate(new Date("2026-10-05T23:59:59Z")), "2026-10-06");
});
