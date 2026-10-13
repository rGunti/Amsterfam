import { TimelineUser } from './timeline';

export interface NewsPost {
  id: number;
  title: string | null;
  /** Raw Markdown. */
  body: string;
  author: TimelineUser;
  createdAt: string;
  /** When the post goes (or went) out. */
  publishAt: string;
  /** Null while the post is scheduled; only organisers see it then. */
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
  /** Set when the post has an image; changes when the image does. Fetch it with auth. */
  imageFileId: string | null;
  /** Members can add comments; existing ones stay readable either way. */
  commentsEnabled: boolean;
  commentCount: number;
  /** Other people's comments since the viewer last opened the feed. */
  newCommentCount: number;
  /** The post goes (or went) out to the event's outside channels, e.g. Discord. */
  notify: boolean;
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
  /** Defaults to true on the server. */
  commentsEnabled?: boolean;
  /**
   * More than a minute ahead schedules the post; null or sooner publishes it now. Can't
   * change once the post is out.
   */
  publishAt?: string | null;
  /**
   * Mirror the post to the event's outside channels when it's published. Defaults to true;
   * ignored once the post is out, since edits never notify.
   */
  notify?: boolean;
}

/** A plain-text comment on a post, oldest first in a list. */
export interface NewsComment {
  id: number;
  author: TimelineUser;
  body: string;
  createdAt: string;
  editedAt: string | null;
  /** The viewer wrote it and the event isn't read-only. */
  canEdit: boolean;
  /** The viewer wrote it or is an organiser, and the event isn't read-only. */
  canDelete: boolean;
  /** Only emoji with at least one reaction, in {@link NEWS_REACTIONS} order. */
  reactions: NewsReactionCount[];
  myReactions: string[];
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

/** Whether the viewer has a private feed link. The link itself is only shown when created. */
export interface NewsFeedTokenStatus {
  active: boolean;
  createdAt: string | null;
  lastUsedAt: string | null;
}

export interface NewsFeedTokenCreated {
  url: string;
  createdAt: string;
}

/** The fixed reaction set; the server rejects anything else. */
export const NEWS_REACTIONS = ['👍', '❤️', '😂', '🎉', '😮', '🙏'] as const;

export const NEWS_TITLE_MAX = 120;
export const NEWS_BODY_MAX = 4000;
export const NEWS_COMMENT_MAX = 2000;
