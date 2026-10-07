"""Explicit out-of-process, sequential native-PDF Court intelligence worker."""
import argparse
import hashlib
import json
import os
import re
import tempfile
import time
import uuid
import copy
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit

import fitz
import requests
import jsonschema
from provider import LlamaCppProvider
from semantics import VERSION, identity, normalized, dates_in, synthesize, confirmed_hearing_date
from anchors import anchors_for, expand, schema_for, source_header, CASE_REFERENCES, INSTRUCTIONS as ANCHOR_INSTRUCTIONS
from chronology import caption_context
from preselection import select_candidates, bounded_chunks
from native_layout import outer_paragraph_offsets
from completeness import recover
from fast_path import select_fast_candidates, compact_prompt, INSTRUCTIONS as FAST_INSTRUCTIONS
from lac_scope import build_scope, empty_scope

class StopRequested(BaseException):
    pass

class SourceVerificationError(ValueError):
    def __init__(self, code, message):
        super().__init__(message)
        self.code=code

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
                raise SourceVerificationError('OfficialPdfUnavailable','NeedsSourceReview: official direct PDF unavailable')
            size = 0
            with path.open('wb') as output:
                for chunk in response.iter_content(65536):
                    size += len(chunk)
                    if size > 30 * 1024 * 1024:
                        raise SourceVerificationError('OfficialPdfTooLarge','NeedsSourceReview: official PDF exceeds bounded size')
                    output.write(chunk)
                    digest.update(chunk)
        with path.open('rb') as stream:
            if stream.read(5) != b'%PDF-':
                raise SourceVerificationError('OfficialSourceNotPdf','NeedsSourceReview: official source is not a PDF')
        return path, digest.hexdigest()
    except requests.Timeout as error:
        raise SourceVerificationError('OfficialPdfDownloadTimeout','NeedsSourceReview: official PDF download timed out; retry source retrieval') from error
    except requests.ConnectionError as error:
        raise SourceVerificationError('OfficialPdfUnavailable','NeedsSourceReview: official PDF connection unavailable; retry source retrieval') from error
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
                raise SourceVerificationError('InsufficientNativeText',f'NeedsSourceReview: page {number} has insufficient native text; no OCR used')
            pages[number] = normalized(text)
    return pages

