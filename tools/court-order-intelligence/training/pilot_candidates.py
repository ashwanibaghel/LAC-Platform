"""Public official source candidates, not gold or frozen split assignments."""
CANDIDATES = [
    ("wpc6384-2024", "W.P.(C) 6384/2024", "1784265498_d3f53f18dc58e8f0_593_63842024.pdf/2026"),
    ("cont659-2018", "CONT.CAS(C) 659/2018", "1761741491_0a09f00174bd30bf_591_6592018.pdf/2025"),
    ("lpa543-2026", "LPA 543/2026", "1787380261_3c4a9b15bc5245a3_683_5432026.pdf/2026"),
    ("wpc6328-2026", "W.P.(C) 6328/2026", "1778324702_50f27990d32c835d_svn_63282026.pdf/2026"),
    ("wpc6203-2026", "W.P.(C) 6203/2026", "1787577436_ae76a2a50a36e1ad_pms_62032026.pdf/2026"),
    ("wpc8971-2025", "W.P.(C) 8971/2025", "1776957395_9d9290feb79c659b_svn_89712025.pdf/2026"),
    ("cont527-2018", "CONT.CAS(C) 527/2018", "1766569429_189437e82a61701e_591_5272018.pdf/2025"),
    ("wpc11284-2026", "W.P.(C) 11284/2026", "1786543486_e106db2cd86fb862_svn_112842026.pdf/2026"),
    ("cs147-2022", "CS(OS) 147/2022", "1774251281_1f4a65821cb11c71_smp_1472022.pdf/2026"),
    ("cs23-2025", "CS(OS) 23/2025", "1784107404_a3557268331cce0c_743_232025.pdf/2026"),
    ("wpc16669-2024", "W.P.(C) 16669/2024", "1766061126_e26b7e27853188c9_683_166692024.pdf/2025"),
]


if __name__ == "__main__":
    import json
    from acquire_pilot_sources import ROOT, acquire, write_json
    for key, case, path in CANDIDATES:
        destination = ROOT / "local-private/pilot-source-audit" / (key + ".json")
        if destination.exists():
            print(json.dumps({"case": case, "state": "already acquired"}), flush=True)
            continue
        try:
            record = acquire(case, "https://delhihighcourt.nic.in/app/showlogo/" + path)
            write_json(destination, record)
            print(json.dumps({"case": case, "pages": len(record["pages"]), "state": "UNREVIEWED"}), flush=True)
        except Exception as error:
            print(json.dumps({"case": case, "state": "acquisition failed", "error_class": type(error).__name__}), flush=True)
