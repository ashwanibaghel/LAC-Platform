"""Compile manually reviewed research annotations; never includes PDF text."""
import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

# Order number in corpus-candidates.json: source-header identity and research lesson.
ANNOTATIONS = {
1: ('W.P.(C) 3710/2026','2026-04-10','Affidavit authenticity and costs payable by petitioners are not LAC filing tasks.'),
2: ('W.P.(C) 1442/2015','2026-01-30','Separate possession/acquisition findings from compensation claims and party submissions.'),
3: ('W.P.(C) 2687/2018','2026-04-23','An operative reference-forwarding direction is distinct from the quoted earlier judgment and its old six-week deadline.'),
4: ('LA.APP. 59/2007 and connected','2025-09-26','Connected compensation appeals share one judgment; do not count the PDF filename as its primary case or duplicate the order.'),
5: ('W.P.(C) 6594/2014','2025-12-18','A counter-affidavit recording possession/deposit is a respondent statement; a preserved Section 18/30 remedy is not proof a reference was sent.'),
6: ('LA.APP. 460/2009','2025-04-24','Quoted valuation precedents and progressive rates must not become new paid-compensation facts.'),
7: ('LPA 454/2010 and connected','2026-01-09','Keep contractual lease resumption separate from statutory acquisition and Award compensation.'),
8: ('W.P.(C) 1784/2026','2026-04-06','Later court-recorded ADM hearings/status report support compliance; occupants vacating and GNCTD ex-gratia consideration have different actors.'),
9: ('W.P.(C) 9115/2023','2025-05-15','Vacating status quo and rejecting encroachment claims is a court finding; petitioner costs are not an LAC task.'),
10: ('W.P.(C) 17606/2025','2025-12-22','Revenue NOC/LSR circular and petitioner undertaking require source attribution, not invented LAC document directions.'),
11: ('LPA 56/2025','2025-01-23','Separate appellate treatment of an alternate-plot claim from the Scrutiny Committee decision quoted as background.'),
12: ('W.P.(C) 17415/2024','2024-12-18','An old LAC certificate records acquisition/possession; disposal does not create fresh Award or possession data.'),
13: ('LPA 543/2019','2022-12-22','Section 18 reference, enhanced compensation and appeal maintainability are distinct procedural layers.'),
14: ('CM(M) 1304/2023','2023-08-16','Execution deposit differential, beneficiary undertaking and executing-court release must retain their individual responsible actors.'),
15: ('W.P.(C) 7125/2022','2022-08-30','Petitioner denial of LAC possession is not a court finding that acquisition lapsed.'),
16: ('W.P.(C) 4247/2017','2023-02-07','South-West LAC reference limitation and supplemental structure compensation are not a fresh reference-forwarding direction.'),
17: ('W.P.(C) 13742/2019 and 2536/2020','2022-04-26','Actual LAC direction to publish Award for omitted ten biswas within twelve weeks; acquisition possession is separately recorded.'),
18: ('W.P.(C) 5155/2023','2023-09-04','Land-status report records an old quashed Award; present registration relief and historical acquisition must not be conflated.'),
19: ('W.P.(C) 12324/2018','2022-04-13','LAC reply affidavit concerning lapse is a respondent submission until independently adopted by Court.'),
20: ('RSA 143/2023','2023-09-27','LAC counter-affidavit and later DDA possession proceedings are different evidence sources and actors.'),
21: ('W.P.(C) 3485/2017','2022-09-19','Legal notice is not equivalent to valid representation; rejection/finality does not imply LAC compliance.'),
22: ('W.P.(C) 8214/2013 and connected','2024-12-24','Section 11A Award timing, consensual possession and statutory lapse must remain separate source-backed propositions.'),
23: ('W.P.(C) 5796/2026','2026-04-28','Reference to District Judge includes limitation decision; Section 30/31 payment history is not proof Section 18 forwarding happened.'),
24: ('W.P.(C) 787/2026','2026-02-02','Redetermination within three months is order-anchored; release six months thereafter is dependent, not nine months guessed from order.'),
25: ('W.P.(C) 8664/2021','2025-01-30','Forward reference preferably within four weeks from today; preserve preferential wording and distinguish knowledge-of-Award limitation.'),
26: ('W.P.(C) 11854/2018','2025-01-31','Excess land/demarcation and possession of acquired land require parcel-level attribution.'),
27: ('CO.PET. 39/2009 and 333/2010','2024-08-09','LAC Faridabad compensation and liquidator sale directions cannot be assigned to Delhi LAC merely by actor acronym.'),
28: ('W.P.(C) 7689/2000 and connected','2026-01-12','Later notification/decision expressly withdraws acquisition for specified Samalkha khasras; Award effect is a source finding, not canonical mutation.'),
29: ('CO.PET. 39/2009 and 333/2010','2024-05-10','LAC compensation remittance must be separated from DTCP approval and society/OL duties in the same lengthy order.'),
30: ('CO.PET. 39/2009 and connected','2024-10-04','Later compensation-release direction and OL valuation have different actors; upload timestamp is not order date.'),
31: ('W.P.(C) 6159/2024 and connected','2026-02-12','Petitioners may seek compensation shares; rejected lapse claim and recorded deposits do not determine each claimant payment.'),
32: ('W.P.(C) 706/2017','2022-03-29','Separate petitioner Section 24 prayers from operative conclusions and compensation/possession evidence.'),
33: ('W.P.(C) 8823/2017 and connected','2022-08-18','LAC SRP calculations and broken-period submissions are not Court-issued new computation facts without express adoption.'),
34: ('W.P.(C) 9506/2019','2022-11-22','Alternate-plot eligibility policy mentions compensation/possession but is not a fresh direction to pay compensation.'),
35: ('LPA 112/2020','2023-11-06','Impleadment in Section 30/31 apportionment differs from Section 18; title merits expressly left open must remain unknown.'),
36: ('W.P.(C) 7363/2015','2015-08-04','Notice/counter-affidavit filing deadline and next hearing can be extracted without assuming petitioner request was granted.'),
37: ('W.P.(C) 3163/2013','2019-10-11','Compensation plus interest direction to respondent five must not automatically be attributed to LAC.'),
38: ('W.P.(C) 1879/2022','2023-01-18','South-West LAC report contains conflicting Pochanpur khasra identifiers; preserve ambiguity rather than normalize silently.'),
39: ('W.P.(C) 2316/2016','2022-09-23','Alternate-plot documents/legal-heir requirements and acquisition history are not generic LAC tasks.'),
40: ('W.P.(C) 15821/2022','2022-11-18','Inherited compensation/ownership and alternate-plot eligibility remain separate propositions.'),
41: ('W.P.(C) 120/2026','2026-01-07','Section 30/31 ADJ reference direction follows disputed Gaon Sabha vesting; claimant ownership is not established by filing.'),
42: ('W.P.(C) 8611/2019 and connected','2023-02-23','Contradictory land-withdrawal stands lead to six-week affidavit direction; NTPC allegations are submissions.'),
43: ('W.P.(C) 568/2024','2026-04-23','LAC counter-affidavit says payment occurred; Court notes uncertainty and directs release of amounts due if any, not unconditional payment proof.'),
44: ('W.P.(C) 2759/2014','2026-03-23','Quoted Supreme Court fresh-acquisition time limit is historical; LAC confirms no initiation and present petition is disposed.'),
45: ('W.P.(C) 6339/2015 and connected','2016-02-23','DDA-to-LAC compensation transfer and payment-to-owner are different; inter-department dispute cannot silently become unpaid/paid canonical fact.'),
46: ('W.P.(C) 1784/2026','2026-02-09','ADM hearing/report and LAC notifications/Award documents have explicit duties; petitioner affidavits/court fees are not office actions.'),
47: ('W.P.(C) 1784/2026','2026-03-25','Old directions are reproduced before new events; documents handed over by LAC support only specifically recorded compliance.'),
49: ('W.P.(C) 7689/2000 with CONT.CAS(C) 191/2008 and connected','2025-03-21','Joint stakeholder/LG decision and later order must be linked by explicit recorded decision, not elapsed time.'),
50: ('CO.PET. 39/2009 and 333/2010','2024-02-27','Faridabad LAC appearance/remittance concerns coexist with quoted earlier DTCP/OL directions; avoid re-anchoring historical deadlines.'),
51: ('CO.PET. 39/2009 and 333/2010','2024-04-05','Counsel communication to LAC is not compensation paid; express nonappearance and conditional future direction are distinct.'),
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('study')
    args = parser.parse_args()
    study = json.loads(Path(args.study).read_text(encoding='utf-8'))
    by_url = {x['officialUrl']: x for x in study if 'sha256' in x}
    from study_corpus import official_url
    candidates = json.loads(Path(__file__).with_name('corpus-candidates.json').read_text())
    orders = []
    for number, (case, date, lesson) in ANNOTATIONS.items():
        url = official_url(candidates[number-1])
        source = by_url.get(url)
        if not source:
            raise SystemExit(f'Studied source unavailable in verification cache: {number}')
        orders.append({'caseNumber': case, 'orderDate': date, 'officialUrl': url,
                       'sha256': source['sha256'], 'pageCount': source['pageCount'],
                       'relevantPages': [p['page'] for p in source['relevantPages']],
                       'topics': sorted({t for p in source['relevantPages'] for t in p['topics']}),
                       'officeLesson': lesson})
    if len({x['sha256'] for x in orders}) != 50:
        raise SystemExit('Expected 50 distinct studied official PDFs')
    manifest = {'version': '1', 'studiedAt': datetime.now(timezone.utc).isoformat(),
                'method': 'Native text/page inspection and manual office-semantics annotations; no inference/OCR',
                'pdfsRetained': False, 'orders': orders}
    Path(__file__).with_name('corpus-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')

if __name__ == '__main__':
    main()
