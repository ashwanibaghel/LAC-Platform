"""Lossless V3 input presentation, independent of task/target/case identity.

Repeated source metadata and selected text are referenced, not truncated.
Native actor/attribution context is retained verbatim. Round-trip equality is
mandatory before this presentation can be supplied to any comparison model.
"""
from copy import deepcopy
import json

from semantic_gate import chronology_context

VERSION = 'v3-lossless-source-references-1'


def restore_input(value):
    result = deepcopy(value)
    if result.get('contextPresentation') != VERSION:
        return result
    result.pop('contextPresentation')
    catalog = result.pop('sourceCatalog')
    for entry in result['availableEvidence']:
        for source in [entry['source'], *entry.get('sourceContext', [])]:
            index = source.pop('sourceRef')
            if isinstance(index, bool) or not isinstance(index, int) or not 0 <= index < len(catalog):
                raise ValueError('Invalid compacted source reference')
            if set(source) & set(catalog[index]):
                raise ValueError('Compacted source metadata conflict')
            source.update(catalog[index])
            if source.pop('evidenceFromText', False):
                if 'evidence' in source:
                    raise ValueError('Conflicting evidence representation')
                source['evidence'] = entry['text']
            if source.pop('evidenceSuffixIsText', False):
                source['evidence'] += entry['text']
    if result.pop('chronologyDerivedFromEvidence', False):
        result['chronologyState'] = chronology_context(result['availableEvidence'])
    return result


def compact_input(value):
    if 'availableEvidence' not in value:
        return deepcopy(value)
    if 'contextPresentation' in value or 'sourceCatalog' in value:
        raise ValueError('Input is already presented/contains reserved source references')
    result, catalog, indices = deepcopy(value), [], {}
    for entry in result['availableEvidence']:
        for source in [entry['source'], *entry.get('sourceContext', [])]:
            metadata = {k: source.pop(k) for k in ('sha256', 'officialUrl', 'orderDate') if k in source}
            key = json.dumps(metadata, sort_keys=True)
            if key not in indices:
                indices[key] = len(catalog)
                catalog.append(metadata)
            source['sourceRef'] = indices[key]
            evidence, text = source.get('evidence', ''), entry['text']
            if evidence == text:
                source.pop('evidence')
                source['evidenceFromText'] = True
            elif evidence.endswith(text):
                # Prefix retains every character, including unselected speech.
                source['evidence'] = evidence[:-len(text)]
                source['evidenceSuffixIsText'] = True
    if result.get('chronologyState') == chronology_context(value['availableEvidence']):
        result.pop('chronologyState')
        result['chronologyDerivedFromEvidence'] = True
    result.update(contextPresentation=VERSION, sourceCatalog=catalog)
    if restore_input(result) != value:
        raise ValueError('Lossless context round-trip failed')
    return result
