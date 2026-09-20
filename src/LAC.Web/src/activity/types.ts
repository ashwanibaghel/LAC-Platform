export interface ActivityItemDto {
  eventId: string;
  sourceType: 'Dak' | 'Matter' | 'Outward' | 'WorkItem' | 'RecordAccess';
  action: string;
  summary: string;
  occurredAt: string;
  actorUserId: string;
  actorDisplayName: string;
  entityType: 'Dak' | 'Matter' | 'Outward' | 'WorkItem' | 'Document';
  entityId: string;
  entityTitle: string;
  entityReferenceNumber?: string | null;
  workstreamId?: string | null;
  workstreamName?: string | null;
  deskId?: string | null;
  deskName?: string | null;
  canOpen: boolean;
  navigationUrl?: string | null;
  isReadEvent: boolean;
  metadata?: Record<string, any> | null;
}

export interface ActivityFeedResult {
  items: ActivityItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface FilterOptionDto {
  id: string;
  name: string;
}

export interface TeamFilterOptionsDto {
  workstreams: FilterOptionDto[];
  desks: FilterOptionDto[];
  actors: FilterOptionDto[];
}
