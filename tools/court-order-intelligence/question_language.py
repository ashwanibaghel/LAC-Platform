"""Deterministic answer-language presentation. Canonical claims stay verbatim.

Only approved narrative templates are localized, never generated factual prose.
Unmapped factual detail remains available in the original cited claim, rather
than trusting a second model/translation to preserve conditions and negation.
"""
import re

LANGUAGES=('Auto','English','Hindi','Hinglish')

def selected_language(value, question):
    if not isinstance(value,str) or value.lower() not in [x.lower() for x in LANGUAGES]:
        raise ValueError('Language must be Auto, English, Hindi or Hinglish')
    value=next(x for x in LANGUAGES if x.lower()==value.lower())
    if value!='Auto': return value
    if re.search(r'[\u0900-\u097f]',question): return 'Hindi'
    if re.search(r'\b(?:kya|kaise|samjhao|samjha|mein|hai|tha|thi|karna|isme|ye|batao|bata|kis|aakhri|agli|mujhe)\b',question,re.I): return 'Hinglish'
    return 'English'

def localize(result, language):
    result['language']=language
    if language=='English': return result
    hindi=language=='Hindi'
    lines=[]
    date=result.get('latestVerifiedOrderDate')
    if result.get('reason')=='NoFreshLatestDirection':
        lines.append(f'{date} के नवीनतम सत्यापित आदेश में कोई नया न्यायालयी निर्देश स्थापित नहीं है।' if hindi else
                     f'Latest verified order ({date}) mein Court ne koi fresh verified direction nahi di hai.')
        if result.get('earlierDirectionDate'):
            d=result['earlierDirectionDate']
            lines.append(f'सबसे हाल का पहले का सत्यापित निर्देश {d} के आदेश में था।' if hindi else
                         f'Sabse recent pehle ka verified direction {d} ke order mein tha.')
    if result.get('referentOrderDate'):
        d=result['referentOrderDate']
        lines.append(f'आप जिस आदेश या निर्देश की बात कर रहे हैं, वह {d} का है।' if hindi else
                     f'Jis order ya direction ki baat ho rahi hai, woh {d} ka tha.')
    for index,claim in enumerate(result.get('claims',[]),1):
        lines.append(narrative(claim,index,hindi))
        # The original source quote is evidence, not translated narration.
        lines.append('> '+claim['text'].replace('\n','\n> '))
    conclusion=result.get('actionConclusion')
    if conclusion:
        if conclusion.startswith('No verified LAC-specific'):
            line='वर्तमान में संसाधित साक्ष्य से LAC का कोई अनिवार्य लंबित कार्य सत्यापित नहीं होता।' if hindi else 'Abhi processed evidence se LAC ka koi mandatory pending action establish nahi hota.'
        elif 'conditional' in conclusion.lower():
            line='ये निर्देश सशर्त हैं; इन्हें LAC के अनिवार्य कार्य न मानें।' if hindi else 'Yeh directions conditional hain; inhe LAC ke mandatory tasks mat samjhein.'
        elif conclusion.startswith('No usable'):
            line='इस मामले में अभी उपयोग योग्य सत्यापित न्यायालयी साक्ष्य उपलब्ध नहीं है।' if hindi else 'Is case mein abhi usable verified Court evidence available nahi hai.'
        else:
            line='वर्तमान साक्ष्य में सत्यापित LAC के अनिवार्य कार्य नीचे दिए गए मूल अंशों में हैं।' if hindi else 'Current evidence mein verified mandatory LAC tasks neeche original cited passages mein hain.'
        result['actionConclusion']=line; lines.append(line)
    if result.get('coverageNote'):
        line='यह उत्तर केवल अभी सत्यापित साक्ष्य पर आधारित है; समीक्षा या प्रसंस्करण की प्रतीक्षा वाले स्रोत शामिल नहीं हैं।' if hindi else 'Yeh answer sirf abhi verified evidence par based hai; review ya processing ke liye pending sources ismein include nahi hain.'
        result['coverageNote']=line; lines.append(line)
    if not lines:
        reason=result.get('reason')
        messages={
            'HistoryTooLong':('इतिहास एक उत्तर के लिए बहुत लंबा है। छोटी तारीख अवधि पूछें या पूरा आदेश इतिहास देखें।','History ek reply ke liye bahut lambi hai. Chhoti date range poochhein ya complete order history dekhein.'),
            'FollowUpReferentUnavailable':('आदेश की तारीख बताएं; इस बातचीत में कोई एक निश्चित सत्यापित पिछला आदेश उपलब्ध नहीं है।','Order ki date bata dein; is conversation mein ek unique verified pichhla order available nahi hai.'),
            'OrderUnavailable':('उस तारीख का आधिकारिक आदेश उपलब्ध नहीं मिला।','Us date ka official order nahi mila.'),
            'OrderNotVerified':('उस आदेश का सत्यापन लंबित है; उसके तथ्य अभी उत्तर में उपयोग नहीं किए जा सकते।','Us order ka verification pending hai; uske facts abhi answer mein use nahi ho sakte.'),
            'OrderProcessing':('उस आदेश का प्रसंस्करण अभी लंबित है; उस तारीख का सत्यापित उत्तर उपलब्ध नहीं है।','Us order ki processing abhi pending hai; us date ka verified answer available nahi hai.')}
        lines.append(messages.get(reason,('इस मामले के संसाधित आदेशों से यह जानकारी सत्यापित नहीं हो सकी।','Is case ke processed orders se yeh information confirm nahi ho saki.'))[0 if hindi else 1])
    result['answer']='\n'.join(lines)
    return result

