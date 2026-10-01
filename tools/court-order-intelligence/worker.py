"""Explicit out-of-process, sequential native-PDF Court intelligence worker."""
import argparse
import hashlib
import json
import os
import re
import tempfile
import uuid
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

import fitz
import requests
import jsonschema
from provider import LlamaCppProvider
from semantics import VERSION, identity, normalized, dates_in, synthesize
from anchors import anchors_for, expand, schema_for, INSTRUCTIONS as ANCHOR_INSTRUCTIONS

class StopRequested(BaseException):
    pass

def check_stop(stop_file):
    if stop_file and Path(stop_file).is_file():
        raise StopRequested()

def download(url, directory):
    uri = urlsplit(url)
    if (uri.scheme != 'https' or uri.hostname != 'delhihighcourt.nic.in'
            or uri.port not in (None, 443) or uri.username or uri.password or uri.query or uri.fragment
            or not uri.path.startswith(('/app/showFileJudgment/', '/app/showlogo/',
                                        '/app/case_number_pdf/', '/app/downloadOrderbByDate/'))):
        raise ValueError('Only direct, query-free official DHC PDF sources are permitted')
    session = requests.Session()
    session.trust_env = False
    path = Path(directory) / 'order.pdf'
    digest = hashlib.sha256()
    try:
        with session.get(url, stream=True, timeout=(10, 90), allow_redirects=False) as response:
            if response.status_code != 200:
                raise ValueError('Official direct PDF unavailable')
            size = 0
            with path.open('wb') as output:
                for chunk in response.iter_content(65536):
                    size += len(chunk)
                    if size > 30 * 1024 * 1024:
                        raise ValueError('Official PDF exceeds bounded size')
                    output.write(chunk)
                    digest.update(chunk)
        with path.open('rb') as stream:
            if stream.read(5) != b'%PDF-':
                raise ValueError('Official source is not a PDF')
        return path, digest.hexdigest()
    finally:
        session.close()

def native_pages(path):
    pages = {}
    with fitz.open(path) as document:
        if document.is_encrypted or len(document) > 200:
            raise ValueError('NeedsSourceReview: encrypted or excessive-page source')
        for number, page in enumerate(document, 1):
            text = page.get_text(sort=True)
            if len(re.sub(r'[^A-Za-z]', '', text)) < 100:
                raise ValueError(f'NeedsSourceReview: page {number} has insufficient native text; no OCR used')
            pages[number] = normalized(text)
    return pages

