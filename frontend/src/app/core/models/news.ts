import { TimelineUser } from './timeline';

export interface NewsPost {
  id: number;
  title: string | null;
  /** Raw Markdown. */
  body: string;
  author: TimelineUser;
  createdAt: string;
  publishedAt: string | null;
  editedAt: string | null;
  canEdit: boolean;
  /** Set while the post is pinned; pinned posts lead the feed. */
  pinnedAt: string | null;
  /** The post asks every member for a "Got it". */
  requiresAck: boolean;
  /** False for the post's author, who has nothing to acknowledge. */
  canAcknowledge: boolean;
  acknowledgedByMe: boolean;
  /** Only emoji with at least one reaction, in {@link NEWS_REACTIONS} order. */
  reactions: NewsReactionCount[];
  myReactions: string[];
  /** Organisers only, and only on posts that ask for acknowledgement. */
  ackCount: number | null;
  ackTotal: number | null;
}

export interface NewsReactionCount {
  emoji: string;
  count: number;
}

export interface NewsFeed {
  /** Newest first. */
  posts: NewsPost[];
  /** When the viewer last opened the feed; later posts are new to them. */
  seenAt: string | null;
}

export interface UpsertNewsPostRequest {
  title: string | null;
  body: string;
  requiresAck?: boolean;
  /** Create only: pin the new post straight away. */
  pin?: boolean;
  /** Edit only: clear everyone's acknowledgements so they confirm again. */
  resetAcks?: boolean;
}

export interface NewsAcks {
  acknowledged: { user: TimelineUser; at: string }[];
  /** Current confirmed members who haven't acknowledged yet. */
  missing: TimelineUser[];
}

/** For the event overview. A post can appear in more than one list. */
export interface NewsSummary {
  pinned: NewsPost[];
  latest: NewsPost | null;
  needsAck: NewsPost[];
}

/** The fixed reaction set; the server rejects anything else. */
export const NEWS_REACTIONS = ['👍', '❤️', '😂', '🎉', '😮', '🙏'] as const;

export const NEWS_TITLE_MAX = 120;
export const NEWS_BODY_MAX = 4000;
