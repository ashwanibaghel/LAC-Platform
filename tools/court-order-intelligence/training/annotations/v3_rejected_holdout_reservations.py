"""Unsafe UNFROZEN reservations are quarantined, not moved into TRAIN.

These are the already-discovered relationships accepted in the course
correction. Do not discard existing independently frozen TRAIN merely because
an unsafe fresh holdout was proposed. Replacement holdouts still need review,
group audits and immutable freeze. No model outputs were inspected.
"""
REJECTED = {
    'wpc17105-2025': 'Shares Okaya/Jeevantika authorities and prior TRAIN group; replace validation reservation.',
    'wpc10546-2023': 'Judgment expressly connected to existing TRAIN contempt 1094/2026; replace blind reservation.',
    'cont724-2021': 'Shares BSK authority with prior/protected source family; replace blind reservation.',
    'wpc17094-2025': 'Same Harender petitioner/Award 02/2024/SW chain as validation 12473/2025; replace blind reservation.',
}
