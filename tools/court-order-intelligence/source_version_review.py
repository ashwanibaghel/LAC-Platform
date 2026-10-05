"""Explicit local source-version review using complete saved model selections.

Never used by normal refresh. Different bytes stay blocked until this bounded
review proves every selected native paragraph and re-applies ALL worker guards.
No assistant prose, new model inference or network retrieval is accepted here.
"""
import hashlib
import shutil
from pathlib import Path
from worker import native_pages,outer_paragraph_offsets,process_order
from anchors import anchors_for,source_header,CASE_REFERENCES
from preselection import select_candidates
from semantics import identity,dates_in,VERSION
import json

def review_cached_selections(source,case_number,prior,pdf,expected_pdf_sha256,model_version):
    expected={'extraction':VERSION,'rulebook':'2','model':model_version}
    if (prior.get('versions')!=expected or prior.get('failureMessage') or prior.get('refreshFailure')
        or prior.get('status') not in ('Validated','NeedsReview')
        or not prior.get('sourceVerificationComplete')
        or not prior.get('coverage',{}).get('allSelectedChunksProcessed')
        or prior.get('deepProcessingComplete') is False
        or prior.get('officialUrl')!=source['officialUrl'] or prior.get('orderDate')!=source['orderDate']
        or identity(prior.get('caseNumber',''))!=identity(case_number)
        or prior.get('courtCaseId')!=source['courtCaseId']
        or not source.get('sourceEvidenceSha256')
        or prior.get('processedEvidenceSha256')!=source['sourceEvidenceSha256']):raise ValueError('Saved complete publication/version provenance required')
    pdf=Path(pdf)
    if hashlib.sha256(pdf.read_bytes()).hexdigest()!=expected_pdf_sha256:raise ValueError('Reviewed PDF hash mismatch')
    pages=native_pages(pdf);headers,bodies=source_header(pages)
    refs={identity(m.group())for text in headers.values()for m in CASE_REFERENCES.finditer(text)}
    if bodies is None or refs!={identity(case_number)} or source['orderDate']not in dates_in(pages[1][:1800]+' '+pages[len(pages)][-1800:]):
        raise ValueError('Exact single-case caption/date verification required')
    for value in prior.get('caption',{}).values():
        if isinstance(value,dict) and value.get('source'):
            citation=value['source']
            if citation['evidence'].strip()not in pages.get(citation['page'],''):raise ValueError('Court caption changed')
    candidates,selection=select_candidates(anchors_for(pages,outer_paragraph_offsets(pdf,pages)),pages)
    lookup={(f['page'],f['evidence']):f for f in prior['facts']}
    coverage=prior['coverage']
    if (not selection['wholeDocumentCoverage'] or coverage.get('pageCount')!=len(pages)
        or coverage.get('anchorCount')!=len(candidates) or len(lookup)!=len(candidates)
        or any((a['page'],a['text'])not in lookup for a in candidates)):
        raise ValueError('Complete identical native candidate coverage required; process changed source normally')
    class CachedSelections:
        version=model_version
        calls=0
        def extract(self,instructions,prompt,schema,feedback=''):
            self.calls+=1;payload=json.loads(prompt)
            values=[]
            for a in payload['anchors']:
                old=lookup.get((a['page'],a['text']))
                if old is None:raise ValueError('New/unmatched native paragraph')
                values.append(dict(anchorId=a['anchorId'],category=old['category'],field=old['field'],scope=old['scope']))
            return dict(facts=values,needsReview=prior['status']=='NeedsReview')
    def cached_download(url,temp):
        if url!=source['officialUrl'] or hashlib.sha256(pdf.read_bytes()).hexdigest()!=expected_pdf_sha256:
            raise ValueError('Reviewed local source changed')
        target=Path(temp)/'order.pdf';shutil.copyfile(pdf,target);return target,expected_pdf_sha256
    provider=CachedSelections()
    record=process_order(source,case_number,provider,temporary_root=pdf.parent,downloader=cached_download)
    if (record.get('failureMessage') or not record.get('sourceVerificationComplete')
        or not record.get('coverage',{}).get('allSelectedChunksProcessed')):raise ValueError('Independent worker revalidation failed')
    record['sourceVersionReview']=dict(previousSha256=prior['sha256'],reviewedSha256=expected_pdf_sha256,
        policy='complete-identical-native-selection-revalidation-v1',modelCalls=0,
        revalidatedAnchorCount=len(candidates))
    return record
