/**
 * LAC Platform — Naksha Muntazamin (NM) & Award Data Entry Workspace
 * Complete Application Logic & State Controller (States 1 to 16)
 */

// --- Currency and formatting helpers ---
function formatINR(val) {
  if (val === undefined || val === null || isNaN(val)) return '₹0.00';
  const parts = Number(val).toFixed(2).split('.');
  let integerPart = parts[0];
  const decimalPart = parts[1];

  let lastThree = integerPart.substring(integerPart.length - 3);
  const otherNumbers = integerPart.substring(0, integerPart.length - 3);
  if (otherNumbers !== '') {
    lastThree = ',' + lastThree;
  }
  const formattedInteger = otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ',') + lastThree;
  return '₹' + formattedInteger + '.' + decimalPart;
}

function formatINRNoDec(val) {
  if (val === undefined || val === null || isNaN(val)) return '₹0';
  const parts = Math.round(Number(val)).toString();
  let lastThree = parts.substring(parts.length - 3);
  const otherNumbers = parts.substring(0, parts.length - 3);
  if (otherNumbers !== '') {
    lastThree = ',' + lastThree;
  }
  return '₹' + otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ',') + lastThree;
}

// ============================================================================
// HIERARCHICAL CANONICAL DATASET
// Award -> Khata -> Khasra Schedule Groups -> Individual Claimants
// ============================================================================
const NM_DATA = [
  {
    khataId: 1,
    khataNumber: '1min',
    statedArea: '18-04-00',
    totalClaimants: 2,
    totalCompensation: 13912440,
    isExpanded: false,
    discrepancy: null,
    scheduleGroups: [
      {
        scheduleId: '1-main',
        scheduleCode: 'Schedule 1',
        scheduleTitle: 'Standard Revenue Holding (Rosli)',
        statedScheduleArea: '18-04-00',
        khasras: [
          { id: '2//18', area: '4-16-00', class: 'Rosli' },
          { id: '2//19', area: '4-12-00', class: 'Rosli' },
          { id: '2//20', area: '4-08-00', class: 'Rosli' },
          { id: '2//21', area: '4-08-00', class: 'Rosli' }
        ],
        claimants: [
          {
            itemId: 1,
            name: 'Devendra Sharma (Demo)',
            parentage: 'S/o R. K. Sharma',
            share: '1/2',
            shareText: '1/2',
            isDittoShare: false,
            inheritSchedule: null,
            isAmbiguous: false,
            allocArea: '9-02-00',
            isDittoArea: false,
            landClass: 'Rosli',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 4231500,
            structureDesc: 'Boundary Wall + Shed',
            structureVal: 85000,
            subtotal: 4316500,
            solatium: 1294950,
            sec23_1a: 1028308,
            sec34: 518342,
            totalPayable: 7158100,
            sourceRef: 'Pt-1 p.12 r.1',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Verified & Entitled',
            parcels: [
              { khasra: '2//18', totalArea: '4-16-00', allocated: '2-08-00' },
              { khasra: '2//19', totalArea: '4-12-00', allocated: '2-06-00' },
              { khasra: '2//20', totalArea: '4-08-00', allocated: '2-04-00' },
              { khasra: '2//21', totalArea: '4-08-00', allocated: '2-04-00' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1 (4 Khasras, Area 18-04 B-B)',
              areaFormula: '1/2 of 18-04 B-B = 9-02-00 B-B (9.10 Bigha)',
              rateFormula: '9.10 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 4231500,
              structFormula: 'Assessed Boundary Wall & Tube-well Shed',
              structValResult: 85000,
              subtotalResult: 4316500,
              solatiumFormula: '30% of ₹43,16,500 [Sec 23(2)]',
              solatiumResult: 1294950,
              sec23Formula: '12% p.a. for 726 days [Sec 23(1A)]',
              sec23Result: 1028308,
              sec34Formula: '9% p.a. for 1 year from possession [Sec 34]',
              sec34Result: 518342,
              grandTotal: 7158100
            }
          },
          {
            itemId: 2,
            name: 'Naveen Sharma (Demo)',
            parentage: 'S/o R. K. Sharma',
            share: '1/2',
            shareText: '-do-',
            isDittoShare: true,
            inheritSchedule: 'Schedule 1',
            inheritShareText: '1/2',
            isAmbiguous: false,
            allocArea: '9-02-00',
            isDittoArea: true,
            landClass: 'Rosli',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 4231500,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 4231500,
            solatium: 1269450,
            sec23_1a: 1008058,
            sec34: 508128,
            totalPayable: 7017136,
            sourceRef: 'Pt-1 p.12 r.2',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Verified & Entitled',
            parcels: [
              { khasra: '2//18', totalArea: '4-16-00', allocated: '2-08-00' },
              { khasra: '2//19', totalArea: '4-12-00', allocated: '2-06-00' },
              { khasra: '2//20', totalArea: '4-08-00', allocated: '2-04-00' },
              { khasra: '2//21', totalArea: '4-08-00', allocated: '2-04-00' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1 (4 Khasras, Area 18-04 B-B)',
              areaFormula: '1/2 of 18-04 B-B = 9-02-00 B-B (9.10 Bigha)',
              rateFormula: '9.10 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 4231500,
              structFormula: 'No structures assessed',
              structValResult: 0,
              subtotalResult: 4231500,
              solatiumFormula: '30% of ₹42,31,500 [Sec 23(2)]',
              solatiumResult: 1269450,
              sec23Formula: '12% p.a. for 726 days [Sec 23(1A)]',
              sec23Result: 1008058,
              sec34Formula: '9% p.a. for 1 year [Sec 34]',
              sec34Result: 508128,
              grandTotal: 7017136
            }
          }
        ]
      }
    ]
  },
  {
    khataId: 2,
    khataNumber: '2',
    statedArea: '04-16-00',
    totalClaimants: 1,
    totalCompensation: 3701200,
    isExpanded: false,
    discrepancy: null,
    scheduleGroups: [
      {
        scheduleId: '2-main',
        scheduleCode: 'Schedule 1',
        scheduleTitle: 'Sole Proprietary Parcel',
        statedScheduleArea: '04-16-00',
        khasras: [
          { id: '2//22', area: '4-16-00', class: 'Rosli' }
        ],
        claimants: [
          {
            itemId: 3,
            name: 'Vikas Mehra (Demo)',
            parentage: 'S/o O. P. Mehra',
            share: '1/1',
            shareText: '1/1',
            isDittoShare: false,
            inheritSchedule: null,
            isAmbiguous: false,
            allocArea: '4-16-00',
            isDittoArea: false,
            landClass: 'Rosli',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 2232000,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 2232000,
            solatium: 669600,
            sec23_1a: 531737,
            sec34: 267863,
            totalPayable: 3701200,
            sourceRef: 'Pt-1 p.14 r.4',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Mutated in full',
            parcels: [
              { khasra: '2//22', totalArea: '4-16-00', allocated: '4-16-00' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1 (Khasra 2//22, Area 4-16 B-B)',
              areaFormula: 'Sole owner: 4-16-00 B-B = 4.80 Bigha',
              rateFormula: '4.80 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 2232000,
              structFormula: 'No structures assessed',
              structValResult: 0,
              subtotalResult: 2232000,
              solatiumFormula: '30% of ₹22,32,000 [Sec 23(2)]',
              solatiumResult: 669600,
              sec23Formula: '12% p.a. for 726 days',
              sec23Result: 531737,
              sec34Formula: '9% p.a. for 1 year',
              sec34Result: 267863,
              grandTotal: 3701200
            }
          }
        ]
      }
    ]
  },
  {
    khataId: 4,
    khataNumber: '4',
    statedArea: '11-02-00',
    totalClaimants: 3,
    totalCompensation: 2987583,
    isExpanded: true,
    discrepancy: null,
    scheduleGroups: [
      {
        scheduleId: '4-main',
        scheduleCode: 'Schedule 1',
        scheduleTitle: 'Main Co-sharer Parcel Group',
        statedScheduleArea: '11-02-00',
        khasras: [
          { id: '6//2', area: '3-00', class: 'Rosli' },
          { id: '6//3', area: '2-02', class: 'Rosli' },
          { id: '6//4', area: '1-08', class: 'Rosli' },
          { id: '6//10', area: '4-12', class: 'Rosli' }
        ],
        claimants: [
          {
            itemId: 10,
            name: 'Anand Verma (Demo)',
            parentage: 'S/o B. L. Verma',
            share: '1/9',
            shareText: '1/9',
            isDittoShare: false,
            inheritSchedule: null,
            isAmbiguous: false,
            allocArea: '1-04-13',
            isDittoArea: false,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 575640,
            structureDesc: 'Tubewell (150ft/5HP)',
            structureVal: 75000,
            subtotal: 650640,
            solatium: 195192,
            sec23_1a: 154852,
            sec34: 78077,
            totalPayable: 1078761,
            sourceRef: 'Pt-2 p.148 r.1',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Original Entitlement',
            parcels: [
              { khasra: '6//2', totalArea: '3-00-00', allocated: '0-06-13' },
              { khasra: '6//3', totalArea: '2-02-00', allocated: '0-04-13' },
              { khasra: '6//4', totalArea: '1-08-00', allocated: '0-03-02' },
              { khasra: '6//10', totalArea: '4-12-00', allocated: '0-10-05' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1 (4 Khasras, Total Area 11-02 B-B)',
              areaFormula: '1/9th of 11-02-00 B-B = 1-04-13 B-B-B (1.2325 Bigha)',
              rateFormula: '1.2325 Bigha × ₹4,65,000 / Bigha (Block A Rosli)',
              landCompResult: 575640,
              structFormula: 'Appraised by PWD Valuer (Boring 150ft + Submersible Pump)',
              structValResult: 75000,
              subtotalResult: 650640,
              solatiumFormula: '30% of Subtotal ₹6,50,640 [Sec 23(2)]',
              solatiumResult: 195192,
              sec23Formula: '12% p.a. on Land Comp from 13.12.2000 to 09.12.2002 (726 days)',
              sec23Result: 154852,
              sec34Formula: '9% p.a. for 1 year from possession dt 10.04.2002',
              sec34Result: 78077,
              grandTotal: 1078761
            }
          },
          {
            itemId: 11,
            name: 'Suresh Verma (Demo)',
            parentage: 'S/o B. L. Verma',
            share: '1/9',
            shareText: '-do-',
            isDittoShare: true,
            inheritSchedule: 'Schedule 1',
            inheritShareText: '1/9',
            isAmbiguous: false,
            allocArea: '1-04-13',
            isDittoArea: true,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 575640,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 575640,
            solatium: 172692,
            sec23_1a: 137002,
            sec34: 69077,
            totalPayable: 954411,
            sourceRef: 'Pt-2 p.148 r.2',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Original Entitlement',
            parcels: [
              { khasra: '6//2', totalArea: '3-00-00', allocated: '0-06-13' },
              { khasra: '6//3', totalArea: '2-02-00', allocated: '0-04-13' },
              { khasra: '6//4', totalArea: '1-08-00', allocated: '0-03-02' },
              { khasra: '6//10', totalArea: '4-12-00', allocated: '0-10-05' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1 (4 Khasras, Total Area 11-02 B-B)',
              areaFormula: '1/9th of 11-02-00 B-B = 1-04-13 B-B-B (1.2325 Bigha)',
              rateFormula: '1.2325 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 575640,
              structFormula: 'No structures assessed',
              structValResult: 0,
              subtotalResult: 575640,
              solatiumFormula: '30% of ₹5,75,640 [Sec 23(2)]',
              solatiumResult: 172692,
              sec23Formula: '12% p.a. on Land Comp from 13.12.2000 to 09.12.2002 (726 days)',
              sec23Result: 137002,
              sec34Formula: '9% p.a. for 1 year from possession',
              sec34Result: 69077,
              grandTotal: 954411
            }
          },
          {
            itemId: 14,
            name: 'Jitendra Verma (Demo)',
            parentage: 'S/o B. L. Verma',
            share: '1/9',
            shareText: '-do-',
            isDittoShare: true,
            inheritSchedule: 'Schedule 1',
            inheritShareText: '1/9',
            isAmbiguous: false,
            allocArea: '1-04-13',
            isDittoArea: true,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 575640,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 575640,
            solatium: 172692,
            sec23_1a: 137002,
            sec34: 69077,
            totalPayable: 954411,
            sourceRef: 'Pt-2 p.148 r.5',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'red-ink',
            remarkBadge: '[!] Red note: LAC(SW)/2003 Asami inquiry',
            remarkText: 'Payment withheld as per LAC(SW) letter No. 1402 dt 14.03.2003 regarding Gaon Sabha Asami dispute.',
            parcels: [
              { khasra: '6//2', totalArea: '3-00-00', allocated: '0-06-13' },
              { khasra: '6//3', totalArea: '2-02-00', allocated: '0-04-13' },
              { khasra: '6//4', totalArea: '1-08-00', allocated: '0-03-02' },
              { khasra: '6//10', totalArea: '4-12-00', allocated: '0-10-05' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1 (4 Khasras, Total Area 11-02 B-B)',
              areaFormula: '1/9th of 11-02-00 B-B = 1-04-13 B-B-B (1.2325 Bigha)',
              rateFormula: '1.2325 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 575640,
              structFormula: 'No structures assessed',
              structValResult: 0,
              subtotalResult: 575640,
              solatiumFormula: '30% of ₹5,75,640 [Sec 23(2)]',
              solatiumResult: 172692,
              sec23Formula: '12% p.a. for 726 days',
              sec23Result: 137002,
              sec34Formula: '9% p.a. for 1 year',
              sec34Result: 69077,
              grandTotal: 954411
            }
          }
        ]
      }
    ]
  },
  {
    khataId: 7,
    khataNumber: '7',
    statedArea: '08-16-00',
    totalClaimants: 1,
    totalCompensation: 6520470,
    isExpanded: false,
    discrepancy: null,
    scheduleGroups: [
      {
        scheduleId: '7-rosli',
        scheduleCode: 'Schedule 1',
        scheduleTitle: 'Block A (Rosli) Agricultural Parcel',
        statedScheduleArea: '04-16-00',
        khasras: [
          { id: '8//12', area: '4-16', class: 'Rosli (Block A @ ₹4,65,000)' }
        ],
        claimants: [
          {
            itemId: 18,
            name: 'Suresh Nambiar (Demo)',
            parentage: 'S/o V. Nambiar',
            share: '1/1',
            shareText: '1/1',
            isDittoShare: false,
            inheritSchedule: null,
            isAmbiguous: false,
            allocArea: '4-16-00',
            isDittoArea: false,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 2232000,
            structureDesc: 'Boundary Wall',
            structureVal: 70000,
            subtotal: 2302000,
            solatium: 690600,
            sec23_1a: 531737,
            sec34: 276240,
            totalPayable: 3800577,
            sourceRef: 'Pt-2 p.152 r.8',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Block A schedule parcel',
            parcels: [
              { khasra: '8//12', totalArea: '4-16-00', allocated: '4-16-00', class: 'Rosli (Block A)' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 1: Khasra 8//12 (Rosli Block A)',
              areaFormula: '4 Bigha 16 Biswa = 4.80 Bigha',
              rateFormula: '4.80 Bigha × ₹4,65,000 / Bigha (Rosli Rate)',
              landCompResult: 2232000,
              structFormula: 'Masonry Boundary Wall appraisal',
              structValResult: 70000,
              subtotalResult: 2302000,
              solatiumFormula: '30% of ₹23,02,000 [Sec 23(2)]',
              solatiumResult: 690600,
              sec23Formula: '12% p.a. on Land Comp for 726 days',
              sec23Result: 531737,
              sec34Formula: '9% p.a. for 1 year from possession',
              sec34Result: 276240,
              grandTotal: 3800577
            }
          }
        ]
      },
      {
        scheduleId: '7-dakar',
        scheduleCode: 'Schedule 2',
        scheduleTitle: 'Block B (Dakar) Lowland Parcel',
        statedScheduleArea: '04-00-00',
        khasras: [
          { id: '8//13', area: '4-00', class: 'Dakar (Block B @ ₹3,95,000)' }
        ],
        claimants: [
          {
            itemId: 19,
            name: 'Kavita Nambiar (Demo)',
            parentage: 'D/o V. Nambiar',
            share: '1/1',
            shareText: '-do-',
            isDittoShare: true,
            inheritSchedule: 'Schedule 2',
            inheritShareText: '1/1',
            isAmbiguous: false,
            allocArea: '4-00-00',
            isDittoArea: false,
            landClass: 'Dakar (Block B)',
            classType: 'dakar',
            ratePerBigha: 395000,
            landComp: 1580000,
            structureDesc: 'Irrigation Well',
            structureVal: 50000,
            subtotal: 1630000,
            solatium: 489000,
            sec23_1a: 376378,
            sec34: 195600,
            totalPayable: 2690978,
            sourceRef: 'Pt-2 p.152 r.9',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Block B schedule parcel',
            parcels: [
              { khasra: '8//13', totalArea: '4-00-00', allocated: '4-00-00', class: 'Dakar (Block B)' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule 2: Khasra 8//13 (Dakar Block B)',
              areaFormula: '4 Bigha 0 Biswa = 4.00 Bigha',
              rateFormula: '4.00 Bigha × ₹3,95,000 / Bigha (Dakar Rate)',
              landCompResult: 1580000,
              structFormula: 'Open Irrigation Well appraisal',
              structValResult: 50000,
              subtotalResult: 1630000,
              solatiumFormula: '30% of ₹16,30,000 [Sec 23(2)]',
              solatiumResult: 489000,
              sec23Formula: '12% p.a. on Land Comp for 726 days',
              sec23Result: 376378,
              sec34Formula: '9% p.a. for 1 year from possession',
              sec34Result: 195600,
              grandTotal: 2690978
            }
          }
        ]
      }
    ]
  },
  {
    khataId: 104,
    khataNumber: '104min',
    statedArea: '51-13-00',
    totalClaimants: 164,
    totalCompensation: 39316000,
    isExpanded: false,
    discrepancy: {
      status: 'MULTI_SCHEDULE_SCOPE_REVIEW',
      badge: 'Scope Review Required',
      grossStatedArea: '51-13-00 B-B',
      schedule1Area: '1-07-00 B-B (Khasra 2//23)',
      schedule2Area: '44-07-00 B-B (11 Khasras)',
      separateScopeArea: '7-06-00 B-B (Khasras 6//1 & 7//5/1-2)',
      description: 'Khata 104min contains multiple distinct source schedules with separate legal scopes: Schedule A (Gram Sabha 1/1 share) and Schedule B (Proprietary Body Co-sharers). The 7-06 B-B balance reflects separate source schedule / unlinked parcels rather than a confirmed calculation error. Review schedule scopes prior to cohort mutation.'
    },
    scheduleGroups: [
      {
        scheduleId: '104-A',
        scheduleCode: 'Schedule A',
        scheduleTitle: 'Gram Sabha / Gaon Sabha Public Land (1/1 Share)',
        statedScheduleArea: '1-07-00',
        khasras: [
          { id: '2//23', area: '1-07', class: 'Rosli (Block A)' }
        ],
        claimants: [
          {
            itemId: 24,
            name: 'Gram Sabha / Gaon Sabha',
            parentage: 'Public Revenue Estate',
            share: '1/1',
            shareText: '1/1',
            isDittoShare: false,
            inheritSchedule: null,
            isAmbiguous: false,
            allocArea: '1-07-00',
            isDittoArea: false,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 639375,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 639375,
            solatium: 191813,
            sec23_1a: 152345,
            sec34: 87467,
            totalPayable: 1071000,
            sourceRef: 'Pt-2 p.148 r.1',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Gram Sabha statutory holding',
            parcels: [
              { khasra: '2//23', totalArea: '1-07-00', allocated: '1-07-00', class: 'Rosli (Block A)' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule A (Khasra 2//23, Area 1-07 B-B)',
              areaFormula: '1 Bigha 7 Biswa = 1.35 Bigha (1/1 Sole Public Holding)',
              rateFormula: '1.35 Bigha × ₹4,65,000 / Bigha (Block A Rosli)',
              landCompResult: 639375,
              structFormula: 'No structures assessed',
              structValResult: 0,
              subtotalResult: 639375,
              solatiumFormula: '30% of ₹6,39,375 [Sec 23(2)]',
              solatiumResult: 191813,
              sec23Formula: '12% p.a. for 726 days [Sec 23(1A)]',
              sec23Result: 152345,
              sec34Formula: '9% p.a. for 1 year from possession [Sec 34]',
              sec34Result: 87467,
              grandTotal: 1071000
            }
          }
        ]
      },
      {
        scheduleId: '104-B',
        scheduleCode: 'Schedule B',
        scheduleTitle: 'Proprietary Body Co-sharers (163 Claimants)',
        statedScheduleArea: '44-07-00',
        khasras: [
          { id: '2//1', area: '4-16', class: 'Rosli' },
          { id: '2//2', area: '4-16', class: 'Rosli' },
          { id: '2//3', area: '4-16', class: 'Rosli' },
          { id: '2//4', area: '4-16', class: 'Rosli' },
          { id: '2//5', area: '4-16', class: 'Rosli' },
          { id: '2//6', area: '4-16', class: 'Rosli' },
          { id: '2//7', area: '4-16', class: 'Rosli' },
          { id: '2//8', area: '4-16', class: 'Rosli' },
          { id: '2//9', area: '4-16', class: 'Rosli' },
          { id: '2//10', area: '4-16', class: 'Rosli' },
          { id: '2//23', area: '1-07', class: 'Rosli' }
        ],
        claimants: [
          {
            itemId: 25,
            name: 'Rohan Kapoor (Demo)',
            parentage: 'S/o T. N. Kapoor',
            share: '12/20660',
            shareText: '12/20660',
            isDittoShare: false,
            inheritSchedule: null,
            isAmbiguous: false,
            allocArea: '0-00-11',
            isDittoArea: false,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 12150,
            structureDesc: 'Share in Well',
            structureVal: 2400,
            subtotal: 14550,
            solatium: 4365,
            sec23_1a: 3467,
            sec34: 1748,
            totalPayable: 24130,
            sourceRef: 'Pt-2 p.148 r.2',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Explicit Schedule B anchor',
            parcels: [
              { khasra: '2//1 to 2//10 (10 parcels)', totalArea: '43-00-00', allocated: '0-00-10' },
              { khasra: '2//23', totalArea: '1-07-00', allocated: '0-00-01' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule B (11 Khasras, Area 44-07 B-B)',
              areaFormula: '12/20660 share of Schedule B (44-07 B-B) = 0-00-11 B-B-B',
              rateFormula: '0.0261 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 12150,
              structFormula: '12/20660 share in masonry irrigation well',
              structValResult: 2400,
              subtotalResult: 14550,
              solatiumFormula: '30% of ₹14,550 [Sec 23(2)]',
              solatiumResult: 4365,
              sec23Formula: '12% p.a. for 726 days [Sec 23(1A)]',
              sec23Result: 3467,
              sec34Formula: '9% p.a. for 1 year from possession',
              sec34Result: 1748,
              grandTotal: 24130
            }
          },
          {
            itemId: 26,
            name: 'Pawan Kapoor (Demo)',
            parentage: 'S/o T. N. Kapoor',
            share: '12/20660',
            shareText: '-do-',
            isDittoShare: true,
            inheritSchedule: 'Schedule B',
            inheritShareText: '12/20660',
            isAmbiguous: false,
            allocArea: '0-00-11',
            isDittoArea: true,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 12150,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 12150,
            solatium: 3645,
            sec23_1a: 2895,
            sec34: 1458,
            totalPayable: 20148,
            sourceRef: 'Pt-2 p.148 r.3',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Verified',
            remarkType: 'clean',
            remarkText: 'Inherits preceding entry',
            parcels: [
              { khasra: '2//1 to 2//10 (10 parcels)', totalArea: '43-00-00', allocated: '0-00-10' },
              { khasra: '2//23', totalArea: '1-07-00', allocated: '0-00-01' }
            ],
            calculationProof: {
              scheduleRef: 'Schedule B (11 Khasras, Area 44-07 B-B)',
              areaFormula: '12/20660 share of Schedule B (44-07 B-B) = 0-00-11 B-B-B',
              rateFormula: '0.0261 Bigha × ₹4,65,000 / Bigha',
              landCompResult: 12150,
              structFormula: 'Nil',
              structValResult: 0,
              subtotalResult: 12150,
              solatiumFormula: '30% of ₹12,150 [Sec 23(2)]',
              solatiumResult: 3645,
              sec23Formula: '12% p.a. for 726 days',
              sec23Result: 2895,
              sec34Formula: '9% p.a. for 1 year',
              sec34Result: 1458,
              grandTotal: 20148
            }
          },
          {
            itemId: 27,
            name: 'Sunita Kapoor (Demo)',
            parentage: 'W/o T. N. Kapoor',
            share: 'Ambiguous',
            shareText: '-do-',
            isDittoShare: true,
            inheritSchedule: null,
            isAmbiguous: true,
            allocArea: '0-00-11 (?)',
            isDittoArea: false,
            landClass: 'Rosli (Block A)',
            classType: 'rosli',
            ratePerBigha: 465000,
            landComp: 12150,
            structureDesc: 'Nil',
            structureVal: 0,
            subtotal: 12150,
            solatium: 3645,
            sec23_1a: 2895,
            sec34: 1458,
            totalPayable: 20148,
            sourceRef: 'Pt-2 p.148 r.4',
            entryMethod: 'PDF Extracted',
            reviewStatus: 'Needs Review',
            remarkType: 'amber-issue',
            remarkBadge: '⚠️ Needs Review',
            remarkText: 'Ambiguous schedule inheritance boundary between Schedule A and B in register.',
            parcels: [
              { khasra: 'Schedule Scope Pending Verification', totalArea: '44-07-00', allocated: 'Subject to Review' }
            ],
            calculationProof: {
              scheduleRef: 'AMBIGUOUS SCHEDULE INHERITANCE',
              areaFormula: 'Preceding entry spans schedule boundary. Requires Tehsildar verification.',
              rateFormula: 'Provisional valuation based on Schedule B',
              landCompResult: 12150,
              structFormula: 'Nil',
              structValResult: 0,
              subtotalResult: 12150,
              solatiumFormula: '30% of ₹12,150 [Sec 23(2)]',
              solatiumResult: 3645,
              sec23Formula: '12% p.a. for 726 days',
              sec23Result: 2895,
              sec34Formula: '9% p.a. for 1 year',
              sec34Result: 1458,
              grandTotal: 20148
            }
          }
        ]
      }
    ]
  }
];

// ============================================================================
// AWARD WORKSPACE MASTER DATA (For States 15 & 16)
// ============================================================================
const AWARD_KHASRAS = [
  { sr: 1, khasra: '6//2', village: 'Pochanpur', awardedArea: '3-00-00', landClass: 'Rosli (Block A)', circleRate: 465000, landComp: 1395000, sec4Date: '13.12.2000', possDate: '10.04.2002', gazetteRef: 'P.16 Para 2', vmStatus: 'Verified Match (3-00-00)', entryMethod: 'Gazette Verified' },
  { sr: 2, khasra: '6//3', village: 'Pochanpur', awardedArea: '2-02-00', landClass: 'Rosli (Block A)', circleRate: 465000, landComp: 976500, sec4Date: '13.12.2000', possDate: '10.04.2002', gazetteRef: 'P.16 Para 2', vmStatus: 'Verified Match (2-02-00)', entryMethod: 'Gazette Verified' },
  { sr: 3, khasra: '6//4', village: 'Pochanpur', awardedArea: '1-08-00', landClass: 'Rosli (Block A)', circleRate: 465000, landComp: 651000, sec4Date: '13.12.2000', possDate: '10.04.2002', gazetteRef: 'P.16 Para 2', vmStatus: 'Verified Match (1-08-00)', entryMethod: 'Gazette Verified' },
  { sr: 4, khasra: '6//10', village: 'Pochanpur', awardedArea: '4-12-00', landClass: 'Rosli (Block A)', circleRate: 465000, landComp: 2139000, sec4Date: '13.12.2000', possDate: '10.04.2002', gazetteRef: 'P.16 Para 2', vmStatus: 'Verified Match (4-12-00)', entryMethod: 'Gazette Verified' },
  { sr: 5, khasra: '8//12', village: 'Pochanpur', awardedArea: '4-16-00', landClass: 'Rosli (Block A)', circleRate: 465000, landComp: 2232000, sec4Date: '13.12.2000', possDate: '10.04.2002', gazetteRef: 'P.17 Para 1', vmStatus: 'Verified Match (4-16-00)', entryMethod: 'Gazette Verified' },
  { sr: 6, khasra: '8//13', village: 'Pochanpur', awardedArea: '4-00-00', landClass: 'Dakar (Block B)', circleRate: 395000, landComp: 1580000, sec4Date: '13.12.2000', possDate: '10.04.2002', gazetteRef: 'P.17 Para 1', vmStatus: 'Verified Match (4-00-00)', entryMethod: 'Gazette Verified' }
];

// --- Application State ---
const APP_STATE = {
  activeState: '1-collapsed-khatas',
  selectedClaimant: null,
  selectedSchedule: null,
  selectedKhata: null,
  activeDrawerTab: 'statutory',
  searchTerm: '',
  activeFilter: 'all',
  isDrawerOpen: false,
  isEntryDrawerOpen: false,
  isModalOpen: false,
  activeModal: null
};

// --- DOM References ---
const dom = {
  nmWorkspace: document.getElementById('nmWorkspace'),
  awardWorkspace: document.getElementById('awardWorkspace'),
  breadcrumbWorkspaceTitle: document.getElementById('breadcrumbWorkspaceTitle'),
  tableBody: document.getElementById('nmTableBody'),
  awardTableBody: document.getElementById('awardTableBody'),
  drawer: document.getElementById('nmDrawer'),
  entryDrawer: document.getElementById('manualEntryDrawer'),
  awardEntryDrawer: document.getElementById('awardEntryDrawer'),
  drawerCloseBtn: document.getElementById('drawerCloseBtn'),
  drawerItemNum: document.getElementById('drawerItemNum'),
  drawerSharePill: document.getElementById('drawerSharePill'),
  drawerClaimantSubtitle: document.getElementById('drawerClaimantSubtitle'),
  drawerBody: document.getElementById('drawerBody'),
  drawerTabs: document.querySelectorAll('.drawer-tab'),
  searchInput: document.getElementById('nmSearchInput'),
  filterPills: document.querySelectorAll('.filter-pill'),
  subbarTelemetry: document.getElementById('subbarTelemetry'),
  qaStateLabel: document.getElementById('activeStateLabel'),
  qaButtons: document.querySelectorAll('.qa-btn'),
  modalTemplateDownload: document.getElementById('modalTemplateDownload'),
  modalExcelImport: document.getElementById('modalExcelImport'),
  templateModalBody: document.getElementById('templateModalBody'),
  importModalTitle: document.getElementById('importModalTitle'),
  importModalBody: document.getElementById('importModalBody'),
  importModalFooter: document.getElementById('importModalFooter'),
  btnOpenManualEntry: document.getElementById('btnOpenManualEntry'),
  btnOpenExcelImport: document.getElementById('btnOpenExcelImport'),
  btnOpenTemplateDownload: document.getElementById('btnOpenTemplateDownload'),
  btnClassicView: document.getElementById('btnClassicView'),
  btnAwardView: document.getElementById('btnAwardView')
};

// ============================================================================
// NM TABLE RENDERER (Hierarchy: Khata -> Schedule -> Item)
// ============================================================================
function renderTable() {
  let html = '';
  const term = APP_STATE.searchTerm.trim().toLowerCase();

  NM_DATA.forEach(khata => {
    let allClaimantsInKhata = [];
    khata.scheduleGroups.forEach(sg => {
      allClaimantsInKhata = allClaimantsInKhata.concat(sg.claimants);
    });

    let khataHasMatch = false;
    if (!term && APP_STATE.activeFilter === 'all') {
      khataHasMatch = true;
    } else {
      khataHasMatch = allClaimantsInKhata.some(c => matchesFilter(c, khata, term));
      if (('khata ' + khata.khataNumber).toLowerCase().includes(term) || khata.khataNumber.toLowerCase().includes(term)) {
        khataHasMatch = true;
      }
    }

    if (!khataHasMatch) return;

    const isExpanded = khata.isExpanded;
    const arrowClass = isExpanded ? 'khata-arrow expanded' : 'khata-arrow';
    const scheduleCount = khata.scheduleGroups.length;

    // Level 1: Khata Header Row
    html += `
      <tr class="khata-group-row" onclick="toggleKhata(${khata.khataId})">
        <td colspan="17">
          <div class="khata-header-content">
            <button class="khata-toggle-btn" type="button" aria-expanded="${isExpanded}">
              <span class="${arrowClass}">▶</span>
              <span class="khata-badge">Khata ${khata.khataNumber}</span>
            </button>
            <span class="khata-area-badge">Stated Khata Area: <strong>${khata.statedArea} B-B</strong></span>
            <span class="khata-schedules-badge">📁 ${scheduleCount} Khasra ${scheduleCount === 1 ? 'Schedule' : 'Schedules'}</span>
            <span class="khata-stat" style="color: var(--slate-600);">
              (${khata.totalClaimants} Claimants)
            </span>
            <span class="khata-stat" style="background: #ffffff; border: 1px solid var(--slate-300); padding: 1px 7px; border-radius: 3px;">
              Khata Total Comp: <strong>${formatINRNoDec(khata.totalCompensation)}</strong>
            </span>
            ${khata.discrepancy ? `
              <span class="remark-badge amber-issue" style="font-size: 11px; font-weight: 700;">
                ⚠️ ${khata.discrepancy.badge}
              </span>
            ` : ''}
          </div>
        </td>
      </tr>
    `;

    // Discrepancy / Scope Banner
    if (isExpanded && khata.discrepancy) {
      html += `
        <tr style="background: #fffbeb;">
          <td colspan="17" style="padding: 8px 14px; border-bottom: 2px solid #fde68a;">
            <div style="display: flex; align-items: center; gap: 14px; font-size: 11.5px; color: #92400e; flex-wrap: wrap;">
              <div style="display: flex; align-items: center; gap: 6px;">
                <span style="font-size: 15px;">🔍</span>
                <span><strong>SCHEDULE SCOPE &amp; BOUNDARY ANALYSIS:</strong> ${khata.discrepancy.description}</span>
              </div>
              <button class="row-action-btn" style="background: #d97706; color: #ffffff; border-color: #b45309; font-weight: 600; padding: 3px 10px;" onclick="event.stopPropagation(); inspectScheduleScope(${khata.khataId})">
                Inspect Schedule Boundaries &rarr;
              </button>
            </div>
          </td>
        </tr>
      `;
    }

    // Level 2 & 3: Schedules & Items
    if (isExpanded) {
      khata.scheduleGroups.forEach(sg => {
        let matchingClaimants = sg.claimants.filter(c => matchesFilter(c, khata, term));
        if (term && matchingClaimants.length === 0 && !sg.scheduleCode.toLowerCase().includes(term) && !sg.scheduleTitle.toLowerCase().includes(term)) {
          return;
        }

        const khasraPills = sg.khasras.map(k => {
          return `<span class="khasra-pill">${k.id} <span class="extent">(${k.area})</span></span>`;
        }).join(' ');

        html += `
          <tr class="schedule-group-row">
            <td colspan="17">
              <div class="schedule-header-content">
                <span class="schedule-badge">
                  <span>📌 ${sg.scheduleCode}</span>
                </span>
                <span class="schedule-title">${sg.scheduleTitle}</span>
                <span class="schedule-area">Schedule Area: <strong>${sg.statedScheduleArea} B-B</strong></span>
                <div class="schedule-khasra-list">
                  <span style="font-size: 10.5px; color: var(--slate-500); margin-right: 2px;">Khasras (${sg.khasras.length}):</span>
                  ${khasraPills}
                </div>
                <span style="font-size: 11px; color: var(--slate-600); margin-left: auto;">
                  (${sg.claimants.length} ${sg.claimants.length === 1 ? 'Item' : 'Items'})
                </span>
              </div>
            </td>
          </tr>
        `;

        matchingClaimants.forEach(c => {
          const isSelected = APP_STATE.selectedClaimant && APP_STATE.selectedClaimant.itemId === c.itemId;
          const selectedClass = isSelected ? 'selected-row' : '';

          let shareMarkup = '';
          if (c.isDittoShare) {
            if (c.isAmbiguous) {
              shareMarkup = `<span class="ditto-pill">-do-</span> <span class="needs-review-pill" title="Ambiguous inheritance boundary">⚠️ Ambiguous Scope</span>`;
            } else {
              shareMarkup = `<span class="ditto-pill">-do-</span> <span class="inherit-pill" title="Inherits from ${c.inheritSchedule}">↳ ${c.inheritSchedule} (${c.inheritShareText})</span>`;
            }
          } else {
            shareMarkup = `<span class="share-pill">${c.shareText}</span>`;
          }

          const areaMarkup = c.isDittoArea
            ? `<span class="ditto-pill" title="Same area as preceding row">${c.allocArea}</span>`
            : `<span style="font-family: var(--font-mono); font-weight: 500;">${c.allocArea}</span>`;

          let classBadgeMarkup = '';
          if (c.classType === 'rosli') {
            classBadgeMarkup = `<span class="class-badge rosli">${c.landClass}</span>`;
          } else if (c.classType === 'dakar') {
            classBadgeMarkup = `<span class="class-badge dakar">${c.landClass}</span>`;
          } else if (c.classType === 'mixed') {
            classBadgeMarkup = `<span class="class-badge mixed">${c.landClass}</span>`;
          }

          let remarkMarkup = `<span class="remark-badge clean">${c.remarkText}</span>`;
          if (c.remarkType === 'red-ink') {
            remarkMarkup = `<span class="remark-badge red-ink" title="${c.remarkText}">${c.remarkBadge}</span>`;
          } else if (c.remarkType === 'amber-issue') {
            remarkMarkup = `<span class="remark-badge amber-issue" title="${c.remarkText}">${c.remarkBadge || c.remarkText}</span>`;
          }

          html += `
            <tr class="claimant-row ${selectedClass}" onclick="selectClaimant(${c.itemId}, '${sg.scheduleId}', ${khata.khataId})">
              <td class="sticky-col-item item-num">${c.itemId}</td>
              <td class="sticky-col-claimant">
                <div class="claimant-info">
                  <span class="claimant-name">${highlightMatch(c.name, term)}</span>
                  <span class="claimant-parentage">${highlightMatch(c.parentage, term)}</span>
                </div>
              </td>
              <td class="sticky-col-share">${shareMarkup}</td>

              <td class="num">${areaMarkup}</td>
              <td class="center">${classBadgeMarkup}</td>
              <td class="num">${formatINRNoDec(c.ratePerBigha)}</td>
              <td class="num" style="border-right: 2px solid var(--slate-300); font-weight: 600;">${formatINR(c.landComp)}</td>

              <td><span style="font-size: 11px; color: ${c.structureVal > 0 ? 'var(--slate-800)' : 'var(--slate-400)'};">${c.structureDesc}</span></td>
              <td class="num" style="border-right: 2px solid var(--slate-300);">${c.structureVal > 0 ? formatINR(c.structureVal) : '<span style="color: var(--slate-400);">-</span>'}</td>

              <td class="num" style="border-right: 2px solid var(--slate-300); font-weight: 600; color: var(--slate-900);">${formatINR(c.subtotal)}</td>

              <td class="num">${formatINR(c.solatium)}</td>
              <td class="num">${formatINR(c.sec23_1a)}</td>
              <td class="num" style="border-right: 2px solid var(--slate-300);">${formatINR(c.sec34)}</td>

              <td class="num total-cell" style="border-right: 2px solid var(--slate-300); color: var(--brand-700);">${formatINR(c.totalPayable)}</td>

              <td class="center"><span style="font-size: 10px; color: var(--slate-500); font-family: var(--font-mono);">${c.sourceRef}</span></td>
              <td>${remarkMarkup}</td>
              <td class="center">
                <button class="row-action-btn" type="button" onclick="event.stopPropagation(); selectClaimant(${c.itemId}, '${sg.scheduleId}', ${khata.khataId})">Inspect &rarr;</button>
              </td>
            </tr>
          `;
        });
      });
    }
  });

  dom.tableBody.innerHTML = html;
}

function matchesFilter(c, khata, term) {
  if (APP_STATE.activeFilter === 'red-notes' && c.remarkType !== 'red-ink') return false;
  if (APP_STATE.activeFilter === 'mismatches' && !khata.discrepancy && !c.isAmbiguous) return false;
  if (APP_STATE.activeFilter === 'mixed-class' && c.classType !== 'mixed') return false;

  if (!term) return true;
  const matchItem = ('item ' + c.itemId).toLowerCase().includes(term) || ('' + c.itemId).includes(term);
  const matchName = c.name.toLowerCase().includes(term);
  const matchParentage = c.parentage.toLowerCase().includes(term);
  return matchItem || matchName || matchParentage;
}

function highlightMatch(text, query) {
  if (!query) return text;
  const regex = new RegExp(`(${query.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
  return text.replace(regex, '<mark style="background: #fef08a; color: #1e293b; padding: 0 2px; border-radius: 2px;">$1</mark>');
}

window.toggleKhata = function(khataId) {
  const khata = NM_DATA.find(k => k.khataId === khataId);
  if (khata) {
    khata.isExpanded = !khata.isExpanded;
    renderTable();
  }
};

window.selectClaimant = function(itemId, scheduleId, khataId) {
  const khata = NM_DATA.find(k => k.khataId === khataId);
  if (!khata) return;
  const schedule = khata.scheduleGroups.find(s => s.scheduleId === scheduleId);
  if (!schedule) return;
  const claimant = schedule.claimants.find(c => c.itemId === itemId);
  if (!claimant) return;

  APP_STATE.selectedClaimant = claimant;
  APP_STATE.selectedSchedule = schedule;
  APP_STATE.selectedKhata = khata;
  APP_STATE.isDrawerOpen = true;

  renderDrawer();
  renderTable();
};

window.inspectScheduleScope = function(khataId) {
  const khata = NM_DATA.find(k => k.khataId === khataId);
  if (!khata) return;
  APP_STATE.selectedKhata = khata;
  APP_STATE.selectedSchedule = khata.scheduleGroups[1] || khata.scheduleGroups[0];
  APP_STATE.selectedClaimant = APP_STATE.selectedSchedule.claimants[0];
  APP_STATE.activeDrawerTab = 'parcels';
  APP_STATE.isDrawerOpen = true;
  renderDrawer();
  renderTable();
};

// ============================================================================
// DETAIL DRAWER RENDERER
// ============================================================================
function renderDrawer() {
  const c = APP_STATE.selectedClaimant;
  const sg = APP_STATE.selectedSchedule;
  const khata = APP_STATE.selectedKhata;
  if (!c || !APP_STATE.isDrawerOpen) {
    dom.drawer.classList.add('closed');
    return;
  }

  dom.drawer.classList.remove('closed');
  dom.drawerItemNum.textContent = `Item ${c.itemId}`;
  dom.drawerSharePill.textContent = `Share ${c.share}`;
  dom.drawerClaimantSubtitle.textContent = `${c.name} ${c.parentage} • ${sg.scheduleCode} • Khata ${khata.khataNumber}`;

  dom.drawerTabs.forEach(tab => {
    tab.classList.toggle('active', tab.dataset.tab === APP_STATE.activeDrawerTab);
  });

  let bodyHtml = '';

  if (APP_STATE.activeDrawerTab === 'statutory') {
    const p = c.calculationProof || {};
    bodyHtml = `
      <div class="stat-card" style="border-left: 3px solid var(--brand-600); background: #f0f9ff;">
        <div class="stat-card-title" style="color: var(--brand-700);">
          <span>Schedule Group Provenance</span>
          <span class="schedule-badge" style="font-size: 10px;">${sg.scheduleCode}</span>
        </div>
        <div style="font-size: 11.5px; color: var(--slate-800); font-weight: 600;">
          ${sg.scheduleTitle}
        </div>
        <div style="font-size: 11px; color: var(--slate-600); margin-top: 2px;">
          Schedule Stated Area: <strong>${sg.statedScheduleArea} B-B</strong> across ${sg.khasras.length} Khasras.
        </div>
        <div style="display: flex; gap: 8px; margin-top: 6px;">
          <span style="font-size: 10px; background: #ffffff; border: 1px solid var(--slate-300); padding: 1px 5px; border-radius: 3px; color: var(--slate-600);">
            Entry: <strong>${c.entryMethod || 'PDF Extracted'}</strong>
          </span>
          <span style="font-size: 10px; background: #ffffff; border: 1px solid var(--slate-300); padding: 1px 5px; border-radius: 3px; color: var(--slate-600);">
            Status: <strong>${c.reviewStatus || 'Verified'}</strong>
          </span>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-card-title">
          <span>1. Land Assessment (Section 23(1))</span>
          <span class="share-pill" style="font-size: 10px;">${c.allocArea}</span>
        </div>
        <div class="calc-line">
          <span class="calc-label">Allocated Area</span>
          <span class="calc-value">${c.allocArea} B-B-B</span>
        </div>
        <div class="calc-line">
          <span class="calc-label">Classification</span>
          <span class="calc-value">${c.landClass}</span>
        </div>
        <div class="calc-line">
          <span class="calc-label">Valuation Formula</span>
          <span class="calc-formula">${p.rateFormula || 'Gazetted circle rate'}</span>
        </div>
        <div class="calc-line" style="font-weight: 600;">
          <span class="calc-label">Total Land Compensation (A)</span>
          <span class="calc-value">${formatINR(c.landComp)}</span>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-card-title">
          <span>2. Immovable Property &amp; Structures</span>
          <span style="font-size: 10px; color: var(--slate-500);">${c.structureVal > 0 ? 'Assessed' : 'Nil'}</span>
        </div>
        <div class="calc-line">
          <span class="calc-label">Asset Specification</span>
          <span class="calc-value" style="font-size: 11px;">${c.structureDesc}</span>
        </div>
        <div class="calc-line">
          <span class="calc-label">Engineering Valuation</span>
          <span class="calc-formula">${p.structFormula || 'Certified schedule'}</span>
        </div>
        <div class="calc-line" style="font-weight: 600;">
          <span class="calc-label">Total Structural Value (B)</span>
          <span class="calc-value">${formatINR(c.structureVal)}</span>
        </div>
      </div>

      <div class="stat-card" style="border-left: 3px solid var(--brand-600);">
        <div class="stat-card-title">
          <span>3. Statutory Enhancements (LAA 1894)</span>
          <span style="font-size: 10px; color: var(--brand-600); font-weight: 700;">MANDATORY</span>
        </div>
        <div class="calc-line">
          <div>
            <div class="calc-label">Subtotal (A + B)</div>
            <div class="calc-formula">Primary market evaluation basis</div>
          </div>
          <span class="calc-value">${formatINR(c.subtotal)}</span>
        </div>
        <div class="calc-line">
          <div>
            <div class="calc-label">Solatium @ 30% [Sec 23(2)]</div>
            <div class="calc-formula">${p.solatiumFormula || '30% of Subtotal'}</div>
          </div>
          <span class="calc-value">${formatINR(c.solatium)}</span>
        </div>
        <div class="calc-line">
          <div>
            <div class="calc-label">Addl. Component @ 12% [Sec 23(1A)]</div>
            <div class="calc-formula">${p.sec23Formula || '12% p.a. for duration from Sec 4 to Award'}</div>
          </div>
          <span class="calc-value">${formatINR(c.sec23_1a)}</span>
        </div>
        <div class="calc-line">
          <div>
            <div class="calc-label">Interest @ 9% [Sec 34]</div>
            <div class="calc-formula">${p.sec34Formula || '9% p.a. from possession date'}</div>
          </div>
          <span class="calc-value">${formatINR(c.sec34)}</span>
        </div>
        <div class="calc-line grand-total">
          <div>
            <div class="calc-label">FINAL NET PAYABLE</div>
            <div class="calc-formula">Individually certified entitlement</div>
          </div>
          <span class="calc-value">${formatINR(c.totalPayable)}</span>
        </div>
      </div>
    `;
  } else if (APP_STATE.activeDrawerTab === 'parcels') {
    let rowsHtml = '';
    c.parcels.forEach(pr => {
      rowsHtml += `
        <tr>
          <td style="font-family: var(--font-mono); font-weight: 600;">${pr.khasra}</td>
          <td style="font-family: var(--font-mono);">${pr.totalArea}</td>
          <td style="font-family: var(--font-mono); font-weight: 600; color: var(--brand-700);">${pr.allocated}</td>
          <td style="font-size: 11px; color: var(--slate-600);">${pr.class || 'Rosli (Block A)'}</td>
        </tr>
      `;
    });

    bodyHtml = `
      <div class="stat-card">
        <div class="stat-card-title">
          <span>Schedule Group Parcel Breakdown</span>
          <span class="schedule-badge">${sg.scheduleCode}</span>
        </div>
        <p style="font-size: 11.5px; color: var(--slate-600); margin-bottom: 6px;">
          Claimant's share of <strong>${c.share}</strong> resolves exclusively across the constituent parcels of <strong>${sg.scheduleCode}</strong> (${sg.statedScheduleArea} B-B).
        </p>

        <table style="width: 100%; border-collapse: collapse; font-size: 11px; margin-top: 4px;">
          <thead>
            <tr style="background: var(--slate-100); border-bottom: 1px solid var(--slate-300); text-align: left;">
              <th style="padding: 4px 6px;">Khasra #</th>
              <th style="padding: 4px 6px;">Total Extent</th>
              <th style="padding: 4px 6px;">Allocated Area</th>
              <th style="padding: 4px 6px;">Land Class</th>
            </tr>
          </thead>
          <tbody>
            ${rowsHtml}
          </tbody>
        </table>
      </div>

      ${khata.discrepancy ? `
        <div class="stat-card" style="border: 1px solid #f59e0b; background: #fffbeb;">
          <div class="stat-card-title" style="color: #b45309;">
            <span>Schedule Scope &amp; Legal Boundary Analysis</span>
            <span class="filter-badge-amber">REVIEW REQUIRED</span>
          </div>
          <p style="font-size: 11px; color: #92400e; line-height: 1.45; margin-bottom: 6px;">
            The revenue record for <strong>Khata ${khata.khataNumber}</strong> contains distinct sub-schedules that cannot be conflated into a single uniform holding:
          </p>
          <div style="font-size: 10.5px; color: #78350f; background: #fef3c7; padding: 6px 8px; border-radius: 4px; line-height: 1.5;">
            • <strong>Schedule A (Gram Sabha Public Rights):</strong> 1-07 B-B [Khasra 2//23] (Share 1/1)<br>
            • <strong>Schedule B (Proprietary Body Co-sharers):</strong> 44-07 B-B [11 Linked Khasras]<br>
            • <strong>Separate Unlinked Area Scope:</strong> 7-06 B-B [Khasras 6//1 (3-10) &amp; 7//5/1-2 (3-16)]<br>
            <strong>Gross Stated Khata Area:</strong> 51-13-00 B-B (Reconciles exactly when separate scopes are accounted for).
          </div>
        </div>
      ` : ''}
    `;
  } else if (APP_STATE.activeDrawerTab === 'source') {
    bodyHtml = `
      <div class="source-proof-box">
        <div class="source-proof-header">
          <span>ARCHIVAL REVENUE REGISTER EXCERPT</span>
          <span style="font-family: var(--font-mono);">${c.sourceRef}</span>
        </div>
        <div class="source-proof-image">
          <div style="display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #e2e8f0; padding-bottom: 5px; margin-bottom: 5px;">
            <div style="font-weight: 700; color: #0f172a; font-size: 11px;">
              NAKSHA MUNTAZAMIN • VILLAGE POCHANPUR • PART-2
            </div>
            <span style="font-size: 9.5px; background: #e2e8f0; padding: 1px 4px; border-radius: 2px; color: #334155;">GAZETTE CERTIFIED</span>
          </div>
          <div style="display: flex; justify-content: space-between; font-size: 10.5px; color: #475569; margin-bottom: 4px;">
            <span>Serial/Item: <strong>${c.itemId}</strong></span>
            <span>Khata: <strong>${khata.khataNumber}</strong></span>
            <span>Schedule: <strong>${sg.scheduleCode}</strong></span>
            <span>Share: <strong>${c.share}</strong></span>
          </div>
          <div style="font-size: 11px; color: #1e293b; margin-bottom: 5px;">
            Recorded Claimant: <strong>${c.name} ${c.parentage}</strong>
          </div>

          ${c.remarkType === 'red-ink' ? `
            <div class="red-remark-text" style="background: #fee2e2; border-left: 3px solid #dc2626; padding: 6px 8px; border-radius: 3px;">
              <div style="display: flex; align-items: center; gap: 4px; font-size: 10px; color: #991b1b; text-transform: uppercase; font-weight: 700;">
                <span>⚠️ Administrative Direction in Red Ink:</span>
              </div>
              <div style="font-style: italic; margin-top: 3px; font-size: 11.5px; font-weight: 600; color: #b91c1c;">
                "${c.remarkText}"
              </div>
              <div style="margin-top: 4px; font-size: 9.5px; color: #7f1d1d;">
                Ref: LAC(SW) Dispatch #1402 / Revenue Branch / Delhi Administration
              </div>
            </div>
          ` : `
            <div style="padding: 4px 6px; background: #f0fdf4; border-left: 3px solid #16a34a; color: #166534; font-size: 11px; margin: 6px 0;">
              ✓ Clean entry. No red ink endorsements or disputed asami notations on this register page.
            </div>
          `}
        </div>
      </div>

      <div class="legal-advisory-box" style="border-left: 3px solid #dc2626; background: #fff5f5;">
        <strong style="color: #991b1b;">Legal Status of Red Ink Endorsements:</strong>
        <p style="margin-top: 3px; color: #7f1d1d; font-size: 10.5px;">
          Notations written in red ink reflect internal revenue administrative instructions (such as pending Tehsildar verifications or LAC correspondence). They <strong>DO NOT</strong> constitute a judicial injunction, court stay order, or formal Section 30/31 reference unless accompanied by an explicit High Court or District Judge case order.
        </p>
      </div>
    `;
  }

  dom.drawerBody.innerHTML = bodyHtml;
}

// Drawer tab switching
dom.drawerTabs.forEach(tab => {
  tab.addEventListener('click', () => {
    APP_STATE.activeDrawerTab = tab.dataset.tab;
    renderDrawer();
  });
});

dom.drawerCloseBtn.addEventListener('click', () => {
  APP_STATE.isDrawerOpen = false;
  APP_STATE.selectedClaimant = null;
  dom.drawer.classList.add('closed');
  renderTable();
});

// Close all modals
window.closeModals = function() {
  dom.modalTemplateDownload.classList.add('closed');
  dom.modalExcelImport.classList.add('closed');
  dom.entryDrawer.classList.add('closed');
  dom.awardEntryDrawer.classList.add('closed');
  APP_STATE.isModalOpen = false;
};

// ============================================================================
// MANUAL NM ENTRY DRAWER CONTROLLER (States 9, 10, 11)
// ============================================================================
function renderManualEntryDrawer(step) {
  dom.drawer.classList.add('closed');
  dom.entryDrawer.classList.remove('closed');

  let html = `
    <div class="entry-drawer-header">
      <div style="display: flex; align-items: center; gap: 8px;">
        <span style="font-size: 13.5px; font-weight: 700; color: var(--slate-900);">Manual Data Entry — Naksha Muntazamin</span>
        <span class="status-draft-badge">Draft Mode</span>
      </div>
      <button class="drawer-close-btn" onclick="closeModals()">&times;</button>
    </div>

    <!-- 3-Step Wizard Navigation -->
    <div class="entry-stepper">
      <div class="step-chip ${step === 'khata-sched' ? 'active' : 'completed'}">
        <span>1</span> <span>Khata &amp; Schedule</span>
      </div>
      <div class="step-chip ${step === 'new-sched' ? 'active' : (step === 'claimant' ? 'completed' : '')}">
        <span>2</span> <span>Configure Schedule</span>
      </div>
      <div class="step-chip ${step === 'claimant' ? 'active' : ''}">
        <span>3</span> <span>Claimant &amp; Finance</span>
      </div>
    </div>

    <div class="entry-body">
  `;

  if (step === 'khata-sched') {
    // Step 1: Select Khata & Schedule Group
    html += `
      <div class="form-section-card">
        <div class="form-section-title">
          <span>A. Select Revenue Khata</span>
          <span style="font-size: 10px; color: var(--slate-500);">Award 30/2002-03</span>
        </div>
        <div class="form-group">
          <label class="form-label">Revenue Khata Number <span class="req">*</span></label>
          <select class="form-select" id="manualKhataSelect">
            <option value="104min" selected>Khata 104min (Gross Stated: 51-13-00 B-B • 2 Schedules)</option>
            <option value="4">Khata 4 (Gross Stated: 11-02-00 B-B • 1 Schedule)</option>
            <option value="1min">Khata 1min (Gross Stated: 18-04-00 B-B • 1 Schedule)</option>
            <option value="7">Khata 7 (Gross Stated: 08-16-00 B-B • 2 Schedules)</option>
            <option value="+new">+ Create New Revenue Khata...</option>
          </select>
        </div>
      </div>

      <div class="form-section-card" style="border-left: 3px solid var(--brand-600);">
        <div class="form-section-title">
          <span>B. Select Khasra Schedule Group</span>
          <span style="font-size: 10px; color: var(--brand-600); font-weight: 700;">MANDATORY STEP</span>
        </div>
        <p style="font-size: 11px; color: var(--slate-600); margin-bottom: 8px;">
          Claimant entries resolve exclusively across their assigned Khasra schedule. Select an existing schedule or create an independent one.
        </p>

        <div style="display: flex; flex-direction: column; gap: 6px;">
          <!-- Option 1: Schedule B -->
          <label style="display: flex; align-items: flex-start; gap: 8px; padding: 6px 8px; background: #ffffff; border: 1px solid var(--brand-500); border-radius: 4px; cursor: pointer;">
            <input type="radio" name="schedChoice" checked style="margin-top: 3px;">
            <div style="flex: 1;">
              <div style="display: flex; justify-content: space-between;">
                <strong style="font-size: 11.5px; color: var(--slate-900);">Schedule B: Proprietary Body Co-sharers</strong>
                <span class="share-pill" style="font-size: 10px;">44-07-00 B-B</span>
              </div>
              <div style="font-size: 10.5px; color: var(--slate-600); margin-top: 2px;">
                Constituent Khasras (11): <code>2//1</code> to <code>2//10</code> (43-00) + <code>2//23</code> (1-07).
              </div>
            </div>
          </label>

          <!-- Option 2: Schedule A -->
          <label style="display: flex; align-items: flex-start; gap: 8px; padding: 6px 8px; background: #ffffff; border: 1px solid var(--slate-300); border-radius: 4px; cursor: pointer;">
            <input type="radio" name="schedChoice" style="margin-top: 3px;">
            <div style="flex: 1;">
              <div style="display: flex; justify-content: space-between;">
                <strong style="font-size: 11.5px; color: var(--slate-900);">Schedule A: Gram Sabha Public Land</strong>
                <span class="share-pill" style="font-size: 10px;">1-07-00 B-B</span>
              </div>
              <div style="font-size: 10.5px; color: var(--slate-600); margin-top: 2px;">
                Constituent Khasras (1): <code>2//23</code> (1-07 Rosli). Public utility holding.
              </div>
            </div>
          </label>

          <!-- Option 3: Create New Schedule -->
          <label style="display: flex; align-items: center; gap: 8px; padding: 6px 8px; background: #f8fafc; border: 1px dashed var(--slate-400); border-radius: 4px; cursor: pointer;">
            <input type="radio" name="schedChoice" style="margin-top: 1px;">
            <span style="font-size: 11px; font-weight: 600; color: var(--brand-700);">+ Create New Khasra Schedule Group under Khata 104min...</span>
          </label>
        </div>
      </div>

      <div class="legal-advisory-box">
        <strong>Office Validation Rule:</strong> Never attach all Khasras in a Khata to every claimant. A single Khata can legitimately hold distinct public, partitioned, or co-sharer schedules.
      </div>
    `;

    html += `
      </div>
      <div class="entry-footer">
        <span style="font-size: 11px; color: var(--slate-500);">Press Tab to navigate fields</span>
        <div style="display: flex; gap: 8px;">
          <button class="action-btn secondary" onclick="closeModals()">Cancel</button>
          <button class="action-btn primary" onclick="window.setDesignState('11-manual-nm-claimant-financial')">
            <span>Proceed to Claimant Entry &rarr;</span>
          </button>
        </div>
      </div>
    `;
  } else if (step === 'new-sched') {
    // Step 2: Configure New Schedule Group (Multi-Khasra Grid)
    html += `
      <div class="form-section-card">
        <div class="form-section-title">
          <span>Schedule Group Definition</span>
          <span class="share-pill" style="font-size: 10px;">Khata 104min</span>
        </div>
        <div class="form-grid-2">
          <div class="form-group">
            <label class="form-label">Schedule Identifier <span class="req">*</span></label>
            <input type="text" class="form-input" value="Schedule C (Partitioned Cohort)">
          </div>
          <div class="form-group">
            <label class="form-label">Stated Schedule Area <span class="req">*</span></label>
            <input type="text" class="form-input mono" value="07-06-00">
          </div>
        </div>
      </div>

      <div class="form-section-card" style="border-left: 3px solid var(--brand-600);">
        <div class="form-section-title">
          <span>Constituent Khasras Grid</span>
          <button class="row-action-btn" type="button" style="font-size: 10px;">+ Add Khasra Row</button>
        </div>

        <table style="width: 100%; border-collapse: collapse; font-size: 10.5px; margin-top: 4px;">
          <thead>
            <tr style="background: var(--slate-200); text-align: left;">
              <th style="padding: 4px 6px;">Khasra #</th>
              <th style="padding: 4px 6px;">Area (B-B)</th>
              <th style="padding: 4px 6px;">Land Class</th>
              <th style="padding: 4px 6px; text-align: right;">Circle Rate</th>
              <th style="padding: 4px 6px; text-align: center;">Act</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td style="padding: 3px 6px;"><input type="text" class="form-input mono" value="6//1" style="width: 70px; height: 22px;"></td>
              <td style="padding: 3px 6px;"><input type="text" class="form-input mono" value="3-10-00" style="width: 80px; height: 22px;"></td>
              <td style="padding: 3px 6px;">
                <select class="form-select" style="height: 22px; font-size: 10px;">
                  <option selected>Rosli (Block A)</option>
                  <option>Dakar (Block B)</option>
                </select>
              </td>
              <td style="padding: 3px 6px; text-align: right; font-family: var(--font-mono);">₹4,65,000</td>
              <td style="padding: 3px 6px; text-align: center; color: var(--red-600); cursor: pointer;">✕</td>
            </tr>
            <tr>
              <td style="padding: 3px 6px;"><input type="text" class="form-input mono" value="7//5/1-2" style="width: 70px; height: 22px;"></td>
              <td style="padding: 3px 6px;"><input type="text" class="form-input mono" value="3-16-00" style="width: 80px; height: 22px;"></td>
              <td style="padding: 3px 6px;">
                <select class="form-select" style="height: 22px; font-size: 10px;">
                  <option selected>Rosli (Block A)</option>
                  <option>Dakar (Block B)</option>
                </select>
              </td>
              <td style="padding: 3px 6px; text-align: right; font-family: var(--font-mono);">₹4,65,000</td>
              <td style="padding: 3px 6px; text-align: center; color: var(--red-600); cursor: pointer;">✕</td>
            </tr>
          </tbody>
        </table>

        <div style="margin-top: 8px; padding: 4px 8px; background: #ecfdf5; border: 1px solid #a7f3d0; border-radius: 4px; display: flex; justify-content: space-between; font-size: 11px;">
          <span style="color: #065f46;"><strong>Sum of Khasra Extents:</strong> 7-06-00 B-B</span>
          <span style="color: #047857; font-weight: 700;">✓ Matches Stated Schedule Area</span>
        </div>
      </div>

      <div class="legal-advisory-box">
        <strong>Schedule Integrity:</strong> Khasras linked here will form the exact legal denominator for all claimants added to Schedule C.
      </div>
    `;

    html += `
      </div>
      <div class="entry-footer">
        <button class="action-btn secondary" onclick="window.setDesignState('9-manual-nm-khata-schedule')">&larr; Back</button>
        <button class="action-btn primary" onclick="window.setDesignState('11-manual-nm-claimant-financial')">
          <span>Save Schedule &amp; Add Claimants &rarr;</span>
        </button>
      </div>
    `;
  } else if (step === 'claimant') {
    // Step 3: Individual Claimant Financial Entry
    html += `
      <div class="form-section-card" style="background: #f0fdf4; border: 1px solid #bbf7d0;">
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <span style="font-size: 11px; font-weight: 700; color: #166534;">
            Active Target: Khata 104min &bull; Schedule B (Proprietary Body)
          </span>
          <span class="share-pill" style="font-size: 10px;">Area: 44-07-00 B-B</span>
        </div>
      </div>

      <!-- Claimant Identity Section -->
      <div class="form-section-card">
        <div class="form-section-title">Claimant Identification</div>
        <div class="form-grid-3">
          <div class="form-group">
            <label class="form-label">Item Number <span class="req">*</span></label>
            <input type="text" class="form-input mono" value="28" style="font-weight: 700;">
          </div>
          <div class="form-group" style="grid-column: span 2;">
            <label class="form-label">Claimant Full Name <span class="req">*</span></label>
            <input type="text" class="form-input" value="Rajesh Kapoor (Demo)">
          </div>
        </div>
        <div class="form-grid-2" style="margin-top: 6px;">
          <div class="form-group">
            <label class="form-label">Parentage / Relation <span class="req">*</span></label>
            <input type="text" class="form-input" value="S/o T. N. Kapoor">
          </div>
          <div class="form-group">
            <label class="form-label">Share Ratio (Num / Denom) <span class="req">*</span></label>
            <div style="display: flex; align-items: center; gap: 4px;">
              <input type="text" class="form-input mono" value="12" style="width: 50px; text-align: center;">
              <span>/</span>
              <input type="text" class="form-input mono" value="20660" style="width: 75px; text-align: center;">
            </div>
          </div>
        </div>
      </div>

      <!-- Financial Calculation Section -->
      <div class="form-section-card" style="border-left: 3px solid var(--brand-600);">
        <div class="form-section-title">
          <span>Statutory Financial Components (LAA 1894)</span>
          <span style="font-size: 9.5px; color: var(--brand-600);">Auto-Computed</span>
        </div>

        <div class="form-grid-2">
          <div class="form-group">
            <label class="form-label">Allocated Area</label>
            <input type="text" class="form-input mono" value="0-00-11 B-B-B" readonly style="background: #f1f5f9;">
          </div>
          <div class="form-group">
            <label class="form-label">Land Compensation (A)</label>
            <input type="text" class="form-input currency" value="₹12,150.00">
          </div>
        </div>

        <div class="form-grid-2" style="margin-top: 6px;">
          <div class="form-group">
            <label class="form-label">Structure Specification</label>
            <input type="text" class="form-input" value="Tubewell Boring 100ft">
          </div>
          <div class="form-group">
            <label class="form-label">Structure Val (B)</label>
            <input type="text" class="form-input currency" value="₹3,500.00">
          </div>
        </div>

        <div class="form-grid-3" style="margin-top: 6px; padding: 6px 0; border-top: 1px dashed var(--slate-300);">
          <div class="form-group">
            <label class="form-label">Solatium 30%</label>
            <input type="text" class="form-input currency" value="₹4,695.00" readonly style="background: #f1f5f9;">
          </div>
          <div class="form-group">
            <label class="form-label">Sec 23(1A) 12%</label>
            <input type="text" class="form-input currency" value="₹2,895.00" readonly style="background: #f1f5f9;">
          </div>
          <div class="form-group">
            <label class="form-label">Sec 34 Int 9%</label>
            <input type="text" class="form-input currency" value="₹1,831.00" readonly style="background: #f1f5f9;">
          </div>
        </div>

        <div style="margin-top: 6px; padding: 6px 8px; background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 4px; display: flex; justify-content: space-between; align-items: center;">
          <span style="font-weight: 700; color: #1e3a8a; font-size: 11px;">FINAL NET PAYABLE:</span>
          <span style="font-family: var(--font-mono); font-size: 13.5px; font-weight: 800; color: #1d4ed8;">₹25,071.00</span>
        </div>
      </div>

      <!-- Administrative Remarks Section -->
      <div class="form-section-card">
        <div class="form-grid-2">
          <div class="form-group">
            <label class="form-label">Administrative Remarks</label>
            <input type="text" class="form-input" value="Original mutation verified">
          </div>
          <div class="form-group">
            <label class="form-label">Gazette Source Ref</label>
            <input type="text" class="form-input mono" value="Pt-2 p.148 r.5">
          </div>
        </div>
      </div>
    `;

    html += `
      </div>
      <div class="entry-footer">
        <button class="action-btn secondary" onclick="closeModals()">Cancel</button>
        <div style="display: flex; gap: 6px;">
          <button class="action-btn secondary" onclick="alert('Draft Saved. Ready for Next Entry.')">
            <span>💾 Save Draft</span>
          </button>
          <button class="action-btn primary" onclick="alert('Item 28 Staged as Draft. Review Required.')">
            <span>✓ Save &amp; Add Next</span>
          </button>
        </div>
      </div>
    `;
  }

  dom.entryDrawer.innerHTML = html;
}

// ============================================================================
// EXCEL TEMPLATE DOWNLOAD DIALOG CONTROLLER (State 12)
// ============================================================================
function renderExcelTemplateModal() {
  closeModals();
  dom.modalTemplateDownload.classList.remove('closed');

  let html = `
    <div style="background: var(--slate-50); border: 1px solid var(--slate-200); border-radius: 6px; padding: 8px 12px; font-size: 11.5px; color: var(--slate-700);">
      <strong>Award Context:</strong> Award No. 30/2002-03 &bull; Village Pochanpur &bull; Multi-Schedule Schema v2.1
      <p style="margin-top: 3px; font-size: 11px; color: var(--slate-500);">
        This workbook uses stable <code>Schedule_Group_ID</code> relationships. Do not use Khata Number alone or free-text "-do-" as linking keys.
      </p>
    </div>

    <!-- Sheet 1 Preview -->
    <div class="sheet-card">
      <div class="sheet-header">
        <span>Sheet 1: <code>NM_Items</code> (Claimant Entitlements &amp; Financial Ledger)</span>
        <span style="font-size: 10px; color: var(--slate-500);">16 Columns</span>
      </div>
      <table class="sheet-table">
        <thead>
          <tr>
            <th>Item_No</th>
            <th>Khata_No</th>
            <th>Schedule_Group_ID</th>
            <th>Claimant_Name</th>
            <th>Parentage</th>
            <th>Share_Num</th>
            <th>Share_Denom</th>
            <th>Land_Comp</th>
            <th>Structure_Val</th>
            <th>Total_Payable</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td style="font-family: var(--font-mono); font-weight: 600;">25</td>
            <td style="font-family: var(--font-mono);">104min</td>
            <td style="font-family: var(--font-mono); color: var(--brand-700); font-weight: 600;">SCHED-104-B</td>
            <td>Rohan Kapoor (Demo)</td>
            <td>S/o T. N. Kapoor</td>
            <td style="font-family: var(--font-mono);">12</td>
            <td style="font-family: var(--font-mono);">20660</td>
            <td style="font-family: var(--font-mono);">12150.00</td>
            <td style="font-family: var(--font-mono);">2400.00</td>
            <td style="font-family: var(--font-mono); font-weight: 700; color: var(--brand-700);">24130.00</td>
          </tr>
          <tr>
            <td style="font-family: var(--font-mono); font-weight: 600;">26</td>
            <td style="font-family: var(--font-mono);">104min</td>
            <td style="font-family: var(--font-mono); color: var(--brand-700); font-weight: 600;">SCHED-104-B</td>
            <td>Pawan Kapoor (Demo)</td>
            <td>S/o T. N. Kapoor</td>
            <td style="font-family: var(--font-mono);">12</td>
            <td style="font-family: var(--font-mono);">20660</td>
            <td style="font-family: var(--font-mono);">12150.00</td>
            <td style="font-family: var(--font-mono);">0.00</td>
            <td style="font-family: var(--font-mono); font-weight: 700; color: var(--brand-700);">20148.00</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Sheet 2 Preview -->
    <div class="sheet-card">
      <div class="sheet-header">
        <span>Sheet 2: <code>Khasra_Schedules</code> (Cadastral Parcel Definition per Schedule Group)</span>
        <span style="font-size: 10px; color: var(--slate-500);">6 Columns</span>
      </div>
      <table class="sheet-table">
        <thead>
          <tr>
            <th>Khata_No</th>
            <th>Schedule_Group_ID</th>
            <th>Schedule_Title</th>
            <th>Khasra_No</th>
            <th>Khasra_Area_BB</th>
            <th>Land_Class</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td style="font-family: var(--font-mono);">104min</td>
            <td style="font-family: var(--font-mono); color: var(--brand-700); font-weight: 600;">SCHED-104-A</td>
            <td>Gram Sabha Public Land</td>
            <td style="font-family: var(--font-mono);">2//23</td>
            <td style="font-family: var(--font-mono);">1-07-00</td>
            <td>Rosli (Block A)</td>
          </tr>
          <tr>
            <td style="font-family: var(--font-mono);">104min</td>
            <td style="font-family: var(--font-mono); color: var(--brand-700); font-weight: 600;">SCHED-104-B</td>
            <td>Proprietary Body Co-sharers</td>
            <td style="font-family: var(--font-mono);">2//1</td>
            <td style="font-family: var(--font-mono);">4-16-00</td>
            <td>Rosli (Block A)</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Sheet 3 Preview -->
    <div class="sheet-card">
      <div class="sheet-header">
        <span>Sheet 3: <code>Instructions_ValidValues</code> (Data Dictionary &amp; Validation Rules)</span>
        <span style="font-size: 10px; color: var(--slate-500);">Guidelines</span>
      </div>
      <div style="padding: 8px 12px; font-size: 10.5px; color: var(--slate-700); line-height: 1.45;">
        • <strong>Unique Item Numbers:</strong> Each Item_No must be strictly unique within the Award.<br>
        • <strong>Schedule References:</strong> Every claimant row must reference a valid <code>Schedule_Group_ID</code> defined in Sheet 2.<br>
        • <strong>Share Format:</strong> Provide integer Share_Num and Share_Denom (e.g. 12 and 20660). Zero denominators are rejected.<br>
        • <strong>No Total Duplication:</strong> Parcel extents are defined once in Sheet 2; do not duplicate totals per claimant.
      </div>
    </div>
  `;

  dom.templateModalBody.innerHTML = html;
}

// ============================================================================
// EXCEL IMPORT WORKFLOW CONTROLLER (States 13 & 14)
// ============================================================================
function renderExcelImportModal(mode) {
  closeModals();
  dom.modalExcelImport.classList.remove('closed');

  if (mode === 'valid') {
    dom.importModalTitle.innerHTML = `<span>📥 Excel Import Validation — Staging Preview</span>`;

    let html = `
      <!-- Validation Summary Bar -->
      <div class="import-summary-bar">
        <div style="display: flex; gap: 14px;">
          <div class="import-metric">File: <strong>Pochanpur_NM_Batch_04.xlsx</strong> (84.2 KB)</div>
          <div class="import-metric">Total Rows: <strong>32</strong></div>
          <div class="import-metric">Valid: <strong style="color: #047857;">32 ✓</strong></div>
          <div class="import-metric">Errors: <strong>0</strong></div>
          <div class="import-metric">Conflicts: <strong>0</strong></div>
        </div>
        <span class="share-pill" style="background: #ecfdf5; color: #047857; border-color: #a7f3d0; font-size: 10.5px;">
          VALIDATION PASSED &bull; READY TO STAGE
        </span>
      </div>

      <div style="font-size: 11px; color: var(--slate-600);">
        Rows successfully parsed and verified against Award 30/2002-03 cadastral boundaries. Review the staged hierarchy below before confirming.
      </div>

      <!-- Grouped Staging Table -->
      <div style="border: 1px solid var(--slate-300); border-radius: 6px; overflow: hidden; max-height: 280px; overflow-y: auto;">
        <table class="sheet-table">
          <thead>
            <tr>
              <th style="width: 50px;">Item #</th>
              <th>Khata</th>
              <th>Target Schedule</th>
              <th>Claimant Name &amp; Parentage</th>
              <th style="text-align: center;">Share</th>
              <th style="text-align: right;">Alloc Area</th>
              <th style="text-align: right;">Land Comp</th>
              <th style="text-align: right;">Net Total</th>
              <th style="text-align: center;">Import Action</th>
            </tr>
          </thead>
          <tbody>
            <tr style="background: #f1f5f9; font-weight: 600;">
              <td colspan="9">📁 Khata 104min &rarr; Schedule B (Proprietary Body Co-sharers — 11 Khasras, 44-07 B-B)</td>
            </tr>
            <tr>
              <td style="font-family: var(--font-mono); font-weight: 700;">28</td>
              <td style="font-family: var(--font-mono);">104min</td>
              <td style="font-family: var(--font-mono); color: var(--brand-700);">SCHED-104-B</td>
              <td>Rajesh Kapoor (Demo) S/o T. N. Kapoor</td>
              <td style="text-align: center; font-family: var(--font-mono);">12/20660</td>
              <td style="text-align: right; font-family: var(--font-mono);">0-00-11</td>
              <td style="text-align: right; font-family: var(--font-mono);">₹12,150</td>
              <td style="text-align: right; font-family: var(--font-mono); font-weight: 700; color: var(--brand-700);">₹24,130</td>
              <td style="text-align: center;"><span class="status-draft-badge" style="font-size: 9.5px;">Stage Draft</span></td>
            </tr>
            <tr>
              <td style="font-family: var(--font-mono); font-weight: 700;">29</td>
              <td style="font-family: var(--font-mono);">104min</td>
              <td style="font-family: var(--font-mono); color: var(--brand-700);">SCHED-104-B</td>
              <td>Mukesh Kapoor (Demo) S/o T. N. Kapoor</td>
              <td style="text-align: center; font-family: var(--font-mono);">12/20660</td>
              <td style="text-align: right; font-family: var(--font-mono);">0-00-11</td>
              <td style="text-align: right; font-family: var(--font-mono);">₹12,150</td>
              <td style="text-align: right; font-family: var(--font-mono); font-weight: 700; color: var(--brand-700);">₹20,148</td>
              <td style="text-align: center;"><span class="status-draft-badge" style="font-size: 9.5px;">Stage Draft</span></td>
            </tr>
            <tr style="background: #f1f5f9; font-weight: 600;">
              <td colspan="9">📁 Khata 4 &rarr; Schedule 1 (Main Parcel Group — 4 Khasras, 11-02 B-B)</td>
            </tr>
            <tr>
              <td style="font-family: var(--font-mono); font-weight: 700;">15</td>
              <td style="font-family: var(--font-mono);">4</td>
              <td style="font-family: var(--font-mono); color: var(--brand-700);">SCHED-4-MAIN</td>
              <td>Satish Verma (Demo) S/o B. L. Verma</td>
              <td style="text-align: center; font-family: var(--font-mono);">1/9</td>
              <td style="text-align: right; font-family: var(--font-mono);">1-04-13</td>
              <td style="text-align: right; font-family: var(--font-mono);">₹5,75,640</td>
              <td style="text-align: right; font-family: var(--font-mono); font-weight: 700; color: var(--brand-700);">₹9,54,411</td>
              <td style="text-align: center;"><span class="status-draft-badge" style="font-size: 9.5px;">Stage Draft</span></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="legal-advisory-box">
        <strong>Staging Safety Gate:</strong> Imported records will be created in <strong>DRAFT (Unverified)</strong> status under batch reference <code>EX-2026-B04</code>. Officers must inspect and certify records before financial disbursement.
      </div>
    `;

    let footerHtml = `
      <div style="font-size: 11px; color: var(--slate-600);">
        Ready to import <strong>32 validated records</strong> into draft register.
      </div>
      <div style="display: flex; gap: 8px;">
        <button class="action-btn secondary" onclick="closeModals()">Cancel</button>
        <button class="action-btn primary" onclick="alert('32 records staged in DRAFT mode.')">
          <span>Confirm &amp; Stage 32 Records as Draft &rarr;</span>
        </button>
      </div>
    `;

    dom.importModalBody.innerHTML = html;
    dom.importModalFooter.innerHTML = footerHtml;

  } else if (mode === 'conflicts') {
    dom.importModalTitle.innerHTML = `<span>⚠️ Excel Import: Validation Errors &amp; Conflict Resolution</span>`;

    let html = `
      <!-- Validation Summary Bar -->
      <div class="import-summary-bar" style="border-color: #fca5a5; background: #fff5f5;">
        <div style="display: flex; gap: 14px;">
          <div class="import-metric">File: <strong>Pochanpur_NM_Batch_05_Errors.xlsx</strong></div>
          <div class="import-metric">Total Rows: <strong>36</strong></div>
          <div class="import-metric">Valid: <strong style="color: #047857;">28 ✓</strong></div>
          <div class="import-metric">Errors: <strong style="color: #b91c1c;">5 ✕</strong></div>
          <div class="import-metric">Conflicts: <strong style="color: #b45309;">3 ⚠️</strong></div>
        </div>
        <span class="row-error-badge">ACTION REQUIRED &bull; ERRORS DETECTED</span>
      </div>

      <div style="font-size: 11px; color: #991b1b; font-weight: 600;">
        8 rows failed validation and cannot be imported directly. Review row-level defects below:
      </div>

      <!-- Error & Conflict Table -->
      <div style="border: 1px solid var(--slate-300); border-radius: 6px; overflow: hidden; max-height: 280px; overflow-y: auto;">
        <table class="sheet-table">
          <thead>
            <tr>
              <th style="width: 50px;">Row #</th>
              <th>Item #</th>
              <th>Khata</th>
              <th>Schedule Ref</th>
              <th>Claimant Name</th>
              <th>Defect / Conflict Description</th>
              <th style="text-align: center;">Recommended Action</th>
            </tr>
          </thead>
          <tbody>
            <tr style="background: #fffbeb;">
              <td style="font-family: var(--font-mono); font-weight: 700;">14</td>
              <td style="font-family: var(--font-mono); font-weight: 700; color: #b45309;">Item 10</td>
              <td>Khata 4</td>
              <td>SCHED-4-MAIN</td>
              <td>Anand Verma (Demo)</td>
              <td>
                <span class="row-error-badge" style="background: #fef3c7; color: #92400e;">Existing Item Conflict</span>
                Item #10 already exists in Award 30/2002-03. Overwrite blocked.
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Skip / Re-number</span></td>
            </tr>
            <tr style="background: #fff5f5;">
              <td style="font-family: var(--font-mono); font-weight: 700;">22</td>
              <td style="font-family: var(--font-mono); font-weight: 700;">Item 34</td>
              <td>Khata 104min</td>
              <td style="color: #b91c1c; font-weight: 700;">SCHED-Z</td>
              <td>Kiran Patel (Demo)</td>
              <td>
                <span class="row-error-badge">Unknown Schedule</span>
                'SCHED-Z' does not exist in Sheet 2 or Khata 104min.
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Fix Schedule ID</span></td>
            </tr>
            <tr style="background: #fff5f5;">
              <td style="font-family: var(--font-mono); font-weight: 700;">29</td>
              <td style="font-family: var(--font-mono); font-weight: 700;">Item 41</td>
              <td>Khata 104min</td>
              <td>SCHED-104-B</td>
              <td>Usha Gupta (Demo)</td>
              <td>
                <span class="row-error-badge">Invalid Share '0/0'</span>
                Share denominator cannot be zero.
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Correct Ratio</span></td>
            </tr>
            <tr style="background: #fff5f5;">
              <td style="font-family: var(--font-mono); font-weight: 700;">35</td>
              <td style="font-family: var(--font-mono); font-weight: 700;">Item 47</td>
              <td>Khata 7</td>
              <td>SCHED-7-ROSLI</td>
              <td>Deepak Rao (Demo)</td>
              <td>
                <span class="row-error-badge">Invalid Area '5-22-00'</span>
                Biswa value '22' exceeds statutory maximum (19).
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Correct to B-B</span></td>
            </tr>
          </tbody>
        </table>
      </div>

      <div style="background: var(--slate-50); border: 1px solid var(--slate-300); border-radius: 4px; padding: 8px 12px; font-size: 11px;">
        <span style="font-weight: 700; color: var(--slate-800);">Batch Handling Options:</span>
        <div style="display: flex; gap: 16px; margin-top: 4px;">
          <label style="cursor: pointer;"><input type="radio" name="errorAction" checked> <strong>Stage 28 Valid Rows</strong> (Quarantine 8 errored rows in audit log)</label>
          <label style="cursor: pointer;"><input type="radio" name="errorAction"> <strong>Abort Import</strong> (Re-upload corrected workbook)</label>
        </div>
      </div>
    `;

    let footerHtml = `
      <div style="display: flex; gap: 8px;">
        <button class="action-btn secondary" onclick="alert('Downloading Error Report: Pochanpur_Errors_Batch05.csv')">
          <span>⬇ Download Error Report (CSV)</span>
        </button>
      </div>
      <div style="display: flex; gap: 8px;">
        <button class="action-btn secondary" onclick="closeModals()">Cancel</button>
        <button class="action-btn primary" onclick="alert('28 valid rows staged. 8 errored rows quarantined.')">
          <span>Stage 28 Valid Rows (Quarantine Errors) &rarr;</span>
        </button>
      </div>
    `;

    dom.importModalBody.innerHTML = html;
    dom.importModalFooter.innerHTML = footerHtml;
  } else if (mode === 'award-conflicts') {
    dom.importModalTitle.innerHTML = `<span>⚠️ Award Khasra Import: Village Master Cross-Check &amp; Conflict Resolution</span>`;

    let html = `
      <!-- Validation Summary Bar -->
      <div class="import-summary-bar" style="border-color: #fca5a5; background: #fff5f5;">
        <div style="display: flex; gap: 14px;">
          <div class="import-metric">File: <strong>Pochanpur_Award_Khasras_Import.xlsx</strong></div>
          <div class="import-metric">Total Rows: <strong>14</strong></div>
          <div class="import-metric">Valid: <strong style="color: #047857;">10 ✓</strong></div>
          <div class="import-metric">Errors: <strong style="color: #b91c1c;">2 ✕</strong></div>
          <div class="import-metric">Conflicts: <strong style="color: #b45309;">2 ⚠️</strong></div>
        </div>
        <span class="row-error-badge">VILLAGE MASTER CONFLICTS DETECTED</span>
      </div>

      <div style="font-size: 11px; color: #991b1b; font-weight: 600;">
        4 Khasra records require officer resolution before acquisition linkage can proceed:
      </div>

      <!-- Error & Conflict Table -->
      <div style="border: 1px solid var(--slate-300); border-radius: 6px; overflow: hidden; max-height: 280px; overflow-y: auto;">
        <table class="sheet-table">
          <thead>
            <tr>
              <th style="width: 50px;">Row #</th>
              <th>Khasra #</th>
              <th>File Area (B-B)</th>
              <th>Village Master Record</th>
              <th>Defect / Conflict Description</th>
              <th style="text-align: center;">Recommended Action</th>
            </tr>
          </thead>
          <tbody>
            <tr style="background: #fff5f5;">
              <td style="font-family: var(--font-mono); font-weight: 700;">4</td>
              <td style="font-family: var(--font-mono); font-weight: 700; color: #b91c1c;">6//10</td>
              <td style="font-family: var(--font-mono); font-weight: 700;">5-00-00</td>
              <td>Total Extent: <strong>4-12-00 B-B</strong></td>
              <td>
                <span class="row-error-badge">Area Exceeds Boundary</span>
                Awarded area 5-00-00 exceeds Village Master total parcel boundary (4-12-00).
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Cap to 4-12-00</span></td>
            </tr>
            <tr style="background: #fff5f5;">
              <td style="font-family: var(--font-mono); font-weight: 700;">7</td>
              <td style="font-family: var(--font-mono); font-weight: 700; color: #b91c1c;">99//1</td>
              <td style="font-family: var(--font-mono);">4-16-00</td>
              <td style="color: #b91c1c; font-weight: 600;">Not Found in Cadastre</td>
              <td>
                <span class="row-error-badge">Unknown Khasra</span>
                Parcel 99//1 does not exist in Village Pochanpur canonical master.
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Verify Cadastral Map</span></td>
            </tr>
            <tr style="background: #fffbeb;">
              <td style="font-family: var(--font-mono); font-weight: 700;">11</td>
              <td style="font-family: var(--font-mono); font-weight: 700; color: #b45309;">6//2</td>
              <td style="font-family: var(--font-mono);">3-00-00</td>
              <td>Active in Award 30/2002-03</td>
              <td>
                <span class="row-error-badge" style="background: #fef3c7; color: #92400e;">Existing Award Link</span>
                Khasra 6//2 already acquired in Award 30. Overwrite blocked.
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: var(--slate-600);">Skip Duplicate</span></td>
            </tr>
            <tr style="background: #ecfdf5;">
              <td style="font-family: var(--font-mono); font-weight: 700;">14</td>
              <td style="font-family: var(--font-mono); font-weight: 700; color: #047857;">7//5/3</td>
              <td style="font-family: var(--font-mono);">0-15-00</td>
              <td>Total Extent: 0-15-00 B-B (Unlinked)</td>
              <td>
                <span class="status-draft-badge" style="font-size: 9.5px;">Valid Cadastral Match</span>
                Matches unlinked boundary in Village Master. Ready to stage.
              </td>
              <td style="text-align: center;"><span style="font-size: 10px; color: #047857; font-weight: 600;">Stage as Draft</span></td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Batch Actions -->
      <div class="form-section-card" style="margin-top: 10px; padding: 10px 12px;">
        <div style="font-size: 11px; font-weight: 700; color: var(--slate-700); margin-bottom: 6px;">Batch Handling Options:</div>
        <div style="display: flex; gap: 20px;">
          <label style="display: flex; align-items: center; gap: 6px; font-size: 11px; cursor: pointer;">
            <input type="radio" name="awardImportAction" checked>
            <span><strong>Stage 10 Valid Khasras</strong> (Quarantine 4 conflicting rows in audit log)</span>
          </label>
          <label style="display: flex; align-items: center; gap: 6px; font-size: 11px; cursor: pointer;">
            <input type="radio" name="awardImportAction">
            <span><strong>Abort Import</strong> (Re-upload corrected workbook)</span>
          </label>
        </div>
      </div>
    `;

    let footerHtml = `
      <div style="display: flex; gap: 8px;">
        <button class="action-btn secondary" onclick="alert('Downloading Error Report: Pochanpur_Award_Khasra_Errors.csv')">
          <span>⬇ Download Error Report (CSV)</span>
        </button>
      </div>
      <div style="display: flex; gap: 8px;">
        <button class="action-btn secondary" onclick="closeModals()">Cancel</button>
        <button class="action-btn primary" onclick="alert('10 valid Khasras staged in DRAFT mode.')">
          <span>Stage 10 Valid Khasras (Quarantine Conflicts) &rarr;</span>
        </button>
      </div>
    `;

    dom.importModalBody.innerHTML = html;
    dom.importModalFooter.innerHTML = footerHtml;
  }
}

// ============================================================================
// AWARD WORKSPACE CONTROLLER (States 15 & 16)
// ============================================================================
function renderAwardWorkspace(mode) {
  dom.nmWorkspace.style.display = 'none';
  dom.awardWorkspace.style.display = 'flex';
  dom.breadcrumbWorkspaceTitle.textContent = 'Award Master & Khasra Ledger';
  dom.btnClassicView.classList.remove('active');
  dom.btnAwardView.classList.add('active');

  let rowsHtml = '';
  AWARD_KHASRAS.forEach(ak => {
    rowsHtml += `
      <tr class="claimant-row">
        <td class="sticky-col-item item-num">${ak.sr}</td>
        <td class="sticky-col-claimant"><strong style="font-family: var(--font-mono);">${ak.khasra}</strong></td>
        <td class="sticky-col-share center">${ak.village}</td>

        <td class="num">${ak.awardedArea}</td>
        <td class="center"><span class="class-badge ${ak.landClass.includes('Rosli') ? 'rosli' : 'dakar'}">${ak.landClass}</span></td>
        <td class="center"><span style="color: #047857; font-size: 10.5px; font-weight: 600;">✓ ${ak.vmStatus}</span></td>

        <td class="num">${formatINRNoDec(ak.circleRate)}</td>
        <td class="num" style="border-right: 2px solid var(--slate-300); font-weight: 600;">${formatINR(ak.landComp)}</td>

        <td class="center" style="font-family: var(--font-mono); font-size: 10.5px;">${ak.sec4Date}</td>
        <td class="center" style="font-family: var(--font-mono); font-size: 10.5px; border-right: 2px solid var(--slate-300);">${ak.possDate}</td>

        <td class="center" style="font-family: var(--font-mono); font-size: 10.5px;">${ak.gazetteRef}</td>
        <td class="center"><span class="share-pill" style="font-size: 10px;">${ak.entryMethod}</span></td>
        <td class="center"><button class="row-action-btn" type="button">Edit</button></td>
      </tr>
    `;
  });

  dom.awardTableBody.innerHTML = rowsHtml;

  if (mode === 'manual-entry') {
    dom.awardEntryDrawer.classList.remove('closed');
    dom.awardEntryDrawer.innerHTML = `
      <div class="entry-drawer-header">
        <div>
          <span style="font-size: 13.5px; font-weight: 700; color: var(--slate-900);">Manual Entry — Award Khasra Record</span>
          <div style="font-size: 10.5px; color: var(--slate-500);">Award 30/2002-03 &bull; Village Pochanpur</div>
        </div>
        <button class="drawer-close-btn" onclick="closeModals()">&times;</button>
      </div>

      <div class="entry-body">
        <div class="form-section-card">
          <div class="form-section-title">Cadastral Identification</div>
          <div class="form-grid-2">
            <div class="form-group">
              <label class="form-label">Khasra Number <span class="req">*</span></label>
              <input type="text" class="form-input mono" value="6//1" style="font-weight: 700;">
            </div>
            <div class="form-group">
              <label class="form-label">Awarded Area (B-B) <span class="req">*</span></label>
              <input type="text" class="form-input mono" value="3-10-00">
            </div>
          </div>
          <div class="form-grid-2" style="margin-top: 6px;">
            <div class="form-group">
              <label class="form-label">Land Classification <span class="req">*</span></label>
              <select class="form-select">
                <option selected>Rosli (Block A @ ₹4,65,000)</option>
                <option>Dakar (Block B @ ₹3,95,000)</option>
              </select>
            </div>
            <div class="form-group">
              <label class="form-label">Gazette Page Ref</label>
              <input type="text" class="form-input mono" value="P.17 Para 4">
            </div>
          </div>
        </div>

        <div class="form-section-card" style="border-left: 3px solid #059669; background: #ecfdf5;">
          <div class="form-section-title" style="color: #065f46;">Village Master Cadastral Cross-Check</div>
          <div style="font-size: 11px; color: #047857; line-height: 1.45;">
            ✓ <strong>Khasra 6//1 confirmed in Village Master:</strong> Total extent recorded as <code>3-10-00 B-B</code>.<br>
            Current status: Unlinked in Award 30/2002-03. Ready for acquisition linkage.
          </div>
        </div>

        <div class="form-section-card">
          <div class="form-section-title">Statutory Possession Dates</div>
          <div class="form-grid-2">
            <div class="form-group">
              <label class="form-label">Sec 4 Notification Date</label>
              <input type="text" class="form-input mono" value="13.12.2000" readonly style="background: #f1f5f9;">
            </div>
            <div class="form-group">
              <label class="form-label">Possession Date</label>
              <input type="text" class="form-input mono" value="10.04.2002">
            </div>
          </div>
        </div>

        <div class="legal-advisory-box">
          <strong>Non-Destructive Guarantee:</strong> Submitting an Award Khasra record links acquisition metadata to the Village Master without altering underlying cadastral survey boundaries.
        </div>
      </div>

      <div class="entry-footer">
        <button class="action-btn secondary" onclick="closeModals()">Cancel</button>
        <div style="display: flex; gap: 6px;">
          <button class="action-btn secondary" onclick="alert('Award Khasra saved as Draft.')">Save Draft</button>
          <button class="action-btn primary" onclick="alert('Khasra 6//1 Linked to Award 30.')">Save &amp; Link Khasra</button>
        </div>
      </div>
    `;
  } else if (mode === 'excel-import') {
    renderExcelImportModal('award-conflicts');
  }
}

// ============================================================================
// CANONICAL 16 DESIGN STATES CONTROLLER
// ============================================================================
window.setDesignState = function(stateName) {
  APP_STATE.activeState = stateName;
  dom.qaStateLabel.textContent = stateName;

  dom.qaButtons.forEach(btn => {
    btn.classList.toggle('active', btn.dataset.state === stateName);
  });

  // Default to NM workspace
  dom.nmWorkspace.style.display = 'flex';
  dom.awardWorkspace.style.display = 'none';
  dom.breadcrumbWorkspaceTitle.textContent = 'Naksha Muntazamin (NM) Register';
  dom.btnClassicView.classList.add('active');
  dom.btnAwardView.classList.remove('active');

  closeModals();
  dom.searchInput.value = '';
  APP_STATE.searchTerm = '';
  APP_STATE.activeFilter = 'all';
  dom.filterPills.forEach(p => p.classList.toggle('active', p.dataset.filter === 'all'));

  switch(stateName) {
    case '1-collapsed-khatas':
      NM_DATA.forEach(k => k.isExpanded = false);
      APP_STATE.isDrawerOpen = false;
      break;

    case '2-expanded-khata-4':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 4));
      APP_STATE.isDrawerOpen = false;
      break;

    case '3-claimant-financial-details':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 4));
      APP_STATE.selectedKhata = NM_DATA.find(k => k.khataId === 4);
      APP_STATE.selectedSchedule = APP_STATE.selectedKhata.scheduleGroups[0];
      APP_STATE.selectedClaimant = APP_STATE.selectedSchedule.claimants.find(c => c.itemId === 10);
      APP_STATE.activeDrawerTab = 'statutory';
      APP_STATE.isDrawerOpen = true;
      break;

    case '4-item-search':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      dom.searchInput.value = 'Item 25';
      APP_STATE.searchTerm = 'Item 25';
      APP_STATE.isDrawerOpen = false;
      break;

    case '5-mixed-land-class':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 7));
      APP_STATE.selectedKhata = NM_DATA.find(k => k.khataId === 7);
      APP_STATE.selectedSchedule = APP_STATE.selectedKhata.scheduleGroups[0];
      APP_STATE.selectedClaimant = APP_STATE.selectedSchedule.claimants[0];
      APP_STATE.activeDrawerTab = 'statutory';
      APP_STATE.isDrawerOpen = true;
      break;

    case '6-red-handwritten-remarks':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 4));
      APP_STATE.selectedKhata = NM_DATA.find(k => k.khataId === 4);
      APP_STATE.selectedSchedule = APP_STATE.selectedKhata.scheduleGroups[0];
      APP_STATE.selectedClaimant = APP_STATE.selectedSchedule.claimants.find(c => c.itemId === 14);
      APP_STATE.activeDrawerTab = 'source';
      APP_STATE.isDrawerOpen = true;
      break;

    case '7-mismatch-review-state':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      APP_STATE.selectedKhata = NM_DATA.find(k => k.khataId === 104);
      APP_STATE.selectedSchedule = APP_STATE.selectedKhata.scheduleGroups[1];
      APP_STATE.selectedClaimant = APP_STATE.selectedSchedule.claimants[2]; // Item 27 (Ambiguous)
      APP_STATE.activeDrawerTab = 'parcels';
      APP_STATE.isDrawerOpen = true;
      break;

    // --- NEW OFFICE DATA ENTRY STATES ---
    case '8-nm-entry-actions':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 4));
      APP_STATE.isDrawerOpen = false;
      break;

    case '9-manual-nm-khata-schedule':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      renderManualEntryDrawer('khata-sched');
      break;

    case '10-manual-nm-new-schedule':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      renderManualEntryDrawer('new-sched');
      break;

    case '11-manual-nm-claimant-financial':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      renderManualEntryDrawer('claimant');
      break;

    case '12-excel-template-download':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 4));
      renderExcelTemplateModal();
      break;

    case '13-excel-import-preview-valid':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      renderExcelImportModal('valid');
      break;

    case '14-excel-import-conflicts':
      NM_DATA.forEach(k => k.isExpanded = (k.khataId === 104));
      renderExcelImportModal('conflicts');
      break;

    case '15-award-manual-entry':
      renderAwardWorkspace('manual-entry');
      break;

    case '16-award-excel-import':
      renderAwardWorkspace('excel-import');
      break;
  }

  renderTable();
  renderDrawer();
};

// Bind UI actions
dom.btnOpenManualEntry.addEventListener('click', () => window.setDesignState('9-manual-nm-khata-schedule'));
dom.btnOpenExcelImport.addEventListener('click', () => window.setDesignState('13-excel-import-preview-valid'));
dom.btnOpenTemplateDownload.addEventListener('click', () => window.setDesignState('12-excel-template-download'));

dom.btnClassicView.addEventListener('click', () => window.setDesignState('1-collapsed-khatas'));
dom.btnAwardView.addEventListener('click', () => window.setDesignState('15-award-manual-entry'));

dom.qaButtons.forEach(btn => {
  btn.addEventListener('click', () => window.setDesignState(btn.dataset.state));
});

// Initial boot
window.setDesignState('1-collapsed-khatas');