def process_order(source, case_number, provider, temporary_root=None, downloader=download, stop_file=None):
    record = {'officialUrl': source['officialUrl'], 'orderDate': source.get('orderDate'),
              'caseNumber': case_number, 'sha256': None, 'status': 'NeedsReview', 'facts': [],
              'versions': {'extraction': VERSION, 'rulebook': '1', 'model': provider.version},
              'processedAt': datetime.now(timezone.utc).isoformat(), 'coverage': {}}
    try:
        with tempfile.TemporaryDirectory(prefix='lac-court-order-', dir=temporary_root) as temporary:
            check_stop(stop_file)
            path, record['sha256'] = downloader(source['officialUrl'], temporary)
            check_stop(stop_file)
            pages = native_pages(path)
            date_context = pages[1][:1800] + ' ' + pages[len(pages)][-1800:]
            if not record['orderDate'] or record['orderDate'] not in dates_in(date_context):
                raise ValueError('Source-confirmed order date required')
            expected = identity(case_number)
            if not any(expected in identity(text[:1200]) for text in pages.values()):
                raise ValueError('Exact requested case identity absent from source')
            anchors = anchors_for(pages)
            chunks=[]
            current=[]
            length=0
            for anchor in anchors:
                size=len(json.dumps(anchor))
                if length+size>5000 and current:
                    chunks.append(current); current=[]; length=0
                current.append(anchor); length+=size
            if current: chunks.append(current)
            if len(chunks)>16: raise ValueError('NeedsSourceReview: order exceeds bounded inference coverage')
            if not chunks:
                raise ValueError('NeedsSourceReview: no usable relevant paragraphs')
            record['coverage'] = {'pageCount': len(pages), 'selectedPages': sorted({a['page'] for c in chunks for a in c}),
                                  'chunkCount': len(chunks), 'allSelectedChunksProcessed': False}
            needs_review = False
            collected = []
            for chunk in chunks:
                check_stop(stop_file)
                prompt = json.dumps({'anchors':chunk},ensure_ascii=False)
                schema = schema_for(chunk,pages)
                feedback = ''
                for attempt in range(2):
                    try:
                        payload = expand(provider.extract(ANCHOR_INSTRUCTIONS, prompt, schema, feedback),chunk,pages)
                        check_stop(stop_file)
                        break
                    except (ValueError, KeyError, TypeError, jsonschema.ValidationError) as error:
                        # Never log model output, PDF text or prompt. Feedback goes ONLY local.
                        detail = str(error) if isinstance(error, ValueError) else 'Schema property/type violation'
                        feedback = 'Return only schema-valid JSON with verbatim correct-page evidence. ' + detail[:240]
                        if attempt == 1:
                            raise ValueError('Structured extraction/evidence validation failed after one correction: ' + detail[:240]) from error
                needs_review |= payload['needsReview']
                collected.extend(payload['facts'])
            record['coverage']['allSelectedChunksProcessed'] = True
            record['facts'] = list({json.dumps(f, sort_keys=True): f for f in collected}.values())
            hearing_dates = {day for fact in record['facts'] if fact['field'] == 'nextHearing' and fact['scope'] == 'Current'
                             for day in dates_in(fact['value'])}
            record['nextHearingDate'] = next(iter(hearing_dates)) if len(hearing_dates) == 1 else None
            record['status'] = 'NeedsReview' if needs_review else 'Validated'
    except Exception as error:
        record['facts'] = [] # partial chunks cannot masquerade as a complete order
        record['status'] = 'NeedsSourceReview' if str(error).startswith('NeedsSourceReview:') else 'NeedsReview'
        record['failureMessage'] = str(error) if isinstance(error, ValueError) else 'Local extraction unavailable: ' + type(error).__name__
    return record

def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', suffix='.tmp', dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--jobs', required=True, help='Explicit local JSON: caseId/caseNumber/orders (known public URLs/date)')
    parser.add_argument('--extraction-root', required=True, help='Existing absolute Storage:ExtractionRoot')
    parser.add_argument('--endpoint', default='http://127.0.0.1:8096')
    parser.add_argument('--model-version', required=True)
    parser.add_argument('--stop-file', help='Runtime-only cooperative stop marker; checked between bounded operations')
    args = parser.parse_args()
    root = Path(args.extraction_root)
    if not root.is_absolute() or not root.is_dir():
        raise SystemExit('Existing absolute configured extraction root required')
    folder = root / 'court-intelligence' / 'v1'
    folder.mkdir(parents=True, exist_ok=True)
    # OS-held lock releases even after crash; no multiple worker/PDF concurrency.
    lock = (folder / '.worker.lock').open('a+b')
    import msvcrt
    lock.seek(0)
    if lock.read(1) == b'':
        lock.write(b'0'); lock.flush()
    lock.seek(0)
    try:
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        raise SystemExit('Another Court intelligence worker already owns this extraction root')
    provider = LlamaCppProvider(args.endpoint, args.model_version)
    jobs = json.loads(Path(args.jobs).read_text(encoding='utf-8'))
    try:
        for job in jobs:
            case_id = str(uuid.UUID(job['caseId']))
            records = []
            for source in job['orders']:
                order = process_order(source, job['caseNumber'], provider, stop_file=args.stop_file)
                records.append(order)
                # Versioned per-order evidence and failures, PDFs never retained.
                key = hashlib.sha256((source['officialUrl'] + str(source.get('orderDate'))).encode()).hexdigest()
                atomic_json(folder / case_id / 'orders' / (key + '.json'), order)
                artifact = synthesize(case_id, job['caseNumber'], records)
                artifact['processedAt'] = datetime.now(timezone.utc).isoformat()
                artifact['processingComplete'] = len(records) == len(job['orders'])
                atomic_json(folder / case_id / 'current.json', artifact)
                print(f'{case_id}: {source.get("orderDate")} {order["status"]}', flush=True)
    except StopRequested:
        print('Worker stopped safely; temporary PDF cleaned. Partial intelligence remains marked incomplete.', flush=True)
    finally:
        lock.seek(0)
        msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)
        lock.close()

if __name__ == '__main__':
    main()
