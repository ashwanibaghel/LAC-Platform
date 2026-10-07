# Compensation calculator backend

Base: `8a02466df4ef96ba585cb2cd1e700f0a55f11a50`.

This calculator executes user-supplied assumptions. Rates, factors, solatium, interest
percentages and interest bases are inputs, not legal entitlement decisions. The
stateless domain service has no database, network, clock, storage or workflow
dependencies. It creates no history, Award, Compensation or Land records.

## API

`POST /api/calculators/compensation/compute`, with `Content-Type: application/json`
and the existing local LAC session cookie. No new role, permission or workflow
allocation is required. The route is registered outside the workflow allocation
group and uses the existing cookie authentication/active-session validation.
RBAC is unchanged. Responses have `Cache-Control: no-store`.

```json
{
  "conversionProfile": "lac-delhi-v1",
  "land": { "area": "18", "unit": "bigha", "equivalentAreaInRateUnit": "3.744" },
  "marketRate": { "amount": "5300000", "perUnit": "acre" },
  "multiplicationFactor": "2",
  "assets": { "treesAndStructures": "0" },
  "solatium": { "percent": "100" },
  "additionalAmount": {
    "type": "Interest",
    "annualRatePercent": "12",
    "basis": "MarketValue",
    "duration": { "mode": "Days", "value": "30" }
  }
}
```

Decimal inputs accept JSON numbers or invariant decimal strings. Exponents,
non-finite numbers, separators and excess precision are rejected before decimal
parsing can silently round. Inputs have at most 12 fractional digits, magnitude
at most 10^12, and must be non-negative; rate and factor must be positive.
Intermediate/results are bounded to magnitude 10^24; arithmetic overflow returns
400. Nonzero products/quotients that underflow to zero also return 400.
Omitted values in the required land/rate/factor/assets/solatium fields return
field validation errors. Zero area is valid. Zero interest/duration is valid.

`additionalAmount` is required. Its `type` defaults to `Interest` and `basis` to
`MarketValue`. Enum values are strings (integer enum values are rejected).
Unknown/duplicate JSON fields are rejected. Maximum JSON depth is 16 and body
size is 16,384 bytes, including requests without a Content-Length header.

Interest accepts `annualRatePercent`, a required `duration`, and one of
`MarketValue`, `FactorAdjustedValue`, `BaseCompensation`, `AmountAfterSolatium`.
It rejects `formula`. Duration shapes:

```json
{ "mode": "Days", "value": "30" }
{ "mode": "Months", "value": "1.5" }
{ "mode": "DateRange", "startDate": "2026-01-01", "endDate": "2026-01-31" }
```

Days use `days / 365`; Months use `months / 12`. Fractional days/months are
permitted. DateRange uses exact DateOnly day-number subtraction, start inclusive,
end exclusive, with a fixed 365-day year including leap years. A same-day range
is zero days. Dates are not accepted in Days/Months, and `value` is not accepted
in DateRange. End dates before start dates are rejected. The applied convention,
count and decimal duration fraction are returned.

Other accepts `formula` and an optional duration:

```json
{
  "type": "Other",
  "formula": "MARKET_VALUE * 12 / 100 * (DAYS / 365)",
  "duration": { "mode": "Days", "value": "30" }
}
```

Other rejects an annual interest rate or a non-default interest basis. Additional
amounts must be non-negative. This is the Additional Amount subtype, with no
separate interest endpoint or UI changes.

Success returns strongly typed `CompensationResponse`: currency, entered and
converted/applied area, profile, rate, factor, solatium percentage, all compensation
stages, additional amount type/basis/rate/duration/formulas/substitutions/variables,
final amount, Indian words, rounding policy, and ordered calculation trace.
Every money value is `{ "precise": "...", "display": "..." }`; all calculated
decimal quantities in responses are strings to preserve client precision. The
trace contains names, formulas, substituted formulas and unrounded results.

Ordinary invalid input returns 400 `application/problem+json` with an `errors`
map keyed by the field, for example:

```json
{ "status": 400, "errors": { "additionalAmount.formula": ["Division by zero is not allowed."] } }
```

Missing/revoked authentication returns 401, oversized requests 413, and non-JSON
content types 415. No calculation input is logged by the calculator.

## Area source and parity

The existing Global Area Calculator source is
`src/LAC.Web/src/calculator/landConversions.js`, not a backend conversion service.
`CalculatorAreaConversion` ports its scalar area-to-square-metres-to-rate-unit
semantics into decimal arithmetic. Unit IDs and factors are preserved:

| Unit | Square metres per unit |
| --- | ---: |
| bigha | 843 |
| biswa | 42.15 |
| biswansi | 2.1075 |
| sqm | 1 |
| sqyd | 0.83612736 |
| sqft | 0.09290304 |
| hectare | 10000 |
| acre | 4046.8564224 |
| sqkm | 1000000 |

`lac-delhi-v1` is the only accepted profile, not a universal Bigha definition.
Unknown profiles/units are rejected. The frontend is unchanged. Tests read its
actual source factors and verify all 81 source/target pairs. This scalar
compensation contract does not accept a revenue shorthand/triplet. Its revenue
ratios remain 1 Bigha = 20 Biswa = 400 Biswansi. Published rounded reference
values from the frontend footer are not combined with the conversion graph.

