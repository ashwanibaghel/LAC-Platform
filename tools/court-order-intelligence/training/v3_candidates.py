"""Pre-review reservations, not verified matters/gold or complete order chains.

Connections/duplicate sources can only exclude/merge within a reservation, never
move protected content to train. Five candidate matters per holdout are reserved
before native review or model outputs; actual independent coverage is unproven.
"""
CANDIDATES = [
    ('wpc10338-2015', 'W.P.(C) 10338/2015', 'train'),
    ('wpc10341-2015', 'W.P.(C) 10341/2015', 'train'),
    ('wpc10340-2015', 'W.P.(C) 10340/2015', 'train'),
    ('wpc1069-2025', 'W.P.(C) 1069/2025', 'train'),
    ('wpc5997-2024', 'W.P.(C) 5997/2024', 'train'),
    ('wpc3352-2024', 'W.P.(C) 3352/2024', 'train'),
    ('wpc3351-2024', 'W.P.(C) 3351/2024', 'train'),
    ('wpc5201-2024', 'W.P.(C) 5201/2024', 'train'),
    ('wpc5202-2024', 'W.P.(C) 5202/2024', 'train'),
    ('wpc5198-2024', 'W.P.(C) 5198/2024', 'train'),
    ('wpc6760-2014', 'W.P.(C) 6760/2014', 'train'),
    ('wpc13738-2025', 'W.P.(C) 13738/2025', 'train'),
    ('wpc13202-2025', 'W.P.(C) 13202/2025', 'train'),
    ('wpc2733-2025', 'W.P.(C) 2733/2025', 'validation'),
    ('wpc12473-2025', 'W.P.(C) 12473/2025', 'validation'),
    ('wpc17105-2025', 'W.P.(C) 17105/2025', 'validation'),
    ('cont191-2008', 'CONT.CAS(C) 191/2008', 'validation'),
    ('cont777-2019', 'CONT.CAS(C) 777/2019', 'validation'),
    ('wpc13941-2025', 'W.P.(C) 13941/2025', 'blind'),
    ('wpc17094-2025', 'W.P.(C) 17094/2025', 'blind'),
    ('wpc5489-2022', 'W.P.(C) 5489/2022', 'blind'),
    ('wpc10546-2023', 'W.P.(C) 10546/2023', 'blind'),
    ('cont724-2021', 'CONT.CAS(C) 724/2021', 'blind'),
]
