"""Explicit read-only local inventory of known public sources. No discovery/inference."""
import json
import os
from urllib.parse import urlsplit
import psycopg


def official(url):
    if not url: return False
    uri = urlsplit(url)
    return uri.scheme=='https' and uri.hostname=='delhihighcourt.nic.in' and not uri.query and not uri.fragment


def main():
    config = dict(part.split('=',1) for part in os.environ['ConnectionStrings__DefaultConnection'].split(';') if '=' in part)
    with psycopg.connect(host=config.get('Host','localhost'),port=config.get('Port',5432),
                         dbname=config['Database'],user=config.get('Username'),password=config.get('Password')) as connection:
        connection.read_only = True
        with connection.cursor() as cursor:
            cursor.execute('''SELECT c."Id", c."CaseNumber", c."CurrentStatus", r."RawLastOrderLink"
                FROM public."CourtCases" c LEFT JOIN public."CourtImportRows" r ON r."CommittedCourtCaseId"=c."Id"
                WHERE lower(c."CourtName")='delhi high court' AND lower(c."CurrentStatus")='pending'
                  AND c."RecordStatus"::text IN ('0','Active') ''')
            matters = {}
            for case_id, number, status, url in cursor.fetchall():
                matter = matters.setdefault(str(case_id),{'caseId':str(case_id),'caseNumber':number,'registerStatus':status,'knownSources':[]})
                if official(url) and not any(source['officialUrl']==url for source in matter['knownSources']):
                    matter['knownSources'].append({'officialUrl':url,'orderDate':None,'basis':'Office import source link; date/caption not yet verified'})
            cursor.execute('''SELECT "CourtCaseId","OrderDate","OfficialUrl" FROM public."CourtExternalOrderObservations"''')
            for case_id, day, url in cursor.fetchall():
                matter = matters.get(str(case_id))
                if matter and official(url) and not any(source['officialUrl']==url for source in matter['knownSources']):
                    matter['knownSources'].append({'officialUrl':url,'orderDate':str(day) if day else None,'basis':'Existing official order observation; PDF caption still independently verified'})
    print(json.dumps({'readOnly':True,'activePendingMatters':len(matters),'matters':list(matters.values())},indent=2))


if __name__=='__main__': main()
