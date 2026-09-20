export type JoinLinkKind = 'Attendee' | 'Organiser';

export interface JoinLinkResponse {
  id: number;
  token: string;
  kind: JoinLinkKind;
  /** Organiser-chosen name, or "Attendee link" / "Organiser link" when unset. */
  label: string;
  createdById: number;
  createdAt: string;
  expiresAt: string | null;
  maxUses: number | null;
  useCount: number;
  isRevoked: boolean;
  isUsable: boolean;
}

export interface CreateJoinLinkRequest {
  kind: JoinLinkKind;
  expiresAt: string | null;
  maxUses: number | null;
  label: string | null;
}

export interface JoinLinkPreviewResponse {
  eventId: string;
  eventName: string;
  location: string;
  startDate: string | null;
  endDate: string | null;
  kind: JoinLinkKind;
  alreadyMember: boolean;
}