def process_order(source, case_number, provider, temporary_root=None, downloader=download, stop_file=None,
                  fast=False, on_fast_ready=None):
    record = {'officialUrl': source['officialUrl'], 'orderDate': source.get('orderDate'),
              'caseNumber': case_number, 'sha256': None, 'status': 'NeedsReview', 'facts': [],
              'versions': {'extraction': VERSION, 'rulebook': '2', 'model': provider.version},
              'processedAt': datetime.now(timezone.utc).isoformat(), 'coverage': {}}
    try:
        with tempfile.TemporaryDirectory(prefix='lac-court-order-', dir=temporary_root) as temporary:
            check_stop(stop_file)
            path, record['sha256'] = downloader(source['officialUrl'], temporary)
            check_stop(stop_file)
            pages = native_pages(path)
            headers,bodies=source_header(pages)
            if bodies is None:
                raise SourceVerificationError('PdfCaptionUnverified','NeedsSourceReview: no reliable Court caption/body boundary')
            date_context = pages[1][:1800] + ' ' + pages[len(pages)][-1800:]
            if not record['orderDate'] or record['orderDate'] not in dates_in(date_context):
                raise SourceVerificationError('PdfOrderDateMismatch','Source-confirmed order date required')
            expected = identity(case_number)
            matches=[(page,match.group()) for page,text in headers.items() for match in CASE_REFERENCES.finditer(text)]
            exact=[(page,raw) for page,raw in matches if identity(raw)==expected]
            if not exact:
                raise SourceVerificationError('PdfAttributionMismatch','Exact requested case identity absent from Court caption')
            record['rawIdentity']=exact[0][1]
            record['identityPage']=exact[0][0]
            if len({identity(raw) for page,raw in matches})>1:
                raise SourceVerificationError('ConnectedCasePdf','NeedsSourceReview: connected-case PDF needs case-specific attribution; no cross-case facts inferred')
            record['sourceVerificationComplete']=True
            record['court']='Delhi High Court' if 'HIGH COURT OF DELHI' in pages[1].upper() else None
            record['caption'] = caption_context(headers, source['officialUrl'], record['orderDate'])
            record['bench'] = record['caption'].get('bench', {}).get('text')
            anchors = anchors_for(pages, outer_paragraph_offsets(path, pages))
            candidates, selection = select_candidates(anchors, pages)
            fast_candidates = select_fast_candidates(candidates) if fast else candidates
            seeded=[]
            if (fast and source.get('deepProcessingComplete') is False and not source.get('failureMessage')
                and source.get('sha256')==record['sha256'] and source.get('versions')==record['versions']):
                # Resume only identical checked bytes/version. Re-expand cached
                # selections through CURRENT quote/speaker/page/action guards.
                for batch in range(0,len(source.get('facts',[])),6):
                    selections=[];matched=[]
                    for fact in source['facts'][batch:batch+6]:
                        anchor=next((a for a in candidates if a['page']==fact['page'] and a['text']==fact['evidence']),None)
                        if anchor:
                            matched.append(anchor);selections.append(dict(anchorId=anchor['anchorId'],category=fact['category'],field=fact['field'],scope=fact['scope']))
                    if matched:seeded.extend(expand(dict(facts=selections,needsReview=False),matched,pages,defer_semantic=True)['facts'])
            represented={(f['page'],f['evidence']) for f in seeded if f['scope']!='Uncertain'}
            chunks = bounded_chunks([a for a in fast_candidates if (a['page'],a['text']) not in represented])
            if len(chunks)>16: raise ValueError('NeedsSourceReview: order exceeds bounded inference coverage')
            if not chunks and not seeded:
                raise ValueError('NeedsSourceReview: no usable relevant paragraphs')
            record['coverage'] = {'pageCount': len(pages), 'selectedPages': sorted({a['page'] for c in chunks for a in c}),
                                  'chunkCount': len(chunks), 'anchorCount':len(anchors),
                                  'preselection': selection, 'allSelectedChunksProcessed': False}
            needs_review = False
            collected = list(seeded)
            for chunk in chunks:
                check_stop(stop_file)
                # Bounded selection must prioritize current operative language,
                # without promoting quoted directions or changing source text.
                chunk = sorted(chunk, key=lambda a: (bool(a.get('quoted')), not bool(re.search(r'\b(?:is directed|are directed|shall|renotify|issue notice)\b', a['text'], re.I)), a['anchorId']))
                prompt = compact_prompt(record['orderDate'],case_number,chunk) if fast else json.dumps({'documentOrderDate':record['orderDate'],'caseNumber':case_number,
                                     'sourceRoleContext':pages[1][:1800],'anchors':chunk},ensure_ascii=False)
                schema = schema_for(chunk,pages)
                feedback = ''
                for attempt in range(2):
                    try:
                        payload = expand(provider.extract(FAST_INSTRUCTIONS if fast else ANCHOR_INSTRUCTIONS, prompt, schema, feedback),chunk,pages,defer_semantic=True)
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
            if fast:
                # A fast context-only response must not precede unresolved
                # operative evidence merely to satisfy a latency target.
                extra,fast_completeness=recover(fast_candidates,collected,pages,case_number,record['orderDate'],provider)
                collected.extend(extra)
                needs_review |= fast_completeness['needsReview'] or bool(fast_completeness['remainingAnchorIds'])
                # Publish only a COMPLETE selected evidence batch, never partial
                # chunk output. Deep coverage and omissions remain explicit.
                partial=copy.deepcopy(record)
                partial['facts']=list({json.dumps(f,sort_keys=True):f for f in collected}.values())
                partial['coverage'].update(allSelectedChunksProcessed=True,
                    selectedAnchorCount=len(fast_candidates),fastCompleteness=fast_completeness,
                    fastOmittedAnchorIds=[a['anchorId'] for a in candidates if a not in fast_candidates])
                partial.update(briefTier='Fast',deepProcessingComplete=False,status='NeedsReview',
                    nextHearingDate=confirmed_hearing_date(partial['facts']))
                partial['lacOrderScope'] = build_scope(partial, pages, anchors, complete=False)
                fast_batch_verified=bool(partial['facts']) and not fast_completeness['remainingAnchorIds']
                if on_fast_ready and fast_batch_verified: on_fast_ready(partial)
                # Continue on the SAME temporary PDF, one inference at a time.
                # Already classified fast anchors are never extracted twice.
                remaining=[a for a in candidates if a not in fast_candidates and (a['page'],a['text']) not in represented]
                for chunk in bounded_chunks(remaining):
                    check_stop(stop_file)
                    prompt=json.dumps(dict(documentOrderDate=record['orderDate'],caseNumber=case_number,
                        sourceRoleContext=pages[1][:1800],anchors=chunk),ensure_ascii=False)
                    schema=schema_for(chunk,pages); feedback=''
                    for attempt in range(2):
                        try:
                            payload=expand(provider.extract(ANCHOR_INSTRUCTIONS,prompt,schema,feedback),chunk,pages,defer_semantic=True)
                            break
                        except (ValueError,KeyError,TypeError,jsonschema.ValidationError) as error:
                            feedback='Return schema-valid exact page evidence. '+str(error)[:240]
                            if attempt==1: raise ValueError('Deep extraction validation failed after one correction') from error
                    needs_review |= payload['needsReview']; collected.extend(payload['facts'])
                record['coverage']['chunkCount']=len(chunks)+len(bounded_chunks(remaining))
            check_stop(stop_file)
            extra, completeness = recover(candidates,collected,pages,case_number,record['orderDate'],provider)
            check_stop(stop_file)
            collected.extend(extra)
            record['coverage']['completenessRetry']=completeness
            needs_review |= completeness['needsReview'] or bool(completeness['remainingAnchorIds'])
            record['coverage']['allSelectedChunksProcessed'] = True
            record['facts'] = list({json.dumps(f, sort_keys=True): f for f in collected}.values())
            record['coverage']['representedAnchorCount'] = len({(f['page'],f['evidence']) for f in record['facts']})
            nonquoted={(anchor['page'],anchor['text']) for anchor in anchors if not anchor.get('quoted')}
            represented={(fact['page'],fact['evidence']) for fact in record['facts']}
            record['coverage']['unrepresentedNonquotedAnchors']=len(nonquoted-represented)
            needs_review |= bool(nonquoted-represented)
            record['nextHearingDate'] = confirmed_hearing_date(record['facts'])
            record['status'] = 'NeedsReview' if needs_review else 'Validated'
            record['lacOrderScope'] = build_scope(record, pages, anchors, complete=True)
            if fast: record.update(briefTier='Deep',deepProcessingComplete=True)
    except Exception as error:
        if fast and locals().get('fast_batch_verified',False):
            # Complete individually checked fast evidence survives interruption
            # of enrichment. It never claims whole-order/history completeness.
            partial['refreshFailure']='Background enrichment did not finish; verified fast evidence retained.'
            return partial
        record['facts'] = [] # partial chunks cannot masquerade as a complete order
        record['status'] = 'NeedsSourceReview' if str(error).startswith('NeedsSourceReview:') else 'NeedsReview'
        record['failureMessage'] = str(error) if isinstance(error, ValueError) else 'Local extraction unavailable: ' + type(error).__name__
        if isinstance(error, SourceVerificationError): record['sourceReasonCode']=error.code
        record['lacOrderScope'] = empty_scope(record)
    return record