The accepted graph converts 18 Bigha to `18 * 843 / 4046.8564224` Acre, approximately
3.749577 Acre. It does **not** yield the office example's 3.744 Acre. With the user's
approval, `land.equivalentAreaInRateUnit` allows the explicitly supplied 3.744 Acre
assumption. This is an optional entered equivalent, not a second conversion
profile or silently changed constant. The response always returns both the
profile-converted area and applied area and flags the override. The trace labels
it a user-supplied assumption. It must preserve zero area and must agree with the
entered area when the units are identical. Without it, profile conversion is used.
The office result can also be obtained by entering 3.744 Acre directly.

## Safe formula grammar

```ebnf
expression = product, { ("+" | "-"), product } ;
product    = atom, { ("*" | "/"), atom } ;
atom       = number | variable | "(", expression, ")" | ("+" | "-"), atom ;
number     = digit, { digit }, [ ".", digit, { digit } ] ;
digit      = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
```

Whitespace is ignored; precedence and left associativity are conventional.
Variables are case-sensitive uppercase identifiers from this closed list:

| Variable | Meaning |
| --- | --- |
| AREA | Applied area in marketRate.perUnit (includes explicit equivalent if supplied) |
| RATE | Market rate amount per selected unit |
| MARKET_VALUE | AREA * RATE |
| FACTOR | Multiplication factor |
| FACTOR_VALUE | Market Value * factor |
| ASSET_VALUE | Trees and structures |
| BASE_COMPENSATION | Factor Adjusted Value + assets |
| SOLATIUM_PERCENT | Supplied percentage |
| SOLATIUM_AMOUNT | Base Compensation * percentage / 100 |
| AFTER_SOLATIUM | Base Compensation + Solatium Amount |
| DAYS | Supplied Days count or exact DateRange count, available only with that duration |
| MONTHS | Supplied Months count, available only with that duration |

The engine never invents a days/months conversion. Missing duration variables
are rejected. `ANNUAL_RATE_PERCENT` appears in the built-in Interest trace only;
it is not a custom formula variable.

Limits: 2,048 formula characters, 256 tokens, 32 nested parentheses/unary levels,
and the same bounded decimal literals as inputs. The normalized output is fully
parenthesized, with canonical decimal literals; a substituted formula and values
are returned separately. Unknown identifiers, functions, strings, property
access, exponent notation/operators, implicit multiplication, malformed
expressions, division by zero, overflow and excessive complexity are rejected.
No eval, dynamic compilation, scripts, SQL, shell, reflection or network are used
for expression evaluation.

## Precision and rounding

All arithmetic uses System.Decimal, with its finite 28–29 significant-digit
representation. Recurring divisions (including days/365 and acre conversion)
are retained to Decimal's available precision; `precise` means the unrounded
decimal calculation result, not an unlimited-precision rational number.
Interest computes `basis * annualRatePercent / 100 * durationFraction`.
There is no intermediate money display rounding. Each display independently
rounds its precise value to two places, midpoint away from zero. The final
display rounds the sum of unrounded stages, rather than adding displayed stages.
Words use the final two-place amount, including paise carry. Crore/lakh/thousand
and paise boundaries are tested; larger amounts recursively use Crore groups.

## Office example

Using the request above, with the explicitly supplied equivalent 3.744 Acre:

| Stage | Display INR |
| --- | ---: |
| Market Value | 19843200.00 |
| Factor Adjusted Value | 39686400.00 |
| Trees/structures | 0.00 |
| Base Compensation | 39686400.00 |
| Solatium Amount | 39686400.00 |
| Amount After Solatium | 79372800.00 |
| Additional Amount: Interest | 195713.75 |
| Final Compensation | 79568513.75 |

Additional formula:
`19843200 * 12 / 100 * (30 / 365)`.

The unrounded Interest value is `195713.75342465753424657534249` and unrounded
final value is `79568513.75342465753424657534`. The complete response, including
conversion and duration precision and every trace step, is in
[`compensation-calculator-sample.json`](compensation-calculator-sample.json).

Words: **Rupees Seven Crore Ninety Five Lakh Sixty Eight Thousand Five Hundred
Thirteen and Seventy Five Paise Only**.

## Verification and scope

New engine and API tests are in `CompensationCalculatorTests.cs` and
`CompensationCalculatorApiTests.cs`. API tests use real local cookie authentication
and an EF SaveChanges interceptor to prove that successful/repeated/invalid
computations invoke no database writes (normal login/startup are measured
separately). Parallel calls prove deterministic service behavior.

Run the new tests and the complete existing .NET backend suite:

```text
dotnet test tests/LAC.Tests/LAC.Tests.csproj --filter FullyQualifiedName~CompensationCalculator
dotnet test tests/LAC.Tests/LAC.Tests.csproj
git diff --check
```

Validation on the exact base: **973 passed, 22 skipped, 0 failed (995 total)**.
New calculator tests: **208 passed, 0 skipped, 0 failed**, comprising **179 engine
tests** (including all 81 conversion pairs) and **29 API tests**. The API project
build passed with no compiler warnings or errors. Existing xUnit analyzer
warnings in AwardExtractionRuleEngineTests are unchanged.

Final complete backend suite: **1,181 passed, 22 skipped, 0 failed (1,203 total)**.
All 995 existing test cases and their 22 environment-dependent skips are preserved;
the increase is exactly the 208 new calculator cases. `git diff --check` passed.
TRX evidence is retained locally under the ignored `out/compensation-tests` folder.

No database tables or migrations are added. No Award/Land/Court implementation,
frontend, office deployment/runtime scripts or other workflows are changed.
The two Program additions register the pure service and authenticated calculator
route. No merge or deployment is part of this change.
