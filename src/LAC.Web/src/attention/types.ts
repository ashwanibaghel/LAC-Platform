export type ScheduledEventKind =
  | 'CourtHearing'
  | 'ComplianceDeadline'
  | 'SiteInspection'
  | 'Meeting'
  | 'OrderDelivery'
  | 'CompensationDisbursement'
  | 'ReportSubmission'
  | 'NoticeExpiry'
  | 'Other';

export type ScheduledEventPriority = 'Routine' | 'Urgent' | 'Immediate';

export type ScheduledEventStatus =
  | 'Scheduled'
  | 'Rescheduled'
  | 'InAttendance'
  | 'Completed'
  | 'Cancelled'
  | 'Adjourned';

export type AttentionSourceType = 'ScheduledEvent' | 'WorkItemDue' | 'DakDue';

export interface FilterOption {
  id: string;
  name: string;
  code?: string;
}

export interface AttentionSummary {
  overdue: number;
  today: number;
  tomorrow: number;
  next7Days: number;
  reminderActive: number;
  needsRouting: number;
  // UI aliases
  overdueCount?: number;
  todayCount?: number;
  tomorrowCount?: number;
  next7DaysCount?: number;
  reminderActiveCount?: number;
  needsRoutingCount?: number;
  totalActiveCount?: number;
}

export interface AttentionContext {
  entityType: string;
  entityId: string;
  title?: string | null;
  referenceNumber?: string | null;
  canOpen: boolean;
  navigationUrl?: string | null;
}

export interface AttentionItemCapabilities {
  canView?: boolean;
  canReschedule?: boolean;
  canReassign?: boolean;
  canComplete?: boolean;
  canCancel?: boolean;
  canManageReminders?: boolean;
  canLinkWorkItem?: boolean;
}

export interface AttentionItem {
  id: string;
  sourceType: AttentionSourceType;
  title: string;
  dueState: string;
  scheduledDate?: string | null;
  scheduledTime?: string | null;
  dueAt?: string | null;
  workstreamId?: string | null;
  workstreamCode?: string | null;
  workstreamName?: string | null;
  responsibleDeskId?: string | null;
  responsibleDeskCode?: string | null;
  responsibleDeskName?: string | null;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  priority: string;
  status: string;
  eventKind?: string | null;
  buckets: string[];
  isReminderActive: boolean;
  needsRouting: boolean;
  lastActivityAt: string;
  context?: AttentionContext | null;
  revision?: number | null;

  // Compatibility accessors
  sourceEntityId?: string;
  capabilities?: AttentionItemCapabilities;
  description?: string | null;
  remindersActive?: boolean;
  isAssignedToCaller?: boolean;
  activeRemindersCount?: number;
}

export interface AttentionFeedResponse {
  summary: AttentionSummary;
  items: AttentionItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  workstreamOptions?: FilterOption[];
  deskOptions?: FilterOption[];
  workstreams?: FilterOption[];
  desks?: FilterOption[];
}

export interface ScheduledReminderDto {
  id: string;
  daysBefore: number;
  reminderTime?: string | null;
  targetReminderDate?: string | null;
  note?: string | null;
  isActive: boolean;
  createdByUserId: string;
  createdByDisplayName?: string | null;
}

export interface ScheduledEventEventDto {
  id: string;
  sequenceNumber: number;
  action: string;
  actionAt: string;
  occurredAt?: string;
  actorUserId: string;
  actorDisplayName: string;
  actorDisplayNameSnapshot?: string | null;
  actorDesignation?: string | null;
  actorDesignationSnapshot?: string | null;
  workstreamName?: string | null;
  sourceDeskName?: string | null;
  targetDeskName?: string | null;
  sourceUserDisplayName?: string | null;
  targetUserDisplayName?: string | null;
  oldScheduledDate?: string | null;
  oldScheduledTime?: string | null;
  newScheduledDate?: string | null;
  newScheduledTime?: string | null;
  reminderDaysBefore?: number | null;
  linkedWorkItemId?: string | null;
  reason?: string | null;
  notes?: string | null;
  remarks?: string | null;
}

export interface ScheduledEventCapabilitiesDto {
  canReschedule: boolean;
  canReassign: boolean;
  canUpdate: boolean;
  canComplete: boolean;
  canCancel: boolean;
  canManageReminders: boolean;
  canLinkWorkItem: boolean;
  canView?: boolean;
}

export interface ScheduledEventDetail {
  id: string;
  workstreamId: string;
  workstreamCode: string;
  workstreamName: string;
  responsibleOfficeDeskId?: string | null;
  responsibleOfficeDeskCode?: string | null;
  responsibleOfficeDeskName?: string | null;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  eventKind: string;
  title: string;
  description?: string | null;
  scheduledDate: string;
  scheduledTime?: string | null;
  priority: string;
  status: string;
  revision: number;
  createdByUserId: string;
  createdByDisplayName?: string | null;
  createdByDisplayNameSnapshot?: string | null;
  createdByDesignation?: string | null;
  createdByDesignationSnapshot?: string | null;
  createdAt: string;
  lastActivityAt: string;
  completedAt?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
  origin: string;
  reminders: ScheduledReminderDto[];
  history: ScheduledEventEventDto[];
  matterContext?: AttentionContext | null;
  dakContext?: AttentionContext | null;
  outwardContext?: AttentionContext | null;
  workItemContext?: AttentionContext | null;
  courtCaseContext?: AttentionContext | null;
  courtProceedingContext?: AttentionContext | null;
  capabilities: ScheduledEventCapabilitiesDto;
}

export interface CalendarEventDto {
  id: string;
  eventKind?: string | null;
  title: string;
  scheduledDate: string;
  scheduledTime?: string | null;
  priority: string;
  status: string;
  workstreamId?: string | null;
  workstreamName?: string | null;
  responsibleDeskId?: string | null;
  responsibleDeskName?: string | null;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  activeRemindersCount?: number;
  isReminderActive?: boolean;
  canOpen?: boolean;
}

export interface ScheduleCalendarResult {
  from?: string;
  to?: string;
  events?: CalendarEventDto[];
  summary?: AttentionSummary;
  items?: AttentionItem[];
}

export interface ScheduleDeskMemberOption {
  userId: string;
  username: string;
  displayName: string;
  designationName?: string | null;
}

export interface ScheduleDeskOption {
  id: string;
  code: string;
  name: string;
  workstreamId?: string | null;
  members: ScheduleDeskMemberOption[];
}

export interface ScheduleOptionsResponse {
  workstreams: FilterOption[];
  desks: ScheduleDeskOption[];
}
