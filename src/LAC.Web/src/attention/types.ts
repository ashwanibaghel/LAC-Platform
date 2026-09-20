export type ScheduledEventKind =
  | 'Hearing'
  | 'ComplianceDeadline'
  | 'SiteInspection'
  | 'Meeting'
  | 'OrderDelivery'
  | 'CompensationDisbursement'
  | 'ReportSubmission'
  | 'NoticeExpiry'
  | 'Other';

export type ScheduledEventPriority = 'Low' | 'Medium' | 'High' | 'Urgent';

export type ScheduledEventStatus =
  | 'Draft'
  | 'Scheduled'
  | 'Rescheduled'
  | 'InAttendance'
  | 'Completed'
  | 'Cancelled'
  | 'Adjourned';

export type AttentionSourceType = 'ScheduledEvent' | 'WorkItemDue' | 'DakDue';

export interface FilterOption {
  id: string;
  code: string;
  name: string;
}

export interface AttentionSummary {
  overdueCount: number;
  todayCount: number;
  tomorrowCount: number;
  next7DaysCount: number;
  reminderActiveCount: number;
  totalActiveCount: number;
  needsRoutingCount: number;
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
  canView: boolean;
  canReschedule: boolean;
  canReassign: boolean;
  canComplete: boolean;
  canCancel: boolean;
  canManageReminders: boolean;
  canLinkWorkItem: boolean;
}

export interface AttentionItem {
  id: string;
  sourceType: AttentionSourceType;
  sourceEntityId: string;
  title: string;
  description?: string | null;
  scheduledDate: string;
  scheduledTime?: string | null;
  priority: string;
  status: string;
  workstreamId: string;
  workstreamName: string;
  responsibleDeskId?: string | null;
  responsibleDeskName?: string | null;
  assignedUserId?: string | null;
  assignedUserName?: string | null;
  assignedUserDisplayName?: string | null;
  isAssignedToCaller: boolean;
  isCallerDeskMember: boolean;
  buckets: string[];
  remindersActive: boolean;
  activeRemindersCount: number;
  earliestActiveReminderDate?: string | null;
  context?: AttentionContext | null;
  capabilities: AttentionItemCapabilities;
}

export interface AttentionFeedResponse {
  summary: AttentionSummary;
  items: AttentionItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  workstreams: FilterOption[];
  desks: FilterOption[];
}

export interface ScheduledReminderDto {
  id: string;
  daysBefore: number;
  targetReminderDate: string;
  note?: string | null;
  isActive: boolean;
  createdAt: string;
  createdByName?: string | null;
}

export interface ScheduledEventHistoryDto {
  id: string;
  action: string;
  occurredAt: string;
  actorUserId: string;
  actorDisplayNameSnapshot?: string | null;
  actorDesignationSnapshot?: string | null;
  fromStatus?: string | null;
  toStatus?: string | null;
  fromScheduledDate?: string | null;
  toScheduledDate?: string | null;
  fromDeskId?: string | null;
  toDeskId?: string | null;
  fromDeskNameSnapshot?: string | null;
  toDeskNameSnapshot?: string | null;
  fromUserId?: string | null;
  toUserId?: string | null;
  fromUserDisplayNameSnapshot?: string | null;
  toUserDisplayNameSnapshot?: string | null;
  remarks?: string | null;
  revision: number;
}

export interface ScheduledEventContextDto {
  matter?: AttentionContext | null;
  dak?: AttentionContext | null;
  outward?: AttentionContext | null;
  workItem?: AttentionContext | null;
  courtProceeding?: AttentionContext | null;
}

export interface ScheduledEventCapabilitiesDto {
  canView: boolean;
  canReschedule: boolean;
  canReassign: boolean;
  canComplete: boolean;
  canCancel: boolean;
  canManageReminders: boolean;
  canLinkWorkItem: boolean;
}

export interface ScheduledEventDetail {
  id: string;
  workstreamId: string;
  workstreamName: string;
  responsibleOfficeDeskId?: string | null;
  responsibleOfficeDeskName?: string | null;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  eventKind: ScheduledEventKind;
  title: string;
  description?: string | null;
  scheduledDate: string;
  scheduledTime?: string | null;
  priority: ScheduledEventPriority;
  status: ScheduledEventStatus;
  revision: number;
  createdByDisplayNameSnapshot?: string | null;
  createdByDesignationSnapshot?: string | null;
  createdAt: string;
  completedAt?: string | null;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
  reminders: ScheduledReminderDto[];
  history: ScheduledEventHistoryDto[];
  context: ScheduledEventContextDto;
  capabilities: ScheduledEventCapabilitiesDto;
}

export interface CalendarEventDto {
  id: string;
  eventKind: ScheduledEventKind;
  title: string;
  scheduledDate: string;
  scheduledTime?: string | null;
  priority: ScheduledEventPriority;
  status: ScheduledEventStatus;
  workstreamId: string;
  workstreamName: string;
  responsibleDeskId?: string | null;
  responsibleDeskName?: string | null;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  activeRemindersCount: number;
  canOpen: boolean;
}

export interface ScheduleCalendarResult {
  from: string;
  to: string;
  events: CalendarEventDto[];
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
