"""Public source candidates only; no gold labels or private workbook content.

TRAIN candidates selected before native-source inspection. Two independent
groups reserved for validation and three for blind before their source acquisition.
Relationships found during audit may only merge groups or exclude candidates,
never move a protected source into training.
"""
CANDIDATES = [
 ('wpc6108-2015', 'W.P.(C) 6108/2015', 'train', '1774612415_ac4e049598da08ce_670_61082015.pdf/2026'),
 ('wpc4255-2016', 'W.P.(C) 4255/2016', 'train', '1775818888_71d2234172a3ccc0_670_42552016.pdf/2026'),
 ('wpc4806-2014', 'W.P.(C) 4806/2014', 'train', '1770647202_0b3b6337f7d44c3b_755_48062014.pdf/2026'),
 ('wpc2085-2024', 'W.P.(C) 2085/2024', 'train', '1774876405_ecbd1b243e3f94ec_683_20852024.pdf/2026'),
 ('wpc9889-2024', 'W.P.(C) 9889/2024', 'train', '1765800083_5aae0144a82b657e_597_98892024.pdf/2025'),
 ('wpc12994-2023', 'W.P.(C) 12994/2023', 'train', '1768488074_31d1b7fdca9f4820_755_129942023.pdf/2026'),
 ('wpc5084-2018', 'W.P.(C) 5084/2018', 'train', '1787837833_bdd90bd4752e7cba_pms_50842018.pdf/2026'),
 ('wpc9093-2022', 'W.P.(C) 9093/2022', 'train', '1787665216_627ab2f60ed25c6e_pms_90932022.pdf/2026'),
 ('wpc13932-2025', 'W.P.(C) 13932/2025', 'train', '1787567200_6d1a0db3d55c9639_593_139322025.pdf/2026'),
 ('wpc14785-2023', 'W.P.(C) 14785/2023', 'train', '1788349036_3b3f88343fe81b6a_593_147852023.pdf/2026'),
 ('wpc4644-2024', 'W.P.(C) 4644/2024', 'train', '1771934314_d5773198fb29b5a7_pms_46442024.pdf/2026'),
 ('wpc16215-2023', 'W.P.(C) 16215/2023', 'validation', '1787402499_b853653b4908d29a_svn_162152023.pdf/2026'),
 ('wpc5993-2024', 'W.P.(C) 5993/2024', 'validation', '1780317428_1904f033bba4f6c6_pms_59932024.pdf/2026'),
 ('wpc847-2025', 'W.P.(C) 847/2025', 'blind', '1774699458_87b57142fbc5a6be_596_8472025.pdf/2026'),
 ('wpc3282-2024', 'W.P.(C) 3282/2024', 'blind', '1771069350_d3ae9c9f70d9ee00_jsm_32822024.pdf/2026'),
 ('wpc10105-2023', 'W.P.(C) 10105/2023', 'blind', '1788259742_ff30d8d1bd699375_pms_101052023.pdf/2026'),
 ('wpc10105-2023-may2025', 'W.P.(C) 10105/2023', 'blind', '70903455_1747909777_670_101052023.pdf/2025'),
 # Expanded user-supplied candidate pool. Reservations precede native review.
 # Only public official HTTPS URLs; private email/session links are excluded.
 ('cont1094-2026', 'CONT.CAS(C) 1094/2026', 'train', '1783165046_1a3ff4ffd7f6d48f_592_10942026.pdf/2026'),
 ('cont490-2026', 'CONT.CAS(C) 490/2026', 'train', '1780240993_96e9dffcf13384d9_587_4902026.pdf/2026'),
 ('cont615-2019', 'CONT.CAS(C) 615/2019', 'train', '1777975637_618e633dcf0c239a_598_6152019.pdf/2026'),
 ('cont1779-2025', 'CONT.CAS(C) 1779/2025', 'train', '1764164740_873419d739b51e22_598_17792025.pdf/2025'),
 ('cont418-2025', 'CONT.CAS(C) 418/2025', 'train', '1784191545_3e4f55daf06c51cb_592_4182025.pdf/2026'),
 ('wpc14846-2024', 'W.P.(C) 14846/2024', 'train', '1771329942_823588d7b3da6296_755_148462024.pdf/2026'),
 ('wpc5951-2025', 'W.P.(C) 5951/2025', 'train', '1777555100_181f30cb7fa3dcf3_svn_59512025.pdf/2026'),
 ('wpc1157-2026', 'W.P.(C) 1157/2026', 'train', '1784799287_d5c6c8004d4ba98f_670_11572026.pdf/2026'),
 ('wpc15528-2025', 'W.P.(C) 15528/2025', 'train', '1760168238_9e3e6f80418a0c21_prj_155282025.pdf/2025'),
 ('wpc5645-2022', 'W.P.(C) 5645/2022', 'train', '1778234502_f72aa58932693dbf_755_56452022.pdf/2026'),
 ('wpc6504-2023', 'W.P.(C) 6504/2023', 'validation', '1783346164_fccae4e9aa03da20_593_65042023.pdf/2026'),
 ('wpc3238-2023', 'W.P.(C) 3238/2023', 'validation', '1762855093_63d95fc6469039b0_748_32382023.pdf/2025'),
]