def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', suffix='.tmp', dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
        # Windows readers/antivirus may briefly deny delete-sharing. Never
        # replace atomic publication with a direct write or retry indefinitely.
        delays = (0.02, 0.05, 0.1, 0.2, 0.4)
        for attempt in range(len(delays) + 1):
            try:
                os.replace(temporary, path)
                break
            except PermissionError as error:
                if getattr(error, 'winerror', None) not in (5, 32, 33) or attempt == len(delays):
                    raise
                time.sleep(delays[attempt])
    finally:
        if temporary is not None: temporary.unlink(missing_ok=True)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--jobs', required=True, help='Explicit local JSON: caseId/caseNumber/orders (known public URLs/date)')
    parser.add_argument('--extraction-root', required=True, help='Existing absolute Storage:ExtractionRoot')
    parser.add_argument('--endpoint', default='http://127.0.0.1:8096')
    parser.add_argument('--model-version', required=True)
    parser.add_argument('--case-id', help='Optionally process one explicitly listed matter from the local jobs file')
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
    if args.case_id:
        selected_id=str(uuid.UUID(args.case_id))
        jobs=[job for job in jobs if str(uuid.UUID(job['caseId']))==selected_id]
        if not jobs: raise SystemExit('Requested matter is not in the explicit local jobs file')
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