def narrative(claim, index, hindi):
    """A source-specific, attributed description; exact details are in claim n."""
    d=claim['source']['orderDate']; t=claim['text']; label=claim['attribution']
    historical=claim.get('temporalStatus')=='Historical' or 'Historical' in label or 'Earlier' in label
    conditional='Conditional' in label
    current='Mandatory current' in label
    if 'court direction' in label.lower():
        # Patterns describe source speech, not today's obligation. Even a fresh
        # direction is past tense unless the lifecycle establishes it outstanding.
        if hindi:
            head=f'पहले, {d} के आदेश में न्यायालय ने निर्देश दिया था' if historical else f'{d} के आदेश में न्यायालय ने निर्देश दर्ज किया था'
            detail='; उसकी सटीक शर्तें मूल अंश में हैं'
            if re.search(r'file short affidavits by the next date',t,re.I) and re.search(r'\bLAC\b',t) and re.search(r'Delhi Jal Board|\bDJB\b',t): detail=' कि LAC और दिल्ली जल बोर्ड निर्देश प्राप्त करके अगली सुनवाई तक संक्षिप्त शपथपत्र दाखिल करें'
            elif re.search(r'if they wish to do so',t,re.I) and re.search(r'file.*affidavits',t,re.I): detail=' कि संबंधित पक्ष चाहें तो शपथपत्र दाखिल कर सकते हैं; यह सशर्त निर्देश था'
            elif re.search(r'proceedings.*shall remain stayed',t,re.I): detail=' कि संबंधित कार्यवाही पर रोक रहे'
            elif re.search(r'\blist (?:on|for)\b|renotify',t,re.I): detail=' कि मामला मूल अंश में दर्ज तारीख पर सूचीबद्ध किया जाए'
            if current: detail+='; वर्तमान साक्ष्य में यह अनिवार्य कार्य सत्यापित है'
            elif conditional: detail+='; इसे अनिवार्य लंबित कार्य न मानें'
            else: detail+='; इससे आज का अनिवार्य LAC कार्य स्थापित नहीं होता'
            return head+detail+f'। [स्रोत {index}]'
        head=f'Pehle, {d} ke order mein Court ne direction di thi' if historical else f'{d} ke order mein Court ne direction record ki thi'
        detail='; exact wording aur conditions original passage mein hain'
        if re.search(r'file short affidavits by the next date',t,re.I) and re.search(r'\bLAC\b',t) and re.search(r'Delhi Jal Board|\bDJB\b',t): detail=' ki LAC aur DJB instructions lekar agli hearing tak short affidavits file karein'
        elif re.search(r'if they wish to do so',t,re.I) and re.search(r'file.*affidavits',t,re.I): detail=' ki parties chahein toh affidavits file kar sakti hain; yeh conditional tha'
        elif re.search(r'proceedings.*shall remain stayed',t,re.I): detail=' ki concerned proceedings stayed rahein'
        elif re.search(r'\blist (?:on|for)\b|renotify',t,re.I): detail=' ki case original passage mein di gayi date par list ho'
        if current: detail+='; current evidence mein yeh mandatory action verified hai'
        elif conditional: detail+='; isse mandatory pending task mat samjhein'
        else: detail+='; isse aaj ka mandatory LAC action establish nahi hota'
        return head+detail+f'. [Source {index}]'
    roles=[('Petitioner submission','याचिकाकर्ता का पक्ष','petitioner ka stand'),
           ('LAC/respondent submission','LAC/प्रतिवादी का पक्ष','LAC/respondent ka stand'),
           ('Other party submission','अन्य पक्ष की बात','other party ka stand'),
           ('Court finding','न्यायालय का निष्कर्ष','Court ka finding'),
           ('Court observation','न्यायालय का अवलोकन','Court ka observation'),
           ('The Court observed','न्यायालय का अवलोकन','Court ka observation'),
           ('The Court found','न्यायालय का निष्कर्ष','Court ka finding'),
           ('The Court decided','निस्तारण संबंधी विवरण','disposal ka record'),
           ('disposition','निस्तारण संबंधी विवरण','disposal ka record')]
    role=next(((hi,hg) for marker,hi,hg in roles if marker.lower() in label.lower()),('कार्यवाही या मामले का विवरण','case ya proceedings ka record'))
    if re.search(r'\bApplication is disposed of',t,re.I):
        detail='आवेदन का निस्तारण हुआ था; इसे पूरी याचिका का निस्तारण न मानें' if hindi else 'application dispose hui thi; ise poori petition ka disposal mat samjhein'
    elif re.search(r'appearance.*read as marked',t,re.I):
        detail='पहले के आदेश में वकीलों की उपस्थिति दर्ज मानी गई' if hindi else 'pehle ke order mein counsels ki appearance marked maani gayi'
    elif 'submission' in label.lower() and re.search(r'notifications under Sections 11 and 19.*deserve to be quashed',t,re.I):
        detail='याचिकाकर्ता ने धारा 11 और 19 की अधिसूचनाएं रद्द करने की मांग रखी' if hindi else 'petitioner ne Sections 11 aur 19 ki notifications quash karne ki demand rakhi'
    elif re.search(r'SIA report.*compensation.*twice the market value',t,re.I):
        detail='SIA रिपोर्ट में संबंधित भूमि का मुआवज़ा बाजार मूल्य का दोगुना बताया गया है' if hindi else 'SIA report mein subject land ka compensation market value ka do guna bataya gaya hai'
    elif re.search(r'LAC and DJB are stated to have filed their respective affidavits recently',t,re.I):
        detail='LAC और DJB के शपथपत्र हाल ही में दाखिल होने की बात दर्ज है' if hindi else 'LAC aur DJB ke affidavits recently file hone ki baat recorded hai'
    elif re.search(r'whether the final SIA report was.*prepared.*published.*copy.*Petitioners',t,re.I):
        detail='मुद्दा यह था कि अंतिम SIA रिपोर्ट बनी और प्रकाशित हुई थी या नहीं, और उसकी प्रति याचिकाकर्ताओं को मिली थी या नहीं' if hindi else 'issue yeh tha ki final SIA report bani aur publish hui thi ya nahi, aur uski copy petitioners ko mili thi ya nahi'
    elif re.search(r'land in question.*notification issued under Section 11',t,re.I):
        detail='मामले की भूमि का विवरण धारा 11 की अधिसूचना से संबंधित है' if hindi else 'case ki land ka description Section 11 ki notification se related hai'
    else:
        detail=(role[0]+' दर्ज है; सटीक विवरण नीचे के मूल अंश में है') if hindi else (role[1]+' recorded hai; exact details neeche original passage mein hain')
    if 'submission' in label.lower():
        detail+='; यह न्यायालय का स्थापित निष्कर्ष नहीं है' if hindi else '; yeh Court ka established finding nahi hai'
    return (f'{d} के आदेश में {detail}। [स्रोत {index}]' if hindi else f'{d} ke order mein {detail}. [Source {index}]')