PREFIX = 'https://delhihighcourt.nic.in/app/showlogo/'

# Actual earlier order dates expressly identified in acquired official text.
# These GET downloads are not search-form/CAPTCHA submission or guessed hearings.
FOLLOWUPS = [
 ('wpc6108-2015-feb25', 'W.P.(C) 6108/2015', 'train', '6108/2015/25-02-2026'),
 ('wpc4255-2016-feb25', 'W.P.(C) 4255/2016', 'train', '4255/2016/25-02-2026'),
 ('wpc4806-2014-apr2022', 'W.P.(C) 4806/2014', 'train', '4806/2014/11-04-2022'),
 ('wpc4806-2014-feb2020', 'W.P.(C) 4806/2014', 'train', '4806/2014/04-02-2020'),
 ('wpc2085-2024-dec2025', 'W.P.(C) 2085/2024', 'train', '2085/2024/16-12-2025'),
 ('wpc9093-2022-feb09', 'W.P.(C) 9093/2022', 'train', '9093/2022/09-02-2026'),
 ('wpc13932-2025-feb03', 'W.P.(C) 13932/2025', 'train', '13932/2025/03-02-2026'),
 ('wpc13932-2025-sep2025', 'W.P.(C) 13932/2025', 'train', '13932/2025/10-09-2025'),
 ('wpc6108-2015-nov2025', 'W.P.(C) 6108/2015', 'train', '6108/2015/11-11-2025'),
 # Actual indexed official order, not an inferred cause-list/hearing date.
 ('wpc10105-2023-dec2024', 'W.P.(C) 10105/2023', 'blind', '10105/2023/06-12-2024'),
 # Dates discovered in public indexes; native official identity/date must pass audit.
 ('wpc10105-2023-sep2023', 'W.P.(C) 10105/2023', 'blind', '10105/2023/04-09-2023'),
 ('wpc10105-2023-jul2025', 'W.P.(C) 10105/2023', 'blind', '10105/2023/11-07-2025'),
 ('wpc3282-2024-sep2026', 'W.P.(C) 3282/2024', 'blind', '3282/2024/07-09-2026'),
 # Public indexes/cause lists provide discovery candidates, NOT proof that an
 # order exists. Failed GET/non-PDF remains unavailable; only native caption
 # plus actual ORDER date can promote a fetched candidate to source review.
 ('wpc5084-2018-nov2020', 'W.P.(C) 5084/2018', 'train', '5084/2018/24-11-2020'),
 ('wpc9093-2022-apr2024', 'W.P.(C) 9093/2022', 'train', '9093/2022/08-04-2024'),
 ('wpc6504-2023-dec2024', 'W.P.(C) 6504/2023', 'validation', '6504/2023/04-12-2024'),
 ('wpc6504-2023-may2026', 'W.P.(C) 6504/2023', 'validation', '6504/2023/07-05-2026'),
 ('wpc6504-2023-oct2024', 'W.P.(C) 6504/2023', 'validation', '6504/2023/23-10-2024'),
 ('wpc9093-2022-jul2024', 'W.P.(C) 9093/2022', 'train', '9093/2022/30-07-2024'),
 ('wpc5084-2018-jan2021', 'W.P.(C) 5084/2018', 'train', '5084/2018/29-01-2021'),
]
